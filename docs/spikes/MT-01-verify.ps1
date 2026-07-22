<#
.SYNOPSIS
    MT-01 auth-model spike - verification kit. Run once real Mendix credentials
    are available; answers Part B of docs/spikes/MT-01-auth-model.md.

.DESCRIPTION
    READ-ONLY by default. Steps that touch a real environment (create snapshot,
    create archive) run only with explicit switches AND a typed confirmation of
    the environment name. Nothing here is ever destructive (no restore, no
    transport, no start/stop).

    SECRETS: credentials come from environment variables only. NEVER hardcode
    them in this file, never commit them, never paste raw output containing
    them into the repo. All output from this script redacts the key/PAT.

    Compatible with Windows PowerShell 5.1 and PowerShell 7+. ASCII-only on
    purpose (5.1 misreads BOM-less UTF-8) - keep it that way.

.SETUP (current PowerShell session only - do not use setx, it persists):
    $env:MENDIX_USERNAME = "jeremy.voorn@..."     # Mendix platform login
    $env:MENDIX_API_KEY  = "<api key>"            # Mendix profile > API Keys
    $env:MENDIX_PAT      = "<pat>"                # optional; profile > Developer
                                                  # Settings > Personal Access
                                                  # Tokens, scope mx:deployment:read

.USAGE
    # Read-only pass (steps 1-5): list apps, envs, snapshots, PAT tests
    .\MT-01-verify.ps1

    # Pin a specific app/environment instead of the first one found:
    .\MT-01-verify.ps1 -AppId myapp -EnvironmentMode Test

    # Opt-in [ENV] steps - use a TEST app/environment, never customer production:
    .\MT-01-verify.ps1 -AppId myapp -EnvironmentMode Test -CreateSnapshot
    .\MT-01-verify.ps1 -AppId myapp -EnvironmentMode Test -CreateArchive
    .\MT-01-verify.ps1 -AppId myapp -EnvironmentMode Test -CreateArchive -DownloadArchive
#>
[CmdletBinding()]
param(
    [string]$AppId,                 # v1 AppId (subdomain name); default: first app returned
    [string]$EnvironmentMode,       # e.g. Test / Acceptance / Production; default: first env
    [switch]$CreateSnapshot,        # [ENV] POST a new snapshot (non-destructive, but real)
    [switch]$CreateArchive,         # [ENV] request a database_only archive of newest snapshot
    [switch]$DownloadArchive,       # with -CreateArchive: download + inspect the archive
    [switch]$RateLimitProbe         # fire 30 rapid GETs to look for HTTP 429 behaviour
)

$ErrorActionPreference = 'Stop'
$DeployV1  = 'https://deploy.mendix.com/api/1'
$BackupsV2 = 'https://deploy.mendix.com/api/v2'
$DeployV4  = 'https://cloud.home.mendix.com/api/v4'

# --- credentials (env vars only; never printed) -----------------------------
$MxUser = $env:MENDIX_USERNAME
$MxKey  = $env:MENDIX_API_KEY
$MxPat  = $env:MENDIX_PAT
if (-not $MxUser -or -not $MxKey) {
    throw 'Set $env:MENDIX_USERNAME and $env:MENDIX_API_KEY first (see .SETUP in this file). Do NOT hardcode them.'
}
$ApiKeyHeaders = @{ 'Mendix-Username' = $MxUser; 'Mendix-ApiKey' = $MxKey }

function Write-Step([string]$t)  { Write-Host ""; Write-Host "=== $t ===" -ForegroundColor Cyan }
function Write-Check([string]$t) { Write-Host "  CHECK: $t" -ForegroundColor Yellow }
function Write-Ok([string]$t)    { Write-Host "  OK: $t" -ForegroundColor Green }

# Invoke and report status without ever echoing headers (which hold secrets).
function Invoke-Api {
    param([string]$Method = 'GET', [string]$Uri, [hashtable]$Headers, $Body)
    try {
        $splat = @{ Method = $Method; Uri = $Uri; Headers = $Headers }
        if ($null -ne $Body) { $splat.Body = ($Body | ConvertTo-Json); $splat.ContentType = 'application/json' }
        $resp = Invoke-RestMethod @splat
        return @{ ok = $true; status = 200; body = $resp }
    } catch {
        $status = $null
        if ($_.Exception.Response) { $status = [int]$_.Exception.Response.StatusCode }
        return @{ ok = $false; status = $status; error = $_.Exception.Message }
    }
}

# Confirmation gate for [ENV] steps: must type the environment mode exactly.
function Confirm-EnvAction([string]$action, [string]$app, [string]$mode) {
    Write-Host ""
    Write-Host "About to $action on $app / $mode (a REAL Mendix Cloud environment)." -ForegroundColor Yellow
    $typed = Read-Host "Type the environment name '$mode' to proceed (anything else aborts)"
    if ($typed -cne $mode) { Write-Host '  Aborted.'; return $false }
    return $true
}

