# Mendix Tools — Ideas

> Owner: Idea agent. Feeds the Visionair (prioritisation) and Scrum-master (stories).
> Last updated: 2026-07-22 — after full read of `docs/design-system/` (imported from claude.ai/design).
> Status of codebase: fresh MAUI Blazor template; nothing of the design is implemented.

The design system defines the entire product surface as designed so far: one shell,
five screens (Environments, Backups, Build & Deploy, Local Databases, Settings),
16 primitives, light+dark tokens. This document maps what those designs *mean*
functionally, what they leave open, and the feature ideas that fall out of them.

---

## 1. Problem space map — what the five screens actually cover

The persona is a **Mendix consultant running multiple customer apps** (mock data shows
3 customers, 6 environments, mixed Mendix versions 9.24–10.12). The core loop the app
replaces: log into Sprintr per customer → click through Environments → download backup
→ manually `pg_restore` → repeat per app. Local-first wins: batch view across customers,
no web round-trips, direct pipeline into local Postgres.

| Screen | Consultant job | What the design implies functionally |
|---|---|---|
| **Environments** | "What's the state of everything I run, across all customers?" | Deploy API: list apps + environments, status (Running/Stopped/Degraded/Deploying), Mendix version, DB size, region, host, last-backup age. Stat row needs aggregation. Cards have quick actions: Backup (trigger snapshot), Open (browser to app/Sprintr), overflow menu (start/stop implied by status badges). "Auto-refresh every 30s" preference implies background polling. |
| **Backups** | "Get a cloud snapshot into my machine, fast, without babysitting it." | Backups API v2: list snapshots per environment, create snapshot, create archive, poll archive state, download. Then local orchestration: unzip archive → `pg_restore` into target DB. Restore dialog: target DB name, strategy (clean = drop+recreate vs merge), keep-.backup-file checkbox. Live progress card with phases ("Downloading backup" → "Dropping & recreating schema" → "Importing into acme_local"). Multi-select rows → batch "Download N". |
| **Build & Deploy** | "Ship a version from my machine and watch it happen." | Left card: local build — project file (`.mpr`), branch select, version field, current commit + "4 commits ahead" (git integration). Right card: target environment + three switches: backup before deploy, run DB migrations, Slack notification. Streaming LogViewer shows `mx build` / `mx deploy` style output with warn/success levels. Deployment history table (when/env/version/by/result). Log download button. |
| **Local Databases** | "What's on my disk, and manage the Postgres server itself." | Local Postgres control: server status, version badge, uptime, connection string, Stop/Restart buttons, "Open in pgAdmin". Stats: DB count, total size, disk free, connections (3/100 implies `pg_stat_activity`). DB table tracks *provenance*: source env + Mendix version per restored DB. Row actions: Connect (copy conn string?), Dump to file (`pg_dump`), Drop (destructive). Disk usage bar scoped to "used by Mendix Tools". |
| **Settings** | "Wire it up once, then forget it." | Credentials tab: PAT (masked, reveal, "stored in OS credential vault", scopes shown, Revoke), default project, API region tags. Database tab: host/port/user/password, data directory for .backup files/dumps, Test connection with latency readout. Preferences: theme, 30s auto-refresh, keep .backup files, verify checksum after download. |

**Cross-cutting implications baked into the design:**
- **Job model.** Restores, builds, deploys are long-running jobs with phases, progress,
  and terminal states. Both Backups and Deploy render them; a shared job engine is implied.
- **Secrets.** PAT + Postgres password; the design literally says "OS credential vault".
- **Local state.** Restored-DB provenance (source env, Mendix version, restored-at) is not
  in Postgres metadata — the app needs its own small store (SQLite or JSON) to remember it.
