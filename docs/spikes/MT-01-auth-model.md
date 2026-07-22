# MT-01 — Auth-model spike: findings & recommendation

> Story: MT-01 (N1, gating unknown D1). Researched 2026-07-22 against docs.mendix.com
> (and its source repo `mendix/docs`, branch `development`). **Updated 2026-07-22
> with live results:** Jeremy ran the read-only pass of `MT-01-verify.ps1` (same
> folder) twice — sandbox `app1099` and licensed `vanschiemagazijn` — all API-key
> steps PASS. Part A is conclusive from the official docs; Part B records the live
> findings and the few items deferred to MT-16.
>
> **Never commit credentials.** The verification script reads them from environment
> variables only; its output redacts them.

---

## Part A — What the official docs settle today

### A1. Authentication per API (the core question)

| API | Base URL | Auth mechanism | PAT accepted? | Source |
|---|---|---|---|---|
| **Deploy API v1** (apps, environments, upload ≤300 MB, transport, start/stop) | `https://deploy.mendix.com/api/1` | Headers `Mendix-Username` + `Mendix-ApiKey` | **No** — not documented | [Deploy API v1](https://docs.mendix.com/apidocs-mxsdk/apidocs/deploy-api/) |
| **Deploy API v2** (package upload ≤1 GB, job status) | `https://deploy.mendix.com/api/v2` | Headers `Mendix-Username` + `Mendix-ApiKey` | **No** | [Deploy API v2](https://docs.mendix.com/apidocs-mxsdk/apidocs/deploy-api-2/) |
| **Backups API v2** (snapshots list/create, archives create/poll/download) | `https://deploy.mendix.com/api/v2` | Headers `Mendix-Username` + `Mendix-ApiKey` | **No** | [Backups API v2](https://docs.mendix.com/apidocs-mxsdk/apidocs/backups-api/) |
| **Deploy API v4** (read-only apps + environments; team permissions; technical contact) | `https://cloud.home.mendix.com/api/v4` | Header `Authorization: MxToken <PAT>`, scopes `mx:deployment:read` / `mx:deployment:write` | **PAT only** | [Deploy API v4](https://docs.mendix.com/apidocs-mxsdk/apidocs/deploy-api-4/), OpenAPI spec `https://docs.mendix.com/openapi-spec/cloud-portal-v4.yaml` |

Verdicts that follow directly:

- **The ideas.md hypothesis is confirmed.** Backups API v2 and Deploy API v1/v2
  authenticate exclusively with `Mendix-Username` + `Mendix-ApiKey`. Only Deploy API
  v4 is PAT-based.
- **Deploy API v4 cannot power this product alone.** Its full OpenAPI spec contains
  only: `GET /apps`, `GET /apps/{appId}`, `PATCH /apps/{appId}` (technical contact),
  `GET /apps/{appId}/environments`, `GET /apps/{appId}/environments/{environmentId}`,
  and `GET/PATCH .../permissions`. **No backups, no snapshots, no package upload, no
  transport, no start/stop.** Everything the flagship flow needs lives in the
  API-key generation.
- **API keys are not deprecated.** The [Platform APIs index](https://docs.mendix.com/apidocs-mxsdk/apidocs/)
  and [Mendix Profile / User Settings](https://docs.mendix.com/mendix-profile/user-settings/)
  document both mechanisms as active, with no deprecation timeline anywhere. PATs are
  described as *preferred where supported* because they can be scope-restricted — but
  the APIs we need don't support them. API keys are created in the Mendix Profile
  ("API Keys" section, `https://user-settings.mendix.com/link/developersettings`;
  legacy link `https://projects.home.mendix.com/link/personalapikeys`) and are shown
  **only once** at creation.
- **PATs** are created in Mendix Profile → Developer Settings → Personal Access
  Tokens, **do not expire** (invalidated only by deletion or account deactivation),
  and are sent as `Authorization: MxToken {PAT}` (some docs write `mxtoken` —
  case-insensitivity to be confirmed live). The scope names in the designed mock
  (`mx:deploy`, `mx:backups`) **do not exist**; real deployment scopes are
  `mx:deployment:read` / `mx:deployment:write`, and there is **no backups scope at
  all** — consistent with backups having no PAT-based API.
- **Per-app permissions still gate everything**, independent of credential type: the
  calling user needs **API Rights** (all APIs), **Access to Backups** (Backups API),
  and **Transport Rights** (deploy) on each app's team — configured per app under
  Environments → Permissions in Sprintr. There is no credential-side scope that
  replaces this.
- **Security note the product must respect:** Deploy API v1 explicitly states it does
  **not** require the two-factor authentication the web UI enforces for production
  changes (already covered by vision principle 5 / D5 guard).

### A2. Backups API v2 — what MT-14/15/16 get

Endpoints (note: uses **ProjectId**, the GUID from `GET /api/1/apps`, not the AppId
subdomain string):

- List snapshots: `GET /api/v2/apps/{ProjectId}/environments/{EnvironmentId}/snapshots`
  (paginated via `offset`/`limit`, newest first)
- Create snapshot: `POST .../snapshots` — body `{ "comment": "..." }` (send `{}` if no comment)
- Create archive: `POST .../snapshots/{SnapshotId}/archives?data_type=<database_only|files_and_database>`
- Poll archive: `GET .../snapshots/{SnapshotId}/archives/{ArchiveId}`

Documented facts:

- **Snapshot fields:** `snapshot_id`, `state`, `status_message`, `model_version`,
  `comment`, `created_at`, `finished_at`, `updated_at`, `expires_at`.
  State machine: `queued → running → completed | failed` (map `completed` →
  "Available" badge).
- **No `size` field on snapshots** and **no `type` (Automatic/Manual) field** —
  documented nowhere and **confirmed absent in the live run** (Part B results §4).
  MT-14 cuts the Size column (size is knowable only at archive/download time via
  `Content-Length`); Type can be derived from `comment` or shown as the comment.
- **Archive fields:** `archive_id`, `state` (`queued → running → completed | failed`),
  `url` (present when completed), timestamps. **No checksum/hash of any kind** —
  MT-16's fallback (zip/tar integrity test + size check) is the real plan, not the
  fallback.
- **The 8-hour expiry claim from ideas.md is confirmed verbatim:** the archive
  download link "is valid for eight hours after completion".
- **`data_type=database_only` exists** (default is `files_and_database`) — see D4
  proposal below.
- Mendix Cloud only (other hosting returns `NOT_SUPPORTED`).

### A3. Deploy API — what MT-10/MT-20 (environments) and X3 (deploy) get

`GET /api/1/apps` → `AppId`, `Name`, `ProjectId`, `Url` per app (all apps the user
is a team member of, across customers — exactly the cross-customer view we want;
confirm live that it spans orgs, Part B item 7).

`GET /api/1/apps/{AppId}/environments` → per environment:
`Status` (**only** `Empty | Stopped | Running`), `EnvironmentId`, `Mode`
(Test/Acceptance/Production/flexible name), `Url`, `ModelVersion`, `MendixVersion`,
`Production` (bool).

Deploy API v4's environment object (for comparison): `id`, `appId`, `name`, `state`
(**only** `stopped | running | notdeployed`), `isProduction`, `url`, `dbVersion`
(database engine version), `planName` (e.g. `XL21-PREMIUM`), and an embedded
`package` (`modelVersion`, `runtimeVersion`, `fileName`, `fileSize`, `createdOn`).
Richer in places (plan name, runtime version, package metadata) but read-only and a
separate credential — not worth a second credential in v1.

**Field-by-field verdict vs `EnvironmentsScreen.jsx` card (for MT-10 code notes and MT-20 trim):**

| Mock card field | Available? | Source / note |
|---|---|---|
| Status badge Running/Stopped | **Yes** | v1 `Status` (live run observed exactly `Running`/`Stopped`) |
| Status "Degraded" | **No** | No API surface in v1 or v4. Trim. |
| Status "Deploying" | **No** (as a status) | v1 never returns it; could only be synthesized while *we* run a deploy job. Trim from wired card; job cards cover it. |
| Mendix version (mono) | **Yes** (licensed nodes) | v1 `MendixVersion`; **absent on sandboxes** — nullable in DTOs (Part B results §3) |
| Region (`eu-west-1`) | **No** | Not in v1 or v4. Trim (the `Url` host hints at it but is not a documented contract). |
| Host | **Yes** | v1 `Url` |
| DB size | **No** | Not in v1 or v4 (`planName` is plan capacity, not usage). Trim. |
| Last backup ("2h ago") | **Derivable** | Latest `created_at` from Backups API v2 — one extra call **per environment** (N+1). Recommend: lazy/cached, or cut from the card and show it on the Backups screen only. MT-20 decision. |
| Environment name/mode | **Yes** | v1 `Mode` |
| Production marker | **Yes** | v1 `Production` |

**Deploy/transport needs (X3):** all present in v1/v2 — upload package
(`POST /api/1/apps/{AppId}/packages/upload`, >300 MB → v2 endpoint, ≤1 GB), transport
(`POST /api/1/apps/{AppId}/environments/{Mode}/transport`), start with migrations
(`POST .../start` with `AutoSyncDb`, returns `JobId`), stop. Same API-key credential.

### A4. Rate limits

**Not documented** for Deploy v1/v2 or Backups v2 anywhere in the official reference.
Community reports ("Rate Limit of V2 API Exceeded", Mendix forum) prove server-side
limits exist but are unpublished. Product consequence: treat HTTP 429 as a first-class
response everywhere (backoff + retry), keep archive/snapshot polling modest (the docs
frame these as "very long-running tasks" — poll at seconds-to-tens-of-seconds, not
sub-second), and verify actual 429 behaviour/headers live (Part B item 3).

### A5. Archive contents (feeds D4 and MT-17)

Per [Restore a Backup Locally](https://docs.mendix.com/developerportal/operate/restore-backup-locally/):
a full backup archive is a `.tar.gz` containing a **`db/`** folder with the
PostgreSQL dump (`.backup` file, custom format — `pg_restore`-compatible) and a
**`tree/`** folder with FileDocuments (binary files). The documented local-restore
procedure restores only the `.backup`; FileDocuments are needed only to run the app
locally (a declared non-goal).

---

## Recommendation (proposed D1 — for the Visionair to record in `vision.md`)

**Target the API-key generation for everything in v1 of the product:** Deploy API v1
(+ v2 for >300 MB package uploads) and Backups API v2, authenticated with
`Mendix-Username` + `Mendix-ApiKey`. **One credential pair covers environments,
backups, and deploy** — no second credential needed. Skip Deploy API v4/PAT entirely
for now: it adds no capability we need (read-only env data only), and adopting it
would force users to configure *two* credentials for zero feature gain. Revisit only
if Mendix extends the PAT-based API family to backups/transport.

**Credentials tab shape (amends `SettingsScreen.jsx`):**

- **Two fields:** `Mendix username` (the platform login email, plain text field) and
  `API key` (masked, mono, reveal toggle) — replacing the single PAT field.
- **Remove** the "Scopes: mx:deploy, mx:backups" hint — those scopes don't exist.
  Replace with honest copy, e.g.: "Created in your Mendix profile under API Keys.
  Access per app is controlled by your team permissions (API Rights, Access to
  Backups, Transport Rights)."
- **Remove or repurpose the "API region" row** — both API hosts are fixed global
  endpoints (`deploy.mendix.com`); there is no region selection.
- **Rename "Revoke" → "Remove"** — the API provides no server-side key revocation;
  the button can only delete the credential from the OS vault. Revoking the key
  itself happens in the Mendix profile (link out).
- **"Test" button** = `GET /api/1/apps` (read-only, cheap, proves both headers).
- Both values stored in `SecureStorage` (username is not secret, but keeping the
  pair together in the vault is simpler and safer).

**Proposed D4 answer: confirmed — default to DB-only.** `data_type=database_only` is
real, documented, and the flagship flow (`pg_restore` into local Postgres) needs only
the `db/*.backup` file; `files_and_database` adds FileDocuments that only matter for
running the app locally (non-goal, killed idea 20). Smaller archives also soften the
8-hour URL window. Keep `files_and_database` out of the UI for v1 (or behind an
overflow option at most). One caveat to verify live: the internal layout of a
`database_only` archive (single `.backup`? still tar.gz-wrapped?) — Part B item 6;
MT-17's unpack step depends on it.

---

## Impact on stories

- **MT-13 (Credentials tab)** — unblocked once D1 is recorded. Build username +
  API-key fields per the shape above; Test = `GET /api/1/apps`; error mapping: 401 →
  "credential rejected", 403 → "this account lacks API Rights on any app / this app".
- **MT-14 (backups list)** — state mapping `completed→Available`, `queued/running→In
  progress`, `failed→Failed`. **Resolved by the live run (Part B results §4):** cut
  the Size column; derive Type from `comment` (or show the comment directly); render
  `failed` snapshots with their `status_message` — they occur in real data.
- **MT-15 (create snapshot)** — `POST snapshots` with optional comment; poll
  `state` until `completed|failed`; `status_message` is the failure reason to surface.
- **MT-16 (download)** — request archive with `data_type=database_only`; poll; 8-hour
  URL expiry confirmed → the "link expired — start the download again" path in the
  ACs is correct; **no API checksum exists** → ship the zip/tar-integrity + size
  fallback as the only mechanism; handle 429 with backoff.
- **MT-20 (wired environments)** — trim per the field table above: drop Degraded,
  Deploying, region, DB size; keep status/version/host/mode/production (version
  nullable — absent on sandboxes); decide lazy-vs-cut for "last backup". Add a
  **sandbox filter/grouping** — the live run shows the app list mixes licensed
  customer apps with personal sandboxes (Part B results §2). Bonus fields available
  if ever wanted: `Instances`, `MemoryPerInstance`, `TotalMemory`, `RuntimeLayer`.
- **MT-10 (mock environments)** — annotate the mock fields that will be trimmed
  (`region`, `db`, `Degraded`, `Deploying`, possibly `lastBackup`) so nobody builds
  logic on them.
- **X3 (deploy)** — same credential; use v2 upload for large packages; `AutoSyncDb`
  is the "run DB migrations" switch; production transport bypasses web 2FA → MT-19
  guard mandatory.

---

## Part B — Open items that needed Jeremy's real account

Run `docs/spikes/MT-01-verify.ps1` (see header for setup). It is read-only by
default; snapshot/archive creation are opt-in switches with typed confirmation.

Item status after the live run (details in "Part B results" below):

1. **Auth positive proof** — **RESOLVED**: API key succeeds on Deploy v1 + Backups
   v2. The PAT negative-rejection test was not run (no PAT set) — **optional/moot**
   now that the API-key path is proven; we are not adopting PATs in v1 anyway.
2. **Real snapshot JSON** — **RESOLVED**: no `size` field, no automatic/manual type
   field; `comment` reliably distinguishes snapshot origin. See results.
3. **Rate-limit behaviour** — **STILL OPEN** (429/`Retry-After` — probe not run).
4. **Real environment JSON (v1)** — **RESOLVED**: extra undocumented fields exist;
   sandbox payloads are leaner. No region, no DB size anywhere. See results.
5. **Archive completion payload / download headers** — **STILL OPEN** (needs
   `-CreateArchive`; deferred to MT-16 implementation).
6. **`database_only` archive layout** — **STILL OPEN** (same deferral as item 5).
7. **Cross-org coverage** — **RESOLVED**: one account's `GET /api/1/apps` spans
   everything it can access, including personal sandboxes. See results.
8. **PAT scope names in the profile UI** — **moot** (not using PATs in v1).

---

## Part B results — live run, 2026-07-22 (read-only pass)

Run twice by Jeremy with `Mendix-Username` + `Mendix-ApiKey`: once against sandbox
app `app1099`, once against licensed app `vanschiemagazijn`. No credentials or raw
hostnames reproduced here.

| Script step | Result |
|---|---|
| ListApps | **PASS** |
| ListEnvironments | **PASS** |
| ListSnapshots | **PASS** |
| PatV4 | SKIPPED (no PAT set — low value now that API-key auth is proven) |
| PatRejectedByV1 | SKIPPED (same) |

**Script fix from this run:** the snapshot step originally printed only the first
array element and showed nothing when the body shape differed; it now dumps the
full raw response body (`{ total, snapshots: [...] }`). The repo copy of
`MT-01-verify.ps1` includes this fix.

### 1. Auth (item 1)

Deploy v1 (apps + environments) and Backups v2 (snapshots) all returned **200**
with the API-key headers. This confirms D1's core premise operationally. The
MxToken-rejection test remains unrun and is not needed for the decision.

### 2. Apps list (item 7)

`GET /api/1/apps` returned **6 apps: 3 licensed Van Schie apps + 3 personal
sandboxes**. Fields exactly `AppId`/`Name`/`ProjectId`/`Url` — no extras. The list
spans everything the account can access, **including sandboxes** → the Environments
UI (MT-10/MT-20) needs a **sandbox filter or grouping** so free sandboxes don't
drown the licensed customer apps.

### 3. Environments (item 4)

Licensed-node environment payload carries **more** than the documented seven fields:
`Url`, `Mode`, `Status`, `ModelVersion` (e.g. `1.8.82.e3c1a393`), `MendixVersion`
(e.g. `10.24.16.96987`), `Production`, `Instances`, `MemoryPerInstance`,
`TotalMemory`, `EnvironmentId`, `RuntimeLayer`.

Sandbox payload is **leaner**: only `Url`, `Mode` (= `"Sandbox"`), `Status`,
`Production`, `Instances`, `MemoryPerInstance`, `TotalMemory`, `EnvironmentId` —
**no `ModelVersion`, `MendixVersion`, or `RuntimeLayer`**.

Consequences:
- `MendixVersion` **is** available for the designed "Mendix" card column on licensed
  nodes; **DTOs must make the version fields nullable** (absent on sandboxes).
- **No region, no DB size anywhere** — the Part A trim verdict stands confirmed.
- `Status` values observed: `Running`, `Stopped` (no Degraded/Deploying, as
  predicted).

### 4. Snapshots (item 2)

Response shape: `{ total, snapshots: [...] }` — 139 total, all returned in the
first page. Snapshot fields observed: `snapshot_id`, `model_version` (**absent on
failed snapshots**), `comment`, `expires_at`, `state` (observed: `completed`,
`failed`), `status_message`, `created_at`, `finished_at`, `updated_at`.

Consequences for MT-14:
- **No `size` field → the Size column is cut.**
- **No automatic/manual type field**, but `comment` reliably distinguishes origin:
  "Automatically created nightly snapshot" vs "Backup created by Mendix pipeline"
  vs manual user comments → derive Type from `comment` (heuristic) or show the
  comment directly.
- **`state=failed` occurs in real data** (11 of 139) with detailed
  `status_message` (e.g. DB connection refused, missing relation) → the UI must
  render the failed state and surface `status_message`.
- `expires_at` varies from roughly 1 month to roughly 12 months — retention is not
  uniform; display it rather than assuming a policy.

### Still live-only

Rate limits (429 behaviour), archive download headers/`Content-Length`, and the
`database_only` archive layout need `-RateLimitProbe` / `-CreateArchive` runs —
**deferred to MT-16 implementation**, where the download job is built against the
real thing anyway. PAT scope naming is moot.

With these results, the Visionair can record D1 + D4 in `vision.md`;
MT-13/14/15/16/20 unblock.

---

## Sources

- Deploy API v1 — https://docs.mendix.com/apidocs-mxsdk/apidocs/deploy-api/
- Deploy API v2 — https://docs.mendix.com/apidocs-mxsdk/apidocs/deploy-api-2/
- Deploy API v4 — https://docs.mendix.com/apidocs-mxsdk/apidocs/deploy-api-4/
- Deploy API v4 OpenAPI spec — https://docs.mendix.com/openapi-spec/cloud-portal-v4.yaml
- Backups API v2 — https://docs.mendix.com/apidocs-mxsdk/apidocs/backups-api/
- Platform APIs index (auth overview) — https://docs.mendix.com/apidocs-mxsdk/apidocs/
- Mendix Profile / User Settings (API keys + PATs) — https://docs.mendix.com/mendix-profile/user-settings/
- Set Up Your PAT — https://docs.mendix.com/apidocs-mxsdk/mxsdk/set-up-your-pat/
- Restore a Backup Locally (archive contents) — https://docs.mendix.com/developerportal/operate/restore-backup-locally/
