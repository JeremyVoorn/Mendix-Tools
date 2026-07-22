  # Mendix Tools — Backlog

> Owner: Scrum-master. Slices `docs/vision.md` "Now" items (N1–N9) into stories.
> Test spec: the acceptance criteria below are what the Tester/Reviewer verifies.
> Design spec: where a story says "matches `<file>`", that file in
> `docs/design-system/` **is** the spec (layout, copy, spacing, states).
> Last updated: 2026-07-22 — **MT-04 done** (review passed; focus-idiom nuance +
> bUnit follow-up recorded). MT-01 live verification confirmed API-key auth; findings
> propagated into MT-10/14/16/20. **Sprint 2 groomed dev-ready:** live field set baked
> into MT-10/MT-20; N6 "last backup: keep it, lazy-loaded" decision propagated (open
> question 3 closed); test-project + bUnit DoD sharpened on MT-08/MT-09; Sprint 2 status
> flags corrected to reflect the MT-05/06/07 dependency.

---

## Sprint goal (current)

**Sprint 1 — "On-design foundation": the app starts on Windows with the real design
system (tokens, primitives, shell, dark mode) and the auth-model spike is answered,
so every screen after this is cheap and unblocked.**

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
  `Components/Pages/{Counter,Weather,Home}.razor`.

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

## NOW — Sprint 2: Plumbing + first screens (mock env + wired settings)

**Sprint 2 goal:** the app remembers things (SQLite), runs background jobs, shows the
Environments dashboard on mock data, and Settings can talk to a real local Postgres.