- **Copy discipline.** Confirmations state consequences ("This drops and recreates
  `acme_local`. This cannot be undone."), errors state next steps ("View logs"). This is a
  spec for error UX, not just tone.

---

## 2. What the designs imply but leave open

These are decisions the Visionair must make; the mocks quietly assume answers.

### Integration reality vs. the mocks
- **Auth model mismatch (important).** Settings shows a PAT with scopes `mx:deploy,
  mx:backups`. The actual [Backups API v2](https://docs.mendix.com/apidocs-mxsdk/apidocs/backups-api/)
  and [Deploy API v1](https://docs.mendix.com/apidocs-mxsdk/apidocs/deploy-api/) authenticate
  with `Mendix-Username` + `Mendix-ApiKey` headers, not PATs. [Deploy API v4](https://docs.mendix.com/apidocs-mxsdk/apidocs/deploy-api-4/)
  is PAT-based but has a different (narrower/newer) operation set. Likely outcome: the app
  needs **both** an API key (v1/backups) and possibly a PAT (v4), or picks one API generation.
  The Credentials screen may need redesign once this is settled.
- **"DB size" and "Degraded" on environment cards.** Deploy API v1 environment status is
  Empty/Stopped/Running only. "Degraded" and live DB size likely need extra calls (metrics
  endpoints) or may simply not be available. Card metadata may need trimming to what the
  API returns.
- **Local build chain.** The mock logs `$ mx build --project AcmeInsurance.mpr`. Real local
  builds go through MxBuild/mx tool shipped **per Studio Pro version** — building a 9.24 app
  needs the 9.24 toolchain. The app must locate installed Studio Pro versions (or the
  standalone MxBuild distribution) and match them to the project. This is the single
  hairiest integration in the design.
- **Deploy = upload + transport + restart.** Uploading is `.mda` (not `.mpk`) via Deploy
  API; transport requires the target app stopped; production transport needs Transport
  Rights. The "Backup before deploy" switch maps to a snapshot call; "Run database
  migrations" maps to the `AutoSyncDb` flag on start. The streaming log is really *our*
  narration of a multi-call state machine — there is no server-side log stream to tail.
- **Archive format.** Backup archives are a zip of a Postgres dump + file documents.
  Restore is `pg_restore` for the DB; the **file documents** part is unaddressed in the
  design (a local Mendix runtime needs them; a data-analysis use case doesn't). Decide
  whether "restore" means DB-only (`data_type=database_only` keeps downloads small) or
  full. The archive download URL expires after 8 hours — resume/retry design needed.

### Missing states (not mocked)
- **Empty states:** first run (no PAT, no envs, no local DBs), no backups for an env,
  Postgres not installed/not running.
- **Error states:** API 401/403 (expired key, missing API Rights), archive creation
  `failed`, download interrupted, `pg_restore` errors mid-import, port 5432 occupied,
  disk full mid-restore (5 GB backups are in the mock data).
- **Concurrency:** two restores at once? Restore into a DB that has open connections
  (drop will fail — needs `pg_terminate_backend` or a clear error)?
- **Offline:** which screens degrade gracefully? Local Databases and previously
  downloaded backups should work fully offline; Environments/Backups need cached
  last-known state + "stale" indicators. Nothing in the design marks staleness.

### Destructive-action guards (flagged for downstream weight)
- **Drop database** (Databases row action) — mock has only a tooltip; needs the
  type-the-name or explicit confirm dialog per the voice guidelines.
- **Clean restore** — drops schema; dialog exists but shows no "connections will be
  terminated" consequence.
- **Deploy to Production** — the API bypasses 2FA that the web UI enforces. A local tool
  quietly removing that safety rail deserves an intentional guard (confirm with env name,
  or restrict to non-prod by default).
- **Revoke** (Credentials) — one click next to Save; what does it actually do?

### Secrets handling
- OS credential vault = Windows Credential Manager via MAUI `SecureStorage` — fine, but
  decide: are Postgres passwords also vaulted (design shows them in a plain settings form)?
- Connection strings appear in UI (`Host=localhost;...Username=mendix`) — never render
  the password; the mock already avoids it, keep it that way.

---

## 3. Feature ideas

Grouped, sized (S/M/L feel, not estimates), each with who/why/risk.
Ideas marked **[D]** touch destructive actions, **[S]** touch secrets.

### A. Foundation (prerequisites, not features users see)

1. **Port design tokens + primitives to Blazor** — translate `tokens/*.css` 1:1 into the
   MAUI Blazor app, rebuild the 16 primitives as Razor components (Button → Dialog →
   DataTable → LogViewer being the hard four). *Why:* every screen composes these; doing
   it first keeps five screens cheap. *Risk:* low — CSS carries over almost verbatim in a
   WebView; Lucide + Google Fonts CDN references must be bundled locally for offline
   (the readme itself flags this). **Size: M.**
2. **App shell + navigation + theme toggle** — sidebar, topbar, dark mode persisted.
   *Risk:* low. **Size: S.**
3. **Shared job engine** — a background-job abstraction (phases, progress, log lines,
   cancel) that Backups, Deploy, and Databases all render. *Why:* the designs show three
   different long-running flows with identical anatomy; building it once avoids three
   bespoke implementations, and enables a later "jobs continue while you navigate away"
   behaviour that Sprintr can't offer. *Risk:* over-engineering if scoped too generally —
   keep it to phase+progress+lines+cancel. **Size: M.**
4. **Local metadata store** — SQLite for restored-DB provenance, deploy history, cached
   environment state. *Risk:* low. **Size: S.**

### B. The five screens (obvious implementation candidates)

5. **Environments dashboard (read-only first)** — list apps/envs from Deploy API, status,
   version, region; stat row; manual refresh. For: every user, every session — it's the
   home screen. Beats Sprintr: one view across all customers vs. one portal per app.
   *Risk:* API auth model (see §2); "Degraded"/DB-size fields may be unavailable — design
   may need trimming. **Size: M.**
6. **Backups: list + create + download** — per-env snapshot list, "Create backup", download
   archive to data directory with progress and checksum verify. Beats Sprintr: batch
   download, no browser babysitting, files land where `pg_restore` needs them. *Risk:*
   archive-state polling and 8-hour URL expiry; large-file resume. **Size: M.**
7. **Restore to local Postgres [D]** — the flagship flow: dialog (target DB, clean/merge
   strategy, keep-file), phased progress, provenance recorded. Beats Sprintr *entirely* —
   Sprintr stops at "download a zip"; the manual unzip/`pg_restore` dance is the pain this
   app exists to remove. *Risk:* needs `pg_restore` on PATH or bundled; open-connection
   handling on drop; "merge" strategy is semantically murky for pg_restore (probably
   `--clean` vs. plain import — define it precisely or cut it for v1). **Size: L.**
8. **Local Databases screen [D]** — enumerate DBs (filtered to app-created ones via
   provenance store), sizes from `pg_database_size`, dump-to-file, drop with hard confirm.
   Server status via connection probe. Beats Sprintr: Sprintr has no local story at all.
   *Risk:* Stop/Restart buttons assume we control the Postgres service — only true if we
   installed/bundled it (see idea 17); otherwise ship the screen without service control.
   **Size: M.**
9. **Build & Deploy (deploy-only first)** — upload an existing `.mda`/package, transport
   to env, start with migrations flag, narrated log, history table. Beats Sprintr: switches
   like "backup before deploy" compose multi-step ceremonies into one click. *Risk:*
   production deploys bypass web 2FA **[D]**; app must be stopped for transport (downtime
   narration needed). Local *build* is a separate, bigger bet — see idea 10. **Size: M.**
10. **Local build (MxBuild integration)** — detect installed Studio Pro/MxBuild versions,
    match to project's Mendix version, run build, stream real output into LogViewer.
    For: consultants who deploy hotfixes without a CI pipeline. Beats Sprintr: Sprintr
    builds from Team Server only; this builds *what's on your disk*, uncommitted or not
    (which is also its danger). *Risk:* highest in the doc — per-version toolchains, Java
    deps, long build times; git info ("4 commits ahead") adds a libgit2 dependency.
    **Size: L.** Recommend: split from idea 9 and sequence after it.
11. **Settings: Credentials [S] + Database + Preferences** — vault-backed PAT/API-key
    storage, test-connection for both Mendix API and Postgres, data directory picker,
    preference persistence. *Risk:* credentials tab shape depends on the auth-model
    decision (§2); build Database+Preferences first. **Size: M.**

### C. Adjacent opportunities (hinted at in the designs)

12. **Slack deploy notifications** — exists as a switch in the Target card. Webhook URL in
    settings, message on deploy success/fail. For: teams where "I deployed to accp" is a
    Slack message anyway. *Risk:* trivial tech, but it's a distraction before deploys work;
    also implies a notifications abstraction (Teams next?). **Size: S.** Park until 9 ships.
13. **"Open in pgAdmin" / connect-out integrations** — exists as a tooltip. Cheapest
    version: copy connection string + launch pgAdmin/DBeaver/psql if found. For: everyone —
    the local DB is a means, not an end. *Risk:* none worth naming. **Size: S.** Strong
    quick win.
14. **Checksum verification** — exists as a preference. Verify archive integrity after
    download, before restore. *Risk:* API must expose a hash — not confirmed in Backups
    API docs; if absent, downgrade to size-check + zip integrity test. **Size: S.**
15. **Backup-before-deploy** — exists as a switch; composes idea 6's snapshot call into
    the deploy job. This is the kind of ceremony-compression Sprintr can't do in one click.
    **Size: S** (once jobs + backups exist).
16. **Deployment history with local memory** — the history table, but persisted locally
    including *who deployed from this machine, with which package and log*. Beats Sprintr:
    Sprintr's history lacks your local build context; keep the full log per deploy for
    post-mortems. **Size: S** (rides on the metadata store).

### D. Lateral / further out (raw, for the Visionair's radar)

17. **Bundled PostgreSQL** — ship portable Postgres binaries (or drive an embedded
    instance) so first-run is "click start", no separate install. For: consultants on fresh
    laptops. Beats requiring-an-install: removes the biggest onboarding cliff, and makes
    the mocked Stop/Restart/uptime UI honest. *Risk:* installer size (~50 MB), Windows
    service vs. child-process lifetime, upgrades between PG majors. **Size: L.**
    Alternative: detect existing install first, offer bundled as fallback.
18. **Scheduled local pulls** — "every night, snapshot Production and refresh
    `acme_local`". Turns the app from a tool into a safety net: you always have yesterday's
    prod data locally, even offline at the customer site. *Risk:* app must be running (MAUI
    has no service story) — or generate a Task Scheduler entry + CLI (see 19). **Size: L.**
19. **Headless CLI companion** — expose the same operations (`mendixtools restore
    --env acme-prod --db acme_local`) for scripts and CI. For: the power users this app
    targets; it's the difference between a GUI and a platform. *Risk:* scope creep; but the
    architecture cost is low *if* the job engine (idea 3) lives in a core library the GUI
    and CLI share — worth deciding early even if the CLI ships late. **Size: M–L.**
20. **Restore + run locally** — after restore, also unpack file documents and launch the
    app against the local DB with a matching Mendix runtime. The full "prod on my laptop"
    dream. *Risk:* runtime licensing/config, constants/secrets, per-version runtimes —
    genuinely hard; drifts toward rebuilding Studio Pro's run-locally. Note it, don't
    chase it yet. **Size: XL.**
21. **Data anonymisation on restore [D]** — optional SQL scrub step (emails, names) after
    import. For: consultants who legally shouldn't carry raw prod PII on a laptop — this
    may be the difference between the tool being *allowed* and *banned* at some customers.
    *Risk:* per-app column mapping needs config; a naive default could break apps. **Size: M–L.**
    Off the pure "local beats Sprintr" axis but directly enables the flagship flow's
    adoption; Visionair should weigh it as a compliance feature.
22. **Multi-profile credentials** — one PAT/API key per customer org, switchable. The mock
    shows one global PAT but three customers; real consultants often have separate
    accounts per customer. *Risk:* complicates every API call path; validate the need
    first. **Size: M.**
23. **Environment diff** — compare two environments (or local DB vs. cloud): Mendix
    version, constants, scheduled events, DB size drift. Nothing in Sprintr does this
    side-by-side. Speculative; park. **Size: M.**

---

## 4. Stress-test summary — the five unknowns that gate everything

1. **API generation & auth** — v1+API-key vs. v4+PAT decides the Credentials screen, the
   scopes story, and which environment fields we can actually show. *Action: spike —
   1 day with a real Mendix account.* ([Deploy v1](https://docs.mendix.com/apidocs-mxsdk/apidocs/deploy-api/),
   [Deploy v4](https://docs.mendix.com/apidocs-mxsdk/apidocs/deploy-api-4/),
   [Backups v2](https://docs.mendix.com/apidocs-mxsdk/apidocs/backups-api/),
   [PAT setup](https://docs.mendix.com/apidocs-mxsdk/mxsdk/set-up-your-pat/))
2. **Postgres: bundle vs. detect** — decides Databases-screen scope (service control or
   not), installer size, and first-run experience. Also: `pg_restore`/`pg_dump` client
   tools must be present either way.
3. **Local build feasibility** — MxBuild per-version toolchain management. If too hairy,
   Build & Deploy degrades gracefully to "deploy an existing package", which is still
   valuable. Don't let idea 10 block idea 9.
4. **MAUI Blazor constraints** — long-running jobs + streaming logs across the WebView
   boundary (fine: jobs run in .NET, UI is just Blazor state), file-picker and
   Task-Scheduler integration, `SecureStorage` on Windows. Low risk, but the LogViewer
   with thousands of lines needs virtualisation.
5. **Destructive-action policy** — one decision, applied everywhere: what does a confirm
   look like for drop/clean-restore/prod-deploy (type-the-name? env-name echo? non-prod
   default?). The voice guidelines already define the copy style; the *mechanism* is open.

---

## 5. My picks (for the Visionair)

1. **Ideas 1–4 (foundation) + 5 (Environments read-only)** — the shortest path to a real,
   on-design app that shows live data. Everything else stacks on it.
2. **Ideas 6+7 (backup download + restore-to-local)** — the flagship. This is the flow
   where "local beats Sprintr" is not an improvement but a category difference; if only
   one thing ships, it's this.
3. **Idea 13 (pgAdmin/connect-out)** — smallest effort-to-delight ratio in the list;
   makes restored DBs immediately usable.
4. **Idea 9 (deploy-only, without local build)** — real value, medium effort; explicitly
   decouple it from the MxBuild swamp (idea 10).
5. **Spike the auth model (stress-test #1) before committing screen designs** — it's the
   only unknown that can invalidate finished UI (the Credentials tab).

Weakest ideas, killed or parked honestly: 20 (restore+run — XL, drifts into rebuilding
Studio Pro), 23 (env diff — cool, unvalidated), 12 (Slack — fine but premature),
22 (multi-profile — validate the need first).