$results = [ordered]@{}

# ============================================================================
# STEP 1 - Deploy API v1: list apps (API key). Also MT-13's "Test" call.
# Expected: 200 + JSON array of { AppId, Name, ProjectId, Url }.
# ============================================================================
Write-Step 'STEP 1: GET /api/1/apps (Deploy v1, Mendix-Username + Mendix-ApiKey)'
$r = Invoke-Api -Uri "$DeployV1/apps" -Headers $ApiKeyHeaders
if (-not $r.ok) { throw "List apps failed (HTTP $($r.status)): $($r.error) - check username/API key and that the account has API Rights on at least one app." }
$apps = @($r.body)
Write-Ok "$($apps.Count) app(s) returned."
$apps | Select-Object AppId, Name, ProjectId, Url | Format-Table -AutoSize | Out-String | Write-Host
Write-Check 'Part B item 7 - do apps from ALL customer orgs appear, or only one org?'
Write-Check 'Field names exactly AppId/Name/ProjectId/Url? Any extras? (affects DTOs)'
$results.ListApps = 'PASS'

$app = if ($AppId) { $apps | Where-Object { $_.AppId -eq $AppId } } else { $apps[0] }
if (-not $app) { throw "AppId '$AppId' not found in the returned app list." }
Write-Host "  Using app: $($app.Name) (AppId=$($app.AppId), ProjectId=$($app.ProjectId))"

# ============================================================================
# STEP 2 - Deploy API v1: list environments.
# Expected: 200 + array of { Status, EnvironmentId, Url, Mode, ModelVersion,
#           MendixVersion, Production }. Status only Empty|Stopped|Running.
# ============================================================================
Write-Step "STEP 2: GET /api/1/apps/$($app.AppId)/environments"
$r = Invoke-Api -Uri "$DeployV1/apps/$($app.AppId)/environments" -Headers $ApiKeyHeaders
if (-not $r.ok) { throw "List environments failed (HTTP $($r.status)): $($r.error)" }
$envs = @($r.body)
Write-Ok "$($envs.Count) environment(s)."
Write-Host '  RAW payload (Part B item 4 - save this JSON for the field verdict):'
$envs | ConvertTo-Json -Depth 5 | Write-Host
Write-Check 'Documented fields only, or extras (region? db size?)? Update MT-01-auth-model.md field table.'
Write-Check 'Status values observed - confirm only Empty/Stopped/Running ever appear.'
$results.ListEnvironments = 'PASS'

$envSel = if ($EnvironmentMode) { $envs | Where-Object { $_.Mode -eq $EnvironmentMode } } else { $envs[0] }
if (-not $envSel) { throw "Environment mode '$EnvironmentMode' not found for $($app.AppId)." }
Write-Host "  Using environment: $($envSel.Mode) (EnvironmentId=$($envSel.EnvironmentId))"

# ============================================================================
# STEP 3 - Backups API v2: list snapshots (API key; note: ProjectId, not AppId).
# Expected: 200 + { snapshots: [ { snapshot_id, state, status_message,
#           model_version, comment, created_at, finished_at, updated_at,
#           expires_at } ] } - docs list NO size and NO manual/automatic type.
# ============================================================================
$snapBase = "$BackupsV2/apps/$($app.ProjectId)/environments/$($envSel.EnvironmentId)/snapshots"
Write-Step "STEP 3: GET $snapBase"
$r = Invoke-Api -Uri $snapBase -Headers $ApiKeyHeaders
if (-not $r.ok) {
    Write-Host "  FAILED (HTTP $($r.status)): $($r.error)" -ForegroundColor Red
    Write-Check 'If 403: the account lacks "Access to Backups" on this app (Sprintr > Environments > Permissions).'
    $results.ListSnapshots = "FAIL ($($r.status))"
} else {
    # Body shape may differ from docs ({snapshots:[...]} vs bare array) - count null-safe.
    $snapshots = @($r.body.snapshots | Where-Object { $null -ne $_ })
    if ($snapshots.Count -eq 0 -and $r.body -is [array]) { $snapshots = @($r.body | Where-Object { $null -ne $_ }) }
    Write-Ok "$($snapshots.Count) snapshot(s) in first page."
    Write-Host '  RAW response body (Part B item 2 - the Size/Type column question):'
    $r.body | ConvertTo-Json -Depth 6 | Write-Host
    Write-Check 'Is there any size field? Any automatic-vs-manual type marker? Decides MT-14 columns.'
    $results.ListSnapshots = 'PASS'
}

