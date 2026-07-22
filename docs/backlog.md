  # Mendix Tools — Backlog

> Owner: Scrum-master. Slices `docs/vision.md` "Now" items (N1–N9) into stories.
> Test spec: the acceptance criteria below are what the Tester/Reviewer verifies.
> Design spec: where a story says "matches `<file>`", that file in
> `docs/design-system/` **is** the spec (layout, copy, spacing, states).
> Last updated: 2026-07-22 — **Sprint 2 COMPLETE** (MT-08..MT-13 Done, merged @2839e97,
> 40 tests green, security review clean). Sprint 3 (flagship flow) is active.
> Ready-for-dev: MT-14, MT-20. MT-19 still blocked on the D5 decision. **Sprint 2 groomed dev-ready:** live field set baked
> into MT-10/MT-20; N6 "last backup: keep it, lazy-loaded" decision propagated (open
> question 3 closed); test-project + bUnit DoD sharpened on MT-08/MT-09; Sprint 2 status
> flags corrected to reflect the MT-05/06/07 dependency.

---

## Sprint goal (current)

**Sprint 3 — "The flagship flow": cloud backup → restored, usable local Postgres
database in one flow, with real progress and a real destructive-action guard.**

> Sprints 1 and 2 are COMPLETE. Sprint 1 (on-design foundation): MT-01..MT-07. Sprint 2
> (plumbing + first screens): MT-08..MT-13 — metadata store, job engine, mock
> Environments, and a fully wired Settings screen (Postgres + preferences + vault-backed
> credentials). The app builds on Windows with 40 green tests and a clean security
> review (secrets only in `SecureStorage`; no Mendix API call in the codebase yet).

---

## Conventions

- **Flags:** `[D]` destructive action, `[S]` handles secrets, `[ENV]` touches a real
  Mendix cloud environment, `[PG]` touches a real local Postgres.
- **Status:** `READY` (can start today), `BLOCKED(<on what>)`.
- **Standard DoD** (applies to every story; per-story extras listed under the story):
  1. Code compiles and runs on the Windows MAUI target (`net10.0-windows`).
  2. Core/non-UI logic has unit tests; UI verified against acceptance criteria.
  3. Reviewed and passed by Tester/Reviewer against the acceptance criteria.
  4. Docs updated where the story says so (e.g. decisions recorded in `vision.md`).
  5. No secrets in source, logs, or rendered connection strings — ever.
- **Codebase notes for the Ontwikkelaar:** repo is the fresh MAUI Blazor template
  (namespace `Mendix_Tools`). Template artifacts to replace/remove as stories land:
  `wwwroot/lib/bootstrap`, `wwwroot/app.css`, `Components/Layout/*`,
  `Components/Pages/{Counter,Weather,Home}.razor`. *(As of Sprint 1 the layout and
  sample pages have been removed by MT-07.)*
- **NavLink note (for later, from MT-07 review):** the sidebar nav items use
  `Match=NavLinkMatch.All`. When any nav target gains sub-routes (e.g.
  `/backups/{id}`), switch **that item** to `Prefix`, or it loses its active highlight
  on the detail route. Relevant once MT-14+/MT-18 introduce detail routes.
- **Deferred tech-debt (non-blocking review suggestions, no story yet):**
  (a) **ThemeService `Changed` event (MT-12/MT-09 review)** — give `ThemeService` an
  event so the Settings theme radios ↔ sidebar toggle sync is explicit rather than
  relying on the Blazor render cascade. (b) **JobEngine async log write (MT-09
  review)** — `File.WriteAllLinesAsync`; pinned as do-first on **MT-16/MT-17**.

### Open questions pushed back to the Visionair

1. **D5 (destructive-action mechanism)** — MT-19 proposes *type-the-target-database-name*
   as the confirm mechanism for clean restore (and later drop/prod-deploy). Confirm or
   pick another mechanism **before MT-19 starts** (Sprint 3).
2. **"Merge into existing" restore strategy** — the mock dialog shows it, but semantics
   for `pg_restore` are undefined. Stories MT-17/MT-18 are scoped **clean-restore-only**;
   the merge radio ships disabled with "not available in this version". Confirm cut, or
   define merge precisely.

*(Open question 3 — "Last backup" on environment cards — CLOSED by the Visionair in
vision N6; see Resolved decisions below.)*

### Resolved decisions (propagated)