> **Slice order & readiness (Sprint 1 complete — all foundation deps satisfied):**
> - **Every Sprint 2 story is now ready-for-dev today:** MT-08, MT-09, MT-10, MT-11,
>   MT-12, MT-13. All their dependencies (MT-04..MT-07) are Done.
> - **Core track:** **MT-08** first (deps: none; owns the test scaffold), then **MT-09**
>   (needs MT-08's job-history persistence). These carry the first real unit tests +
>   the MT-04 bUnit follow-up + the ThemeService tests.
> - **Screen/Settings track:** **MT-10** (mock Environments), **MT-11** (Postgres),
>   **MT-12** (Preferences), **MT-13** (Credentials) can proceed in parallel with the
>   core track.
> - **Riskiest-first note:** MT-13 (secrets/vault) and MT-11 (real Postgres `[S]`/`[PG]`)
>   carry the most uncertainty in this sprint — pull them early.

### MT-08 — Local metadata store (SQLite) (N4) — **Size: S** — READY (Sprint 2 active)
*As a consultant, I want the app to remember restored-DB provenance, job/deploy history,
and cached environment state locally, so that the app has memory Sprintr doesn't.*

**Acceptance criteria**
- Given first run, when the app starts, then a SQLite DB is created under the app-data
  directory with schema/migrations for: restored-database provenance (target DB name,
  source app + environment, snapshot timestamp, Mendix version, size, restored-at),
  job history (type, phases, result, log path), and cached environment state (payload +
  fetched-at for staleness display).
- Given the store lives in a UI-agnostic core project/namespace (`MendixTools.Core` or
  equivalent), when referenced, then it has no dependency on MAUI/Blazor types
  (vision principle 7 — CLI-ready).
- Given a schema change later, when the app starts on an older DB, then migrations
  upgrade it without data loss (covered by a unit test).
- CRUD covered by unit tests against a temp-file database.

**Dependencies:** none (does not need MT-07 — this is core, so it is `READY` today,
ahead of the shell). **Unblocks:** MT-09 (job-history persistence), MT-17 provenance,
MT-20 (env cache), X4 later.
**DoD extras:** schema documented in code comments or a short `docs/` note.
**No test project exists yet (flagged in MT-02 review):** MT-08 and MT-09 share one
test-scaffold DoD — whichever lands first creates it and wires it into the solution,
running green on Windows. Because MT-09 depends on MT-08, **MT-08 lands first and owns
the scaffold.** The scaffold is **two projects**: an **xUnit core-logic project** (e.g.
`MendixTools.Core.Tests`, referencing the UI-agnostic core) and a **bUnit component
project** (referencing the Blazor components) — MT-04's UI behaviours cannot be tested
from the core project.
**bUnit follow-up (from MT-04 review) — named here so it cannot be lost:** once the
bUnit project exists, add coverage for the three deviation-prone MT-04 behaviours:
(1) **Checkbox mixed→true** (an indeterminate checkbox toggles to checked, not to
unchecked); (2) **Radio group single-selection** (selecting one option clears the
previously selected sibling); (3) **Tabs Arrow/Home/End index math** (ArrowRight/Left
wrap, Home selects first, End selects last). If MT-08 ships before the Blazor project
is test-ready, MT-09 completes the bUnit half — but the three behaviours are DoD for
whichever story closes the scaffold.
**ThemeService as the cheap first target (from MT-05/06/07 reviews):** alongside the
job-engine and bUnit trio, add unit tests for `ThemeService` — default-light, toggle
flips the theme, and Preferences round-trip (persist and restore across a simulated
restart). Low-cost, exercises the scaffold end to end.

---

### MT-09 — Shared job engine (core) (N5) — **Size: M** — **BLOCKED(MT-08)**
*As a consultant, I want long operations (restore, download, deploy) to run as
background jobs with phases, progress, and logs that survive navigation, so that I
never babysit a screen.*

**Acceptance criteria**
- Given the core library, when a job is started, then it exposes: an ordered set of
  named **phases** (e.g. "Downloading backup" → "Dropping & recreating schema" →
  "Importing into acme_local"), **progress** (0–100 or indeterminate per phase),
  streamed **log lines** with level, terminal states (succeeded / failed / cancelled),
  and cooperative **cancel**.
- Given a running job, when the user navigates to another screen and back, then the
  job is still running and its live state re-renders (jobs live in a singleton service;
  Blazor components subscribe/unsubscribe to state-change events).
- Given a job fails, when its state is inspected, then the failure message states what
  happened and the log is retained (persisted via MT-08 job history).
- Given cancel is requested mid-phase, when the job supports it, then it stops at the
  next safe point and ends in state `cancelled` (unit-tested with a fake job).
- Scope guard: **no** scheduling, queues, retries, or persistence-across-restart —
  phase + progress + lines + cancel only.
- The engine has no MAUI/Blazor dependency; unit tests cover state transitions,
  event ordering, and cancellation.

**Dependencies:** MT-08 (job-history persistence — the engine writes terminal state and
log path there; core-only, so it does **not** need MT-07). **Unblocks:** MT-16, MT-17,
MT-18.
**DoD extras:** the job engine is the **home for the first real core unit tests** —
state transitions, phase/log event ordering, and cooperative cancellation, all against
a fake job, must run green (see the AC scope guard). The shared test scaffold is owned
by MT-08 (lands first); if for any reason MT-08 has not closed the **bUnit** half, this
story does, covering the three pinned MT-04 behaviours by name: **Checkbox mixed→true**,
**Radio group single-selection**, and **Tabs Arrow/Home/End index math** (definitions
in MT-08's DoD).

---

### MT-10 — Environments dashboard, mock-first (N6a) — **Size: M** — READY
*As a consultant, I want one dashboard showing every app and environment across all my
customers, so that I see the state of everything without opening a single Sprintr tab.*

**Acceptance criteria**
- Given `ui_kits/app/EnvironmentsScreen.jsx` and its mock data (3 customers,
  6 environments, versions 9.24–10.12), when the Environments page renders, then it
  matches the mock **as amended by D1's field set (vision N6)**: stat row (aggregate
  counts), environment cards grouped per app with status badge/StatusDot
  (**Running/Stopped/Empty only**), Mendix version (mono), host (`Url`, mono), mode,
  and a production marker; card quick actions (Backup, Open, overflow) present but
  **disabled with tooltips** ("Available after cloud connection") since data is mock.
- Given the fields **trimmed by D1** — "Degraded"/"Deploying" statuses, region, live
  DB size — when the page renders, then they do **not** appear on cards, and the mock
  data/annotations make clear no logic may be built on them.
- Given the **live-verified environment field set** (MT-01, 2026-07-22), when the mock
  data and environment DTO are shaped, then the DTO models exactly the fields the real
  Deploy-v1 payload returns — `Url`, `Mode`, `Status`, `ModelVersion`, `MendixVersion`,
  `Production`, `Instances`, `MemoryPerInstance`, `TotalMemory`, `EnvironmentId`,
  `RuntimeLayer` — with **`MendixVersion`/`ModelVersion`/`RuntimeLayer` nullable**
  (sandbox environments return none). The card renders: status badge/StatusDot, Mendix
  version (mono), host (`Url`, mono), mode, production marker. `Instances`,
  `MemoryPerInstance`, `TotalMemory`, `EnvironmentId`, `RuntimeLayer` are carried on the
  DTO but **not shown on the card in v1** (noted for later use, so MT-20 reuses the same
  shape). Mendix version **stays** on cards — confirmed returned for licensed nodes
  (e.g. `10.24.16.96987`) — and renders **"—" for sandboxes**.
- Given the live-verified apps list (`AppId`/`Name`/`ProjectId`/`Url`, mixing personal
  sandboxes with licensed customer apps), when the mock data is shaped, then it includes
  at least one **personal sandbox** app (`Mode = "Sandbox"`, leaner env payload) grouped
  or filtered distinctly from licensed customer apps, so sandboxes cannot drown the
  customer apps — proving the grouping/filter MT-20 will reuse against the real list.
- Given the **N6 "last backup" decision (keep it, lazy-loaded)**, when a card first
  renders, then its "Backup" cell shows a loading placeholder and fills **independently
  per environment** as a separate `IEnvironmentService` call returns the newest backup
  timestamp — the card never blocks on it. In the mock, this is simulated with a
  per-env delay so the lazy fill is visibly proven; **sandbox cards show "—"** (no
  backups). This mirrors the real MT-20 path, where the same seam call maps to the
  single Backups-v2 snapshots call per env (newest `created_at`) that MT-14 makes.
- Given the topbar Refresh action, when clicked, then the mock data visibly re-renders
  (spinner/state cycle) — proving the refresh plumbing the wired version will reuse.
- Given the mock data lives behind an **`IEnvironmentService` seam**, when MT-20 later
  swaps in the real Deploy-v1 client, then the page needs no structural changes. This
  story ships the seam with **one mock implementation registered in DI now**; the
  interface must expose (a) list apps+environments and (b) fetch newest-backup timestamp
  per environment, so the real Deploy-v1 + Backups-v2 implementation in **MT-20** drops
  in without touching the page. Interface asserted by a compile-time seam.
- Light + dark theme both verified.
- **Polish carry-forwards from the Sprint 1 primitive/routing reviews — fold into this
  first real screen (all minor, no rush, but do not lose them):**
  (1) **ProgressBar rounding** — displayed `%` uses banker's rounding; switch to
  `MidpointRounding.AwayFromZero` to match the JSX half-up exactly.
  (2) **ProgressBar `aria-valuenow`** — must report the *clamped* value when
  `Value > Max`.
  (3) **Toast `aria-live`** — currently nested (stack + per-toast); keep it on **one**
  level only.
  (4) **`Routes.razor` `FocusOnNavigate Selector="h1"`** matches nothing today — give
  each real screen a top-level `<h1>` heading so focus-on-navigate works.

**Dependencies:** MT-03 (Done), MT-05 (Done — Tooltip/Card), MT-07 (Done — shell/nav).
**Unblocks:** MT-20.
**DoD extras:** trimmed-field annotations verified in review (D1 field set is final);
the `IEnvironmentService` seam signature reviewed as MT-20-ready (list + per-env
backup-timestamp method both present).

---

### MT-11 — Settings: Database tab, wired to local Postgres (N9a) `[S]` `[PG]` — **Size: M** — READY
*As a consultant, I want to configure and test my local Postgres connection and data
directory once, so that restores have somewhere real to land.*

**Acceptance criteria**
- Given `ui_kits/app/SettingsScreen.jsx` (Database tab), when rendered, then it matches:
  host/port/username/password inputs (mono where the mock is mono), data-directory
  picker, and a "Test connection" action.
- Given valid connection values, when "Test connection" is clicked, then a real
  connection attempt is made (Npgsql) and the result shows server version and latency
  ("PostgreSQL 16.2 — 12 ms" style, values real); given invalid values, then the error
  states what failed (host unreachable / auth failed / timeout) — never a raw stack
  trace.
- Given a password is entered and saved, when storage is inspected, then the password
  is in MAUI `SecureStorage` (Windows Credential Manager), **not** in SQLite,
  Preferences, or any file; non-secret fields persist in Preferences/SQLite; restart
  restores all values (password field shows masked placeholder, never the value).
- Given any UI or log output, when a connection string is displayed, then the password
  is never rendered or logged.
- Given the data-directory picker, when a folder is chosen, then it persists and is
  created if missing.

**Dependencies:** MT-04, MT-07. **Unblocks:** MT-17, X1.
**DoD extras:** secure-storage behaviour covered by an integration note the Tester can
follow (where to look to prove no plaintext).

---

### MT-12 — Settings: Preferences tab (N9b) — **Size: S** — READY
*As a consultant, I want theme, auto-refresh, keep-backup-file, and checksum preferences
in one place, so that the app behaves my way without re-deciding per action.*

**Acceptance criteria**
- Given `ui_kits/app/SettingsScreen.jsx` (Preferences tab), when rendered, then it
  matches: theme selection (synced two-way with the topbar toggle), 30s auto-refresh
  switch, "keep .backup files after restore" switch, "verify checksum after download"
  switch.
- Given any preference change, when toggled, then it takes effect immediately (no Save
  button), persists across restart, and is exposed via a typed settings service the
  Backups/Restore stories consume (keep-file default in MT-18's dialog, checksum flag
  in MT-16).
- Given the auto-refresh switch, when no consumer exists yet, then the value still
  persists (consumed later by MT-20) — noted in code.

**Dependencies:** MT-04, MT-07.
**DoD extras:** none.

---

### MT-13 — Settings: Credentials tab, vault-backed (N9c, D1 shape) `[S]` — **Size: M** — READY
*As a consultant, I want my Mendix username and API key stored in the OS vault with a
clear way to replace or remove them, so that cloud features work without my key ever
touching a file.*

**Acceptance criteria** *(shape per D1, recorded in `docs/vision.md` — this amends the
`SettingsScreen.jsx` mock)*
- Given the Credentials tab, when rendered, then it shows exactly **two fields**:
  `Mendix username` (plain Input) and `API key` (masked, mono, with reveal toggle) —
  **no PAT field, no `mx:deploy, mx:backups` scope tags, no "API region" row**
  (deviations from the mock per D1; layout otherwise follows `SettingsScreen.jsx`).
- Given values are saved, when storage is inspected, then **both** are in MAUI
  `SecureStorage` (Windows Credential Manager) only — never SQLite, Preferences, files,
  logs, or the repo; the UI states "Stored in OS credential vault"; after restart the
  API key renders as a masked placeholder, never the value.
- Given a stored credential, when "Test" is clicked, then the app calls
  `GET /api/1/apps` with `Mendix-Username` + `Mendix-ApiKey` and reports: success with
  the app count; 401 → "Credential rejected — check username and API key."; 403 →
  "No API Rights — ask a Technical Contact for this account."; network failure → an
  actionable offline/timeout message. Never a raw exception.
- Given the **Remove** button (renamed from the mock's "Revoke"), when clicked, then
  both values are deleted from the vault and the tab returns to its empty state; helper
  text notes that real key revocation happens in the Mendix profile, with a link out.
- Given no credential stored (first run), when cloud screens load, then they show the
  designed empty/"connect" state pointing to Settings › Credentials, not errors.

**Dependencies:** MT-04, MT-07. (The verify-kit live run of 2026-07-22 confirmed the
auth model works as specified — no remaining soft dependency.)
**Unblocks:** MT-14, MT-15, MT-16, MT-20.
**DoD extras:** Tester verifies vault storage on Windows (Credential Manager) directly.

---

## NOW — Sprint 3: The flagship flow (wired backups → local Postgres)

**Sprint 3 goal:** cloud backup → restored, usable local Postgres database in one flow,
with real progress and a real destructive-action guard. `[ENV]` stories call real
Mendix APIs — use a test app/environment, never a customer production app, during dev.

### MT-14 — Backups: wired list per environment (N7a) `[ENV]` — **Size: M** — **BLOCKED(MT-13)**
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
  what happened and what to do next ("Credential rejected — check Settings ›
  Credentials"), never a raw exception.
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
- Given the operation completes, when done, then a toast states the fact ("Backup
  created for Acme Insurance · Production.") — voice rules, no celebration.
- Non-destructive but `[ENV]`: creating snapshots on a real environment is allowed;
  the action never targets an environment other than the one selected on screen.

**Dependencies:** MT-14 (+ MT-05 Toast).
**DoD extras:** none.

---

### MT-16 — Backups: download archive with progress + integrity check (N7c) `[ENV]` — **Size: M** — **BLOCKED(MT-09, MT-14)**
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

**Dependencies:** MT-09, MT-14, MT-12 (checksum pref), MT-11 (data directory).
**DoD extras:** orchestration state machine unit-tested with a mocked API client.

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

**Dependencies:** MT-08, MT-09, MT-11, MT-16, MT-19 (guard must gate the destructive
step). **Flag:** destructive against the *local* Postgres (drops the target DB).
**DoD extras:** end-to-end restore of a real (small) archive verified on Windows.

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

### MT-20 — Environments dashboard, wired read-only (N6b) `[ENV]` — **Size: M** — **BLOCKED(MT-10, MT-13)**
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
- Given a fetch succeeds, when the app is later offline or a refresh fails, then the
  last-known state renders from the metadata-store cache with a visible stale
  indicator including fetched-at time (vision principle 6); given no cache and no
  network, an offline empty state explains it.
- Given Refresh (topbar or button), when clicked, then data re-fetches; auto-refresh
  honours the MT-12 preference (30s) and pauses while offline.
- Read-only: card quick actions may link out (Open in browser) and jump to Backups
  with the environment preselected; no start/stop actions in this story.

**Dependencies:** MT-08 (cache + last-backup cache), MT-10 (seam + grouping), MT-12
(auto-refresh pref), MT-13 (credential). (Open question 3 closed — N6 "last backup"
decision recorded above.)
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