# ============================================================================
# STEP 4 - PAT positive test: Deploy API v4 GET /apps (Authorization: MxToken).
# Expected: 200 with PAT scope mx:deployment:read. Skipped without MENDIX_PAT.
# ============================================================================
Write-Step 'STEP 4: GET /api/v4/apps (Deploy v4, PAT / MxToken)'
if (-not $MxPat) {
    Write-Host '  SKIPPED - set $env:MENDIX_PAT to run (scope mx:deployment:read).'
    $results.PatV4 = 'SKIPPED'
} else {
    $r = Invoke-Api -Uri "$DeployV4/apps" -Headers @{ Authorization = "MxToken $MxPat" }
    if ($r.ok) {
        Write-Ok 'PAT accepted by Deploy API v4.'
        Write-Check 'Compare env fields later via /apps/{appId}/environments?expand=package (dbVersion, planName, package.runtimeVersion).'
        $results.PatV4 = 'PASS'
    } else {
        Write-Host "  FAILED (HTTP $($r.status)): $($r.error)" -ForegroundColor Red
        Write-Check 'If 403: PAT lacks mx:deployment:read scope, or wrong header casing (try mxtoken).'
        $results.PatV4 = "FAIL ($($r.status))"
    }
}

# ============================================================================
# STEP 5 - PAT negative test: MxToken against Deploy v1 (Part B item 1).
# Expected: 401/403 - proves old APIs reject PATs, confirming the D1 decision.
# ============================================================================
Write-Step 'STEP 5: GET /api/1/apps with MxToken (expect REJECTION)'
if (-not $MxPat) {
    Write-Host '  SKIPPED - needs $env:MENDIX_PAT.'
    $results.PatRejectedByV1 = 'SKIPPED'
} else {
    $r = Invoke-Api -Uri "$DeployV1/apps" -Headers @{ Authorization = "MxToken $MxPat" }
    if ($r.ok) {
        Write-Host '  UNEXPECTED: v1 accepted the PAT - this CONTRADICTS the docs; update MT-01-auth-model.md!' -ForegroundColor Red
        $results.PatRejectedByV1 = 'UNEXPECTED PASS - investigate'
    } else {
        Write-Ok "Rejected as expected (HTTP $($r.status)) - API-key-only confirmed for v1."
        $results.PatRejectedByV1 = "PASS (rejected: $($r.status))"
    }
}

# ============================================================================
# STEP 6 [ENV, opt-in] - Create snapshot (MT-15 dry run). Non-destructive but real.
# Expected: 200/201 + snapshot with state queued|running; poll to completed.
# ============================================================================
if ($CreateSnapshot) {
    Write-Step 'STEP 6 [ENV]: POST snapshot'
    if (Confirm-EnvAction 'CREATE A SNAPSHOT' $app.Name $envSel.Mode) {
        $r = Invoke-Api -Method POST -Uri $snapBase -Headers $ApiKeyHeaders -Body @{ comment = 'MT-01 spike verification - safe to delete' }
        if (-not $r.ok) {
            Write-Host "  FAILED (HTTP $($r.status)): $($r.error)" -ForegroundColor Red
            $results.CreateSnapshot = "FAIL ($($r.status))"
        } else {
            $snapId = $r.body.snapshot_id
            Write-Ok "Snapshot $snapId requested (state: $($r.body.state)). Polling every 15s (max 20 min)..."
            $deadline = (Get-Date).AddMinutes(20)
            do {
                Start-Sleep -Seconds 15
                $p = Invoke-Api -Uri "$snapBase/$snapId" -Headers $ApiKeyHeaders
                Write-Host "    state: $($p.body.state)"
            } while ($p.ok -and (@('queued','running') -contains $p.body.state) -and (Get-Date) -lt $deadline)
            Write-Check "Terminal state + status_message: $($p.body.state) / $($p.body.status_message)"
            $results.CreateSnapshot = "state=$($p.body.state)"
        }
    } else { $results.CreateSnapshot = 'ABORTED BY USER' }
}