- **MT-13 credential-verification button — ACCEPTED DEVIATION 2026-07-22.** MT-13's
  original AC called for a "Test → `GET /api/1/apps`" button to verify the stored
  credential. It was **deliberately omitted** because the project-wide security rule
  forbids any Mendix Platform API call from the app (customer production data); the tab
  uses a **presence-based Connected / Not-configured badge** instead. Reviewer concurred
  this is correct. **Do not schedule the button** unless the Visionair explicitly
  reconciles D1 with the security rule (e.g. a later user-triggered, read-only
  verification carve-out). **Consequence, propagated to MT-14/MT-15/MT-16/MT-20:** there
  is **no in-app credential pre-validation**, so each story's first real cloud call must
  handle 401/403 gracefully ("Credentials invalid — check Settings › Credentials"),
  never assume a validated key or crash.

- **N6 "last backup" — RESOLVED 2026-07-22** (Visionair, recorded in `docs/vision.md`
  N6): **keep it on the card, lazy-loaded.** The environment payload carries no backup
  info; the only source is one Backups-v2 snapshots call **per environment** (newest
  `created_at`) — the same call MT-14 makes. Cards render immediately and each "Backup"
  cell fills as its call returns; **sandboxes show "—"** (no backups). Propagated into
  MT-10 (mock proves the lazy fill + "—") and MT-20 (real per-env call, cached via
  MT-08); open question 3 is closed, MT-20 no longer blocked on it.

- **D1 — RESOLVED 2026-07-22** (recorded in `docs/vision.md`): API-key generation for
  everything — Deploy API v1 (+v2 for >300 MB) + Backups API v2 with `Mendix-Username`
  + `Mendix-ApiKey`; one credential pair; Deploy v4/PAT skipped. Credentials tab =
  username + API key, no scope tags, no region row, Revoke → Remove. Propagated into
  MT-13 (rewritten, unblocked), MT-10/MT-20 (field set locked), MT-14.
- **D4 — RESOLVED 2026-07-22**: DB-only archives (`data_type=database_only`);
  `files_and_database` out of the v1 UI. No API checksum exists — MT-16's **local**
  integrity check (zip/tar test + `Content-Length`) is the primary mechanism.
  8-hour URL expiry and HTTP 429 handling baked into MT-16.
- **MT-01 live verification — DONE 2026-07-22** (read-only run of
  `docs/spikes/MT-01-verify.ps1`): API-key auth confirmed against Deploy v1 +
  Backups v2. Confirmed facts, propagated: snapshots API returns **no size and no
  type field**, but `comment` reliably distinguishes automatic/pipeline/manual, and
  the response shape is `{ total, snapshots[] }` with `expires_at` and failed states
  present (→ MT-14); `MendixVersion` **is** returned for licensed nodes but absent on
  sandbox environments (→ MT-10/MT-20 nullable DTOs); `GET /api/1/apps` mixes personal
  sandboxes with licensed apps (→ MT-10/MT-20 grouping). Remaining live-only items —
  **rate-limit behaviour** and **archive download headers** — folded into **MT-16** as
  implementation-time checks; PAT tests are moot (Deploy v4 skipped per D1).

---

## NOW — Sprint 1: On-design foundation — COMPLETE (2026-07-22)

All of MT-01..MT-07 are Done and passed review (see the DONE section). The app runs as
an on-design shell with working navigation and a persisted theme toggle; tokens, fonts,
icons, and all 16 primitives are in place. Sprint 2 is now the active sprint.

---

## NOW — Sprint 2: Plumbing + first screens — COMPLETE (2026-07-22)

All of MT-08..MT-13 are Done and passed review (see the DONE section). The app has a
persistent metadata store, a shared job engine, the Environments dashboard on mock
data, and a fully wired Settings screen (Postgres connection + test, preferences,
vault-backed Mendix credentials). **Sprint 3 is now the active sprint.**

---

## NOW — Sprint 3: The flagship flow (wired backups → local Postgres)

**Sprint 3 goal:** cloud backup → restored, usable local Postgres database in one flow,
with real progress and a real destructive-action guard. `[ENV]` stories call real
Mendix APIs — use a test app/environment, never a customer production app, during dev.

> **Slice order & readiness (Sprint 2 complete):**
> - **Ready-for-dev today:** **MT-14** (wired backups list — all deps Done) and
>   **MT-20** (wired Environments — all deps Done). MT-14 is the flagship-flow entry
>   point; start there.
> - **Chain behind MT-14:** MT-15 (create) → then MT-16 (download, also needs MT-09,
>   Done) → MT-17 (restore orchestration) → MT-18 (restore dialog/UI).
> - **Still blocked on the Visionair:** **MT-19** (destructive-action guard) waits on
>   the **D5 decision** (open question 1) — needed before MT-17's destructive step and
>   MT-18. Push for D5 now so it doesn't stall the flagship.
> - **No in-app credential pre-validation exists** (MT-13 accepted deviation, below):
>   every first real cloud call — MT-14, MT-15, MT-16, MT-20 — must handle 401/403
>   gracefully rather than assuming a validated key.

### MT-14 — Backups: wired list per environment (N7a) `[ENV]` — **Size: M** — READY
*As a consultant, I want to see real snapshots for a selected environment, so that I can
pick the exact backup I need without opening Sprintr.*

**Acceptance criteria**
- Given a stored credential and a selected source environment, when the Backups page
  loads, then real snapshots from the Backups API v2 render in the DataTable per
  `ui_kits/app/BackupsScreen.jsx` **with the column set amended by the live-verified
  API shape**: Created (mono), Environment (Tag), Type (derived Badge — see below) +
  comment, Expires (`expires_at`, mono), State (Badge, "Available" = success+dot),
  row actions (Restore, Download), multi-select with "Download N" bulk button
  appearing on selection. **The mock's Size column is cut** — the snapshots API
  returns no size field; size is only known after download (MT-16 records it locally).
- Given the API has no type field, when a snapshot's `comment` is
  "Automatically created nightly snapshot", then Type renders **Automatic**; when
  "Backup created by Mendix pipeline", then **Pipeline**; otherwise **Manual** with
  the user's comment shown (truncated with tooltip).
- Given failed snapshots exist (live data: 11 of 139), when the list renders, then
  `state=failed` rows show a danger Badge and surface the API's `status_message`
  (inline or tooltip) — failed rows have no Restore/Download actions.
- Given the response shape `{ total, snapshots[] }`, when `total` exceeds the fetched
  page, then the list supports pagination or load-more driven by `total` (no silent
  truncation).
- Given the environment Select, when changed, then the list reloads for that
  environment; Refresh re-fetches.