# ============================================================================
# STEP 7 [ENV, opt-in] - Create database_only archive of the newest COMPLETED
# snapshot, poll, then HEAD the URL (MT-16 dry run; Part B items 5 and 6).
# ============================================================================
if ($CreateArchive) {
    Write-Step 'STEP 7 [ENV]: POST archive (data_type=database_only)'
    $r = Invoke-Api -Uri $snapBase -Headers $ApiKeyHeaders
    $snap = @($r.body.snapshots) | Where-Object { $_.state -eq 'completed' } | Select-Object -First 1
    if (-not $snap) {
        Write-Host '  No completed snapshot available - run -CreateSnapshot first.' -ForegroundColor Red
        $results.CreateArchive = 'NO SNAPSHOT'
    } elseif (Confirm-EnvAction 'CREATE A DB-ONLY ARCHIVE' $app.Name $envSel.Mode) {
        $r = Invoke-Api -Method POST -Uri "$snapBase/$($snap.snapshot_id)/archives?data_type=database_only" -Headers $ApiKeyHeaders
        if (-not $r.ok) {
            Write-Host "  FAILED (HTTP $($r.status)): $($r.error)" -ForegroundColor Red
            $results.CreateArchive = "FAIL ($($r.status))"
        } else {
            $archId = $r.body.archive_id
            Write-Ok "Archive $archId requested. Polling every 15s (max 30 min)..."
            $deadline = (Get-Date).AddMinutes(30)
            do {
                Start-Sleep -Seconds 15
                $p = Invoke-Api -Uri "$snapBase/$($snap.snapshot_id)/archives/$archId" -Headers $ApiKeyHeaders
                Write-Host "    state: $($p.body.state)"
            } while ($p.ok -and (@('queued','running') -contains $p.body.state) -and (Get-Date) -lt $deadline)
            if ($p.body.state -eq 'completed') {
                Write-Ok 'Archive completed.'
                Write-Host '  RAW archive payload (Part B item 5 - checksum question). WARNING: if the url embeds a token, treat the output as secret:'
                $p.body | ConvertTo-Json -Depth 5 | Write-Host
                Write-Check 'Any checksum/hash field? (Docs say no - confirm.)'
                # HEAD the download URL: Content-Length / ETag / Content-MD5?
                try {
                    $head = Invoke-WebRequest -Method Head -Uri $p.body.url -UseBasicParsing
                    Write-Host '  Download URL response headers:'
                    foreach ($h in $head.Headers.GetEnumerator()) { Write-Host "    $($h.Key): $($h.Value)" }
                    Write-Check 'Note Content-Length (backup size for MT-14/16), ETag/Content-MD5 (integrity), and expiry hints.'
                } catch {
                    Write-Host "  HEAD failed: $($_.Exception.Message) (some CDNs block HEAD - use -DownloadArchive instead)"
                }
                if ($DownloadArchive) {
                    $out = Join-Path ([IO.Path]::GetTempPath()) "mt01-archive-$archId"
                    Write-Host "  Downloading to $out ..."
                    Invoke-WebRequest -Uri $p.body.url -OutFile $out -UseBasicParsing
                    $fi = Get-Item $out
                    Write-Ok ("Downloaded {0:N0} bytes." -f $fi.Length)
                    Write-Check 'Part B item 6 - inspect layout: file type (tar.gz? plain .backup?), does db/*.backup exist? (7Zip or tar -tzf). Delete the file afterwards - it may contain customer data.'
                }
                $results.CreateArchive = 'PASS'
            } else {
                Write-Host "  Archive ended in state $($p.body.state): $($p.body.status_message)" -ForegroundColor Red
                $results.CreateArchive = "FAIL (state=$($p.body.state))"
            }
        }
    } else { $results.CreateArchive = 'ABORTED BY USER' }
}

# ============================================================================
# STEP 8 [opt-in] - Rate-limit probe (Part B item 3): 30 rapid list-apps GETs.
# Expected: unknown - record whether 429 appears and any Retry-After header.
# ============================================================================
if ($RateLimitProbe) {
    Write-Step 'STEP 8: rate-limit probe (30 rapid GET /api/1/apps)'
    $codes = @{}
    foreach ($i in 1..30) {
        $r = Invoke-Api -Uri "$DeployV1/apps" -Headers $ApiKeyHeaders
        $code = if ($r.ok) { 200 } else { $r.status }
        if ($codes.ContainsKey($code)) { $codes[$code] = $codes[$code] + 1 } else { $codes[$code] = 1 }
    }
    foreach ($kv in $codes.GetEnumerator()) { Write-Host "  HTTP $($kv.Key): $($kv.Value)x" }
    Write-Check 'Did 429 appear? If yes, re-run one failing call and record the Retry-After / ratelimit headers.'
    $results.RateLimitProbe = (($codes.GetEnumerator() | ForEach-Object { "$($_.Key)x$($_.Value)" }) -join ', ')
}

# ============================================================================
Write-Step 'SUMMARY (safe to paste into MT-01-auth-model.md - contains no secrets)'
foreach ($kv in $results.GetEnumerator()) { Write-Host ("  {0,-22} {1}" -f $kv.Key, $kv.Value) }
Write-Host ''
Write-Host 'Reminder: clear credentials when done:'
Write-Host '  Remove-Item Env:MENDIX_USERNAME, Env:MENDIX_API_KEY, Env:MENDIX_PAT -ErrorAction SilentlyContinue'