- Given an API failure (401/403/timeout), when the list loads, then the error states
  what happened and what to do next ("Credentials invalid — check Settings ›
  Credentials"), never a raw exception. **This is the first real cloud call and there
  is no in-app credential pre-validation** (MT-13 accepted deviation), so a bad/rejected
  key surfaces here for the first time — it must degrade gracefully, not crash.
- Given no backups exist, when the list loads, then an empty state says so plainly
  (no mock rows ever shown against a real credential).
- The API client lives in the core library behind an interface; response parsing is
  unit-tested against recorded/sample JSON.

**Dependencies:** MT-06, MT-07, MT-13. (Column set final — live-verified 2026-07-22.)
**DoD extras:** none.

---

### MT-15 — Backups: create snapshot (N7b) `[ENV]` — **Size: S** — **BLOCKED(MT-14)**
*As a consultant, I want to trigger a fresh snapshot from the app, so that "make a
backup first" is one click instead of a Sprintr round-trip.*

**Acceptance criteria**
- **Pre-work (from MT-05 review) — do first or as a tiny pre-MT-15 fix:** `<ToastStack />`
  is currently rooted in `Styleguide.razor`, not the shell, so toasts fired from real
  screens silently no-op. Move `<ToastStack />` into `MainLayout.razor` (app-wide) and
  remove it from `Styleguide.razor`; verify a toast fired from a routed screen renders.
  This also unblocks MT-18's restore toasts.
- Given the "Create backup" button (per `BackupsScreen.jsx`), when clicked, then a
  snapshot is requested via the API for the selected environment and the button enters
  a loading state.
- Given snapshot creation is asynchronous, when the request is accepted, then the list
  shows the new snapshot with its in-progress state and polls until it becomes
  Available or Failed; failure states the API's reason.
- Given the create call returns **401/403** (no in-app credential pre-validation exists
  — MT-13 accepted deviation), when it fails, then the error surfaces gracefully
  ("Credentials invalid — check Settings › Credentials"), never a crash.
- Given the operation completes, when done, then a toast states the fact ("Backup
  created for Acme Insurance · Production.") — voice rules, no celebration.
- Non-destructive but `[ENV]`: creating snapshots on a real environment is allowed;
  the action never targets an environment other than the one selected on screen.

**Dependencies:** MT-14 (+ MT-05 Toast).
**DoD extras:** none.

---

### MT-16 — Backups: download archive with progress + integrity check (N7c) `[ENV]` — **Size: M** — **BLOCKED(MT-14)**
*As a consultant, I want backups downloaded to my data directory with visible progress
and an integrity check, so that files land where the tooling needs them and I trust
them.*

**Acceptance criteria**
- Given a snapshot row's Download action, when clicked, then a job (MT-09) runs:
  request archive creation (**`data_type=database_only` per D4**) → poll archive state →
  download to the configured data directory, rendering as the active-job Card from
  `BackupsScreen.jsx` (StatusDot pulse, phase label, mono timestamp, ProgressBar with
  %, Dismiss when done) and surviving navigation.
- Given the download stream, when in progress, then progress reflects bytes/total; a
  failed or interrupted download ends the job as failed with "what happened + what to
  do next" — never a generic error.
- Given the confirmed **8-hour archive-URL expiry**, when a download starts or resumes
  against an expired URL, then the job **re-requests a fresh archive link
  automatically** and continues; only if the re-request itself fails does the job fail,
  stating the cause.
- Given the API responds **HTTP 429**, when polling or downloading, then the client
  backs off (honouring `Retry-After` when present) and retries within the job rather
  than failing immediately; repeated 429s surface as "Mendix API rate limit — retrying"
  in the job log. (**Implementation-time check, folded in from MT-01:** exact 429
  behaviour was not observable in the read-only verify run — confirm against the live
  API while building this story.)
- Given the archive-creation or download call returns **401/403** (no in-app credential
  pre-validation exists — MT-13 accepted deviation), when it fails, then the job ends
  gracefully with "Credentials invalid — check Settings › Credentials", never a crash.
- Given the archive download response, when implementing, then verify the real
  response headers (**implementation-time check, folded in from MT-01**):
  `Content-Length` presence and range/resume support; if `Content-Length` is absent,
  progress falls back to indeterminate and the size check to the zip/tar test alone.
- Given a completed download, when the file lands, then its **actual size is recorded
  in the metadata store** and shown in the UI — the snapshots API returns no size
  field, so this local record is the only size source (also feeds MT-17 provenance).
- Given the "verify checksum after download" preference (MT-12) is on, when the
  download completes, then the
  **local integrity check is the primary mechanism (D4: no API checksum exists)** —
  zip/tar integrity test + `Content-Length` size match — and the result is stated; a
  corrupt file fails the job and is deleted.
- Given multi-select + "Download N", when clicked, then the selected archives download
  sequentially under one job with per-item phases.
- Given archive creation fails server-side, when polled, then the job fails with the
  API state and the log records the response.

**Dependencies:** MT-09 (Done), MT-14, MT-12 (Done — checksum pref), MT-11 (Done —
data directory). **Blocked only on MT-14.**
**DoD extras:** orchestration state machine unit-tested with a mocked API client.
**Do-first (from MT-09 review, non-blocking but due before this high-volume flow):**
switch the JobEngine log-file write to async (`File.WriteAllLinesAsync`) — the current
synchronous write is fine for low-volume jobs but should be async before MT-16/MT-17
stream large logs.

---

### MT-17 — Restore: pg_restore orchestration (clean restore, core) (N8a) `[D]` `[PG]` — **Size: L** — **BLOCKED(MT-16)**
*As a consultant, I want a downloaded backup imported into a named local Postgres
database automatically, so that the unzip/`pg_restore` dance disappears.*

**Acceptance criteria**
- Given a downloaded archive and a target DB name, when the restore job runs, then it
  executes as one MT-09 job with the phases from `BackupsScreen.jsx` ("Downloading
  backup" — skipped/instant if already local → "Dropping & recreating schema" →
  "Importing into <target>"), each with progress and log lines from the real
  `pg_restore` stderr/stdout.
- Given `pg_restore` is required, when the job starts, then the tool is located (PATH
  or configured location); if absent, the job fails **before any destructive step**
  with "pg_restore not found — install PostgreSQL client tools or set the path in
  Settings".
- Given a clean restore, when the target DB exists, then the job terminates open
  connections (`pg_terminate_backend`) and drops/recreates the database **only after
  the MT-19 guard has been passed**; given the target does not exist, it is created.
- Given the import finishes successfully, when the job completes, then: provenance is
  recorded in the metadata store (source env, snapshot timestamp, Mendix version, size,
  restored-at); the archive is kept or deleted per the keep-file choice; the final
  state reads like "Backup restored to `acme_local` (2.4 GB)." (facts, mono
  identifiers).
- Given `pg_restore` exits non-zero mid-import, when the job fails, then the failure
  names the exit code, offers "View log", and the metadata store records the failed
  attempt; a partially-restored DB is clearly marked as failed in provenance (no silent
  half-restores).
- Given disk-full or an unwritable data directory, when detected, then the job fails
  with that specific cause.
- Scope: **clean restore only** (merge is out, see open question 2); DB-only archives
  (D4). Orchestration lives in the core library, unit-tested with a fake process
  runner; one end-to-end test against a real local Postgres is run by the Tester.

**Dependencies:** MT-08 (Done), MT-09 (Done), MT-11 (Done), MT-16, MT-19 (guard must
gate the destructive step). **Flag:** destructive against the *local* Postgres (drops
the target DB).
**DoD extras:** end-to-end restore of a real (small) archive verified on Windows.
**Metadata-store note (from MT-09 review):** MT-09 persists one **terminal** job-history
row. If this story needs a live-running-job row that survives an app restart mid-restore
(e.g. to warn "a restore was interrupted"), that requires adding `UpdateJobAsync` to
`IMetadataStore` — scope it explicitly here if wanted; otherwise the terminal row
(start+finish) is the agreed baseline.

---

### MT-18 — Restore: dialog + live progress UI (N8b) — **Size: M** — **BLOCKED(MT-17)**
*As a consultant, I want a clear restore dialog and a live progress card, so that
starting and watching the flagship flow feels calm and exact.*

**Acceptance criteria**
- Given a backup row's Restore action, when clicked, then the dialog matches
  `BackupsScreen.jsx`: warning tone, `database-backup` icon, title "Restore to local
  Postgres", description "Snapshot from <created> (<size>) will be imported into your
  local server.", target-database Input (mono, prefilled from app name), restore
  strategy radios (Clean restore enabled; "Merge into existing" **disabled** with a
  "not available in this version" hint), keep-file Checkbox defaulted from the MT-12
  preference, Cancel + "Start restore" footer.
- Given "Start restore", when the target DB already exists, then the MT-19 guard is
  presented before the job starts; when it does not exist, the job starts directly
  (creation is not destructive).
- Given a running restore, when viewing the Backups page, then the active-job Card
  renders phase, mono snapshot timestamp, percentage, and tone changes (db → accent →
  success) per the mock; when navigating away and back, the card reflects the live job;
  on completion it shows the success state with Dismiss.
- Given a failed restore, when rendered, then the card shows the failure fact and a
  "View log" affordance opening the job log (LogViewer).

**Dependencies:** MT-05, MT-17, MT-19.
**DoD extras:** none.

---

### MT-19 — Destructive-action guard for clean restore (D5 mechanism) (N8c) `[D]` — **Size: S** — **BLOCKED(D5 decision)**
*As a consultant, I want a deliberate confirmation before anything drops a database, so
that I can move fast everywhere else knowing the one irreversible step demands intent.*

**Acceptance criteria** *(written against the proposed type-the-name mechanism —
confirm D5 before starting; the copy rules below hold under any mechanism)*
- Given a clean restore targeting an **existing** database, when the user proceeds,
  then a confirm step states the consequence exactly and plainly: "This drops and
  recreates `acme_local`. Open connections will be terminated. This cannot be undone."
  — target name in mono, no "Are you sure…" phrasing, no exclamation marks.
- Given the confirm step, when the user has not typed the exact target database name
  into the confirmation input, then the destructive-confirm button stays disabled;
  typing the exact name (case-sensitive) enables it.
- Given the guard is cancelled, when dismissed, then nothing has been executed against
  Postgres (verified: no dropped DB, no terminated connections).
- Given the guard component, when built, then it is a reusable primitive (one policy,
  one component) that X1's "Drop database" and X3's production-deploy guard can reuse
  unchanged — parameterised by consequence text + name to type.
- Given a non-existent target DB, when restoring, then the guard is **not** shown
  (no fake friction).

**Dependencies:** Visionair's D5 decision (see open questions), MT-05.
**Unblocks:** MT-17's destructive step, MT-18.
**DoD extras:** Tester verifies the cancel path leaves Postgres untouched.

---

### MT-20 — Environments dashboard, wired read-only (N6b) `[ENV]` — **Size: M** — READY
*As a consultant, I want the dashboard to show my real apps and environments, so that
the home screen is true.*

**Acceptance criteria**
- Given a stored credential, when the Environments page loads, then real apps and
  environments (Deploy API v1) render through the existing `IEnvironmentService` seam
  from MT-10, with the **field set locked by D1 (vision N6)**: status
  (Running/Stopped/Empty), Mendix version (confirmed live for licensed nodes, e.g.
  `10.24.16.96987`), host (`Url`), mode, production marker.
  **Trimmed and never faked:** "Degraded", "Deploying", region, live DB size.
- Given sandbox environments return no `MendixVersion`/`ModelVersion`/`RuntimeLayer`
  (confirmed live), when rendered, then those DTO fields are nullable and cards show
  "—" — never a fake value or an error.
- Given `GET /api/1/apps` includes personal sandboxes alongside licensed apps
  (confirmed live), when the list renders, then sandboxes are grouped separately or
  behind a filter toggle so customer apps stay prominent (reusing MT-10's grouping).
  (Extra fields available but out of v1 scope, noted for later: `Instances`,
  `MemoryPerInstance`, `TotalMemory`, `RuntimeLayer`.)
- Given the **N6 "last backup" decision (keep it, lazy-loaded)**, when the dashboard
  loads, then each card's "Backup" cell fills **independently per environment** from a
  single Backups-v2 snapshots call (newest `created_at`) — the same call MT-14 makes —
  through the `IEnvironmentService` seam MT-10 defined; cards never block on it, results
  are cached in the metadata store (MT-08) for the stale/offline path, and **sandboxes
  show "—"** (no backups). Polite pacing/throttling of the per-env N+1 calls (respect
  429 as elsewhere).
- Given the stat row, when rendered, then aggregates are computed from real data.
- Given the apps/environments call returns **401/403** (no in-app credential
  pre-validation exists — MT-13 accepted deviation; this is often the user's first real
  cloud call), when it fails, then the dashboard shows "Credentials invalid — check
  Settings › Credentials" gracefully, never a crash.
- Given a fetch succeeds, when the app is later offline or a refresh fails, then the
  last-known state renders from the metadata-store cache with a visible stale
  indicator including fetched-at time (vision principle 6); given no cache and no
  network, an offline empty state explains it.
- Given Refresh (topbar or button), when clicked, then data re-fetches; auto-refresh
  honours the MT-12 preference (30s) and pauses while offline.
- Read-only: card quick actions may link out (Open in browser) and jump to Backups
  with the environment preselected; no start/stop actions in this story.
- **Inherited from MT-10 (fix on real-client swap-in):** `Environments.razor`
  `LoadBackupAsync` currently only catches `OperationCanceledException`. When the real
  Backups-v2 client replaces the mock, **add a generic catch** that sets the cell to an
  error/"—" state, so a thrown per-env backup call is never left unobserved.
- **Inherited from MT-10 (wire the placeholder stat tiles):** the "Local DBs: 3" and
  "Storage used: 12 GB" stat tiles are hardcoded placeholders in MT-10 — wire them to
  real metadata-store data (MT-08) here (or flag them clearly as placeholders until X1
  provides real local-DB counts). Decide and record which in the PR.

**Dependencies:** MT-08 (cache + last-backup cache), MT-10 (Done — seam + grouping),
MT-12 (auto-refresh pref), MT-13 (credential). (Open question 3 closed — N6 "last
backup" decision recorded above.)
**DoD extras:** none (D1 field set and N6 last-backup decision are final).

---

## NEXT — epics only (from vision X1–X5; do not start, do not slice yet)

- **X1 — Local Databases screen** `[D]` `[PG]`: enumerate app-created DBs via provenance,
  sizes, dump-to-file, drop with the MT-19 guard; server status via probe; no service
  Stop/Restart until D2 is decided.
- **X2 — Connect-out**: copy connection string (never with password), launch detected
  pgAdmin/DBeaver/psql.
- **X3 — Build & Deploy (deploy-only)** `[D]` `[ENV]`: upload existing `.mda`, transport,
  start with migrations flag, narrated log; production deploys require the MT-19 guard
  (API bypasses web 2FA).
- **X4 — Deployment history with local memory**: persist who/what/log per deploy
  (rides on MT-08).
- **X5 — Backup-before-deploy**: compose MT-15's snapshot into the deploy job.

## LATER — epics only (from vision L1–L7)

- **L1 — Local build (MxBuild)**: per-version toolchain detection; strictly after X3.
- **L2 — Bundled/detected PostgreSQL**: decides D2; makes service-control UI honest.
- **L3 — Headless CLI companion**: cheap if the core library stays UI-agnostic
  (enforced by MT-08/MT-09 acceptance criteria).
- **L4 — Data anonymisation on restore** `[D]`: compliance enabler; re-evaluate after
  MT-17 adoption.
- **L5 — Scheduled local pulls**: needs L3 + Task Scheduler.
- **L6 — Multi-profile credentials** `[S]`: validate need first.
- **L7 — Slack deploy notifications**: after deploy ships.

## DONE

### MT-13 — Settings: Credentials tab, vault-backed (N9c, D1 shape) — Done 2026-07-22
Built in a parallel worktree, merged to master (commit `2839e97`), **passed review**
incl. security review. Two fields (Mendix username + masked/mono API key), both stored
**only** in `SecureStorage` (never DB/logs/DOM/disk plaintext), "Stored in OS credential
vault" copy, **Remove** (vault-delete) with link-out helper text, empty/first-run state.

- **ACCEPTED DEVIATION (recorded in Resolved decisions):** the "Test → `GET /api/1/apps`"
  verification button was **deliberately omitted** — the security rule forbids any
  Mendix Platform API call. The tab uses a **presence-based Connected / Not-configured
  badge** instead. Reviewer concurred. **Consequence propagated to MT-14/15/16/20:** no
  in-app credential pre-validation exists, so each first cloud call must handle 401/403
  gracefully (added to their ACs). Do not schedule the button unless the Visionair
  reconciles D1 with the security rule.

### MT-12 — Settings: Preferences tab (N9b) — Done 2026-07-22
Merged (commit `2839e97`), **passed review**. Theme (two-way synced with topbar toggle),
30s auto-refresh, keep-.backup-file, verify-checksum switches; instant-effect, persisted,
exposed via a typed settings service for MT-16/MT-18/MT-20 to consume.

- **Non-blocking suggestion recorded (deferred tech-debt):** give `ThemeService` a
  `Changed` event so the radios ↔ toggle sync is explicit rather than render-cascade
  driven.

### MT-11 — Settings: Database tab, wired to local Postgres (N9a) — Done 2026-07-22
Merged (commit `2839e97`), **passed review** incl. security review. Host/port/user/
password + data-directory picker, real Npgsql "Test connection" with server version +
latency and actionable errors. **Postgres password stored only in `SecureStorage`**
(never DB/Preferences/logs/DOM); connection strings never render the password. Npgsql
10.0.3, no vuln warnings.

### MT-09 — Shared job engine (core) (N5) — Done 2026-07-22
Merged (commit `2839e97`), **passed review**. Phases + progress + log lines + cooperative
cancel, terminal states persisted to `MendixTools.Core` job history; UI-agnostic
(no MAUI/Blazor dependency); survives navigation via a singleton service. **17 new
job-engine unit tests** (part of the 40 green: 32 core + 8 component). Scope guard held
(no scheduling/queues/retries/persist-across-restart).

- **Reviewer ruling (no action):** a single terminal job-history row (capturing
  start+finish) satisfies the AC. A literal live start-row would need `UpdateJobAsync`
  on `IMetadataStore` — only add if a future story needs live-running-job persistence
  across restart (noted on MT-17).
- **Non-blocking suggestions recorded (deferred tech-debt):** switch log-file write to
  `File.WriteAllLinesAsync` before MT-16/MT-17 (pinned as do-first on those stories).

### MT-10 — Environments dashboard, mock-first (N6a) — Done 2026-07-22
Built in a parallel worktree, merged to master, **passed review after fixes**.
`IEnvironmentService` seam + `MockEnvironmentService`, full DTO with the live-verified
nullable field set, dashboard per `EnvironmentsScreen.jsx` (D1-trimmed), collapsible
sandbox group, lazy per-env last-backup, topbar Refresh, and a top-level `<h1>` for
focus-on-navigate. The three pinned polish carry-forwards are now DONE (applied
directly): ProgressBar `Math.Round` away-from-zero + `aria-valuenow` clamped to Max;
Toast per-toast `aria-live` removed (kept on `ToastStack`).

- **Deferred to MT-20 (recorded there):** (a) `Environments.razor` `LoadBackupAsync`
  only catches `OperationCanceledException` — add a generic catch/error-cell on
  real-client swap-in; (b) "Local DBs: 3" / "Storage used: 12 GB" stat tiles are
  hardcoded placeholders — wire to metadata-store data (MT-08) in a later polish.
- NU1902 (AngleSharp, test-only, unreachable) suppressed via `NoWarn` with
  justification.

### MT-08 — Local metadata store (SQLite) + test scaffold (N4) — Done 2026-07-22
Built in a parallel worktree, merged to master, **passed review**. Delivered the
UI-agnostic **`MendixTools.Core`** project (`SqliteMetadataStore` + `user_version`
migrations + models for provenance / job-history / env-state / last-backup /
snapshot-sizes), refactored `ThemeService` behind `IThemeStore`, and created the
**test scaffold**: `MendixTools.Core.Tests` (xUnit, 15 tests) + `MendixTools.
Components.Tests` (bUnit, 8 tests, compiling real primitive source via `Link`). **All
23 tests green on merged master.** Satisfies the test-project DoD; the pinned bUnit trio
(Checkbox mixed→true, Radio single-select, Tabs Arrow/Home/End) and the ThemeService
tests are implemented. Unblocks MT-09.

### MT-07 — App shell: sidebar, topbar, navigation, persisted theme toggle (N3) — Done 2026-07-22
Implemented, **passed review**. On-design shell matching `AppShell.jsx` (248px sidebar,
52px topbar, nav to all five screens with active highlight, theme toggle persisted via
Preferences and restored on restart). Template debris (MainLayout/NavMenu + their CSS,
Home/Counter/Weather pages) verifiably removed. **Sprint 1 complete.**

- **Carry-forwards:** ThemeService unit tests (default-light / toggle / Preferences
  round-trip) added to MT-08/MT-09 scaffold; NavLink `Match=All`→`Prefix` note recorded
  in codebase notes for when sub-routes appear; `Routes.razor` `FocusOnNavigate
  Selector="h1"` fix recorded on MT-10 (screens need a top-level `<h1>`).

### MT-06 — Razor primitives, batch D: DataTable, LogViewer (N2e) — Done 2026-07-22
Implemented, **passed review**. DataTable (columns/mono/templates/hover, selectable
with indeterminate select-all, selection callback) and LogViewer (5,000+ lines smooth,
level colouring, tail-follow) match the JSX in light + dark.

### MT-05 — Razor primitives, batch C: Dialog, Tooltip, ProgressBar, Toast + ToastStack (N2d) — Done 2026-07-22
Implemented, **passed review**. Dialog (overlay/blur, focus trap + return, warning tone
with `alert-triangle` added to `LucideIcons.cs`), Tooltip, ProgressBar, Toast/ToastStack
match the JSX.

- **Carry-forwards recorded:** `<ToastStack />` must move from `Styleguide.razor` into
  `MainLayout.razor` → pinned as MT-15 pre-work (unblocks MT-15/MT-18 toasts);
  ProgressBar rounding (AwayFromZero) + `aria-valuenow` clamp, and Toast single-level
  `aria-live` → recorded on MT-10.

### MT-04 — Razor primitives, batch B: Input, Select, Checkbox, Radio, Switch, Tabs (N2c) — Done 2026-07-22
Implemented by the Ontwikkelaar, **passed review** by the Tester/Reviewer: build green
(0 warnings/errors), all ACs met, value-by-value style fidelity to the JSX, binding +
keyboard + a11y correct, W1 carry-forward (Button `FullWidth` + `RightIcon` styleguide
examples) present.

- **AC-wording nuance (reviewer-confirmed correct):** the "2px `--focus-ring` outline"
  AC is a blanket phrasing; Input/Select deliberately use the accent border +
  `--shadow-focus` ring instead (faithful to the JSX and readme line 49). Correct focus
  idiom = **outline for choice controls/tabs, shadow ring for text fields** — apply
  this reading to MT-05/MT-06 focus ACs too.
- **bUnit follow-up pinned on MT-08/MT-09:** cover Checkbox mixed→true, Radio group
  single-selection, Tabs Arrow/Home/End index math once the test project exists.

### MT-01 — Auth-model spike (N1) — Done 2026-07-22
D1 and D4 **formally recorded in `docs/vision.md`** by the Visionair (spike
recommendation accepted in full, no amendments): API-key generation for everything
(Deploy v1/+v2 + Backups v2, `Mendix-Username` + `Mendix-ApiKey`, no PAT/Deploy v4);
Credentials tab = username + API key, no scope tags, no region row, Revoke → Remove;
D4 = `database_only` archives, local integrity check primary, 8h URL expiry confirmed.
Outputs: `docs/spikes/MT-01-auth-model.md`, `docs/spikes/MT-01-verify.ps1`.

- **Live read-only verification DONE 2026-07-22:** API-key auth confirmed on Deploy
  v1 + Backups v2. Confirmed: no size/type fields in snapshots (`comment`-derived
  Type; `{ total, snapshots[] }` shape; `expires_at`; failed states with
  `status_message`); `MendixVersion` present for licensed nodes, absent on sandboxes;
  apps list mixes sandboxes with licensed apps. Spike doc updated in parallel.
- Remaining live-only items (rate-limit behaviour, archive download headers) folded
  into **MT-16** as implementation-time checks; PAT tests moot.
- Propagated: MT-13 rewritten and unblocked; MT-10/MT-20 field set locked incl.
  sandbox handling; MT-14 columns resolved; MT-16 amended.

### MT-03 — Razor primitives, batch A: Button, IconButton, Card, Badge, StatusDot, Tag (N2b) — Done 2026-07-22
Implemented by the Ontwikkelaar, **passed review** by the Tester/Reviewer.

- **Location amendment (reviewer-approved):** primitives live under `Components/UI/`
  (not `Components/DesignSystem/` as originally written) alongside MT-02's Icon
  component; forward stories (MT-04/05/06) updated to match.
- **Carry-forward W1 pinned on MT-04:** styleguide must add Button `FullWidth` and
  `RightIcon` examples (API shipped but undemonstrated).

### MT-02 — Design tokens, fonts, and icons bundled offline (N2a) — Done 2026-07-22
Implemented by the Ontwikkelaar, **passed review** by the Tester/Reviewer: build
succeeded on the Windows target, tokens byte-identical to `docs/design-system/tokens/`,
fonts and icons fully offline.

- **Accepted deviation:** `fonts.css` differs from the design source in `src` URLs only
  — Google's v22 URLs 404'd, so IBM Plex Sans upright ships as one variable woff2
  covering weights 400–700; documented in the file header.
- **Carry-forwards pinned on open stories:** `alert-triangle` Lucide icon missing from
  `Components/UI/LucideIcons.cs` → added as AC on **MT-05**; template debris
  (MainLayout/NavMenu/Home/Counter/Weather) removal → made verifiable in **MT-07**'s
  ACs; no test project exists yet → establishing one is now in **MT-08/MT-09**'s DoD.
