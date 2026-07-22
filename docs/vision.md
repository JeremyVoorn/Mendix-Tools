# Mendix Tools — Vision

> Owner: Visionair. This is the compass: the problem, the user, the north star,
> the principles, and the prioritised feature list. It feeds the Scrum-master, who
> slices "Now" items into stories.
> Last updated: 2026-07-22 — first version, after `docs/design-system/` and `docs/ideas.md`.

---

## The problem

A Mendix consultant runs many customer apps across many environments. Today, every
routine operation goes through Sprintr: log into a customer portal, click through to an
environment, trigger a snapshot, wait, download a zip, then hand-roll the local part —
unzip, find `pg_restore`, drop and recreate a database, import, repeat for the next app.
The cloud half is a web UI with per-app friction; the local half Mendix doesn't help with
at all. Multiply by 3 customers and 6 environments and the day is gone to babysitting
browser tabs and terminal commands.

## The user

A **Mendix consultant** (and the IT admin who does the same jobs) who:
- manages **multiple customer apps** on Mendix Cloud, often on **mixed Studio Pro
  versions** (9.24–10.12 in the mock data);
- works from **their own machine** with a **local PostgreSQL** as their working copy of
  customer data;
- is technical — comfortable with connection strings, `.mpk`/`.mda`, `pg_restore`, git —
  and wants a tool that respects that, not a consumer app that hides it.

## North star

**Everything a consultant does with a Mendix app in Sprintr, they can do faster and
calmer from their own machine — and the things Sprintr won't do at all (pull a cloud
snapshot straight into a ready-to-use local database) become one click.**

The single flow that defines the product: **cloud backup → local Postgres, restored and
usable, without leaving the app.** Sprintr stops at "download a zip." We finish the job.

## Why local beats Sprintr

- **One view across all customers** instead of one portal per app.
- **No web round-trips, no babysitting** — long jobs run in the background and survive
  navigation; the browser can't do that.
- **Files land where the tooling needs them**, and the restore-to-Postgres dance is
  automated end to end — a category Sprintr simply doesn't cover.
- **Offline-first where it can be** — previously downloaded backups and local databases
  work with no network; cloud views degrade to cached, clearly-marked stale state.
- **Scriptable later** — the same operations belong in a CLI for CI and power users.

## What this is NOT (non-goals)

- **Not a Sprintr replacement for team/collaboration features.** No stories, sprints,
  feedback, user management, app creation, or Team Server browsing beyond what a build
  or deploy needs. We manage *running apps and their data*, not the SDLC around them.
- **Not a Studio Pro replacement.** We do not model, edit, or "run locally" a full Mendix
  app with a bundled runtime. (Kills idea 20 — see below.)
- **Not a hosted/multi-user service.** It is a single-user, local desktop app. No server,
  no shared backend, no accounts of our own.
- **Not a general database IDE.** We manage the databases *this app creates or restores*,
  and hand off to pgAdmin/DBeaver/psql for everything else. We don't rebuild a SQL client.
- **Not a Mendix-branded product.** No official Mendix logo, mark, or branding.

---

## Product principles

Borrowed from the design system's voice — **direct, technical, calm.**

1. **State facts, not celebrations.** "Backup restored to `acme_local` (2.4 GB)." No hype,
   no emoji, no exclamation marks.
2. **Numbers and identifiers are data.** Versions, sizes, hosts, package names, and
   connection strings are exact and set in mono. Prefer the real value to a vague word.
3. **Errors say what happened and what to do next.** "Deploy failed — build exited with
   code 1. View logs."
4. **Destructive actions state their consequence and demand intent.** "This drops and
   recreates `acme_local`. This cannot be undone." One destructive-action policy, applied
   everywhere (see gating decision D5).
5. **Never surprise the user with a safety rail they didn't know they crossed.** The APIs
   bypass web-UI protections (e.g. production 2FA); we add back an explicit guard.
6. **Offline is a first-class state, not an error.** Show stale/cached clearly; let local
   work proceed.
7. **Local core, thin UI.** Operations live in a core library (jobs, API clients, Postgres
   orchestration) so a CLI can share them later. The Blazor UI is a renderer of state.
8. **One accent per view.** Colour carries state, nothing else.

---

## Prioritised feature list

Priority is **Now / Next / Later**. "Now" items are unambiguous enough to slice into
stories today. Numbers in brackets reference `docs/ideas.md`.

### Sequencing — read this first

1. **Auth-model spike DONE (D1/D4 resolved 2026-07-22).** The cloud half is unblocked:
   API-key generation, one credential pair, DB-only restore. Wired cloud work no longer
   waits on anything but live verification of 8 refinement items (which shape stories, not
   the architecture). Environments still ships mock-first for foundation reasons (below),
   not because auth is unresolved.
2. **Design-system foundation comes before screens.** Tokens + Razor primitives +
   AppShell first; every screen is cheap once they exist.
3. **Screen order:** Environments → Backups → Restore → (Settings: DB + Preferences) →
   Local Databases → Build & Deploy (deploy-only). This front-loads the north-star flow.
4. **Mock-first vs. wired-first:**
   - **Environments** ships **mock-first** (proves the foundation on the richest screen),
     then wires to the Deploy API after the auth spike.
   - **Backups + Restore** are **wired-first** — the flagship value is real API + real
     Postgres; mocking it proves nothing worth the effort.
   - **Local Databases** is **wired-first to real local Postgres** — it needs no cloud auth
     and works offline, so it's the earliest fully-real screen.
   - **Settings** ships **Database + Preferences wired-first**; the **Credentials** tab
     waits on the auth spike, then ships wired.

### NOW — the on-design app plus the flagship flow

| # | Feature | Pillar / value | Ideas |
|---|---|---|---|
| N1 | **Auth-model spike — DONE (MT-01, 2026-07-22).** D1/D4 resolved and recorded below. API-key generation, one credential pair, DB-only restore. Remaining: run `MT-01-verify.ps1` against a real account to settle 8 refinement items (snapshot Size/Type, 429, cross-org, archive layout). | Unblocked everything cloud | stress-test #1 |
| N2 | **Design tokens + Razor primitives** — port `tokens/*.css` 1:1; rebuild the 16 primitives, prioritising Button, Dialog, DataTable, LogViewer. Bundle fonts + Lucide locally for offline. | Foundation | 1 |
| N3 | **App shell + navigation + theme toggle** — sidebar, topbar, persisted dark mode. | Foundation | 2 |
| N4 | **Local metadata store (SQLite)** — restored-DB provenance, deploy history, cached env state. | Foundation | 4 |
| N5 | **Shared job engine** — background jobs with phases, progress, log lines, cancel; survives navigation. Scoped tight: phase + progress + lines + cancel, in the core library. | Foundation for Backups/Restore/Deploy; a Sprintr-impossible behaviour | 3 |
| N6 | **Environments dashboard (mock-first, then wired read-only)** — apps/envs from Deploy API v1, stat row, refresh. **Card fields settled by D1:** keep status (Running/Stopped/Empty only), Mendix version, host (`Url`), mode, production marker. **Trim: "Degraded", "Deploying", region, live DB size** (no API surface). **"Last backup": DECIDED — keep it, lazy-loaded.** The env payload carries no backup info; the only source is one Backups-v2 snapshots call per env (newest `created_at`) — the same call MT-14 already makes. Render cards immediately, fill each "Backup" cell as its call returns; show "—" for sandboxes (no backups). Mock (MT-10) must annotate the trimmed fields so no logic is built on them. | Backups pillar entry point; one view across all customers | 5 |
| N7 | **Backups: list + create + download** — per-env snapshots, create (optional comment), download archive (`data_type=database_only` per D4) to the data directory with progress. **Integrity check is local-only** (zip/tar test + `Content-Length`) — no API checksum exists. Handle the confirmed **8-hour URL expiry** (re-request on expiry) and **HTTP 429** (backoff). Snapshot Size/Type columns are unconfirmed — hold final column set until live verification. | Backups pillar; batch, no browser babysitting | 6, 14 |
| N8 | **Restore to local Postgres — the flagship** — dialog (target DB, clean/merge, keep-file), phased progress, provenance recorded. Define "merge" precisely or cut it for v1. Handle open connections on drop. | Backups pillar; the category difference vs. Sprintr | 7 |
| N9 | **Settings: Database + Preferences + Credentials (all now — D1 unblocks Credentials)** — Postgres connection + test, data directory picker, prefs. **Credentials tab, D1 shape:** two fields — `Mendix username` (plain) + `API key` (masked mono, reveal); Test = `GET /api/1/apps` (401→"credential rejected", 403→"no API Rights"). **Drop** the mocked `mx:deploy, mx:backups` scope tags (those scopes don't exist) and the "API region" row (fixed global endpoints). **Rename Revoke → Remove** (vault-delete only; real revocation happens in the Mendix profile — link out). Both values in `SecureStorage`. | Wire-it-once foundation | 11 |

### NEXT — make the local half complete and add deploy

| # | Feature | Pillar / value | Ideas |
|---|---|---|---|
| X1 | **Local Databases screen** — enumerate app-created DBs via provenance, sizes, dump-to-file, drop with hard confirm, server status via probe. Ship **without** service Stop/Restart unless we own Postgres (gated by D2). | Local; no cloud story exists in Sprintr | 8 |
| X2 | **Connect-out (pgAdmin/DBeaver/psql)** — copy connection string, launch a detected client. Smallest effort-to-delight ratio in the list. | Local; makes restored DBs instantly useful | 13 |
| X3 | **Build & Deploy (deploy-only)** — upload existing `.mda`/package, transport, start with migrations flag, narrated log, history. Explicitly decoupled from local build (see L1). | Build + Deploy pillars; ceremony-compression | 9 |
| X4 | **Deployment history with local memory** — persist who/what/log per deploy from this machine. | Deploy; local context Sprintr lacks | 16 |
| X5 | **Backup-before-deploy** — compose N7's snapshot into the deploy job. | Deploy; one-click ceremony | 15 |

### LATER — bets, platform plays, and compliance

| # | Feature | Value / note | Ideas |
|---|---|---|---|
| L1 | **Local build (MxBuild integration)** — per-version toolchain detection + build. The hairiest integration; sequence strictly after X3 and never let it block deploy. | Build pillar; builds what's on disk | 10 |
| L2 | **Bundled / detected PostgreSQL** — detect existing install; offer portable Postgres as fallback so first-run is "click start". Makes the mocked service-control UI honest. | Onboarding cliff | 17 |
| L3 | **Headless CLI companion** — same operations for scripts/CI. Architecturally cheap *if* the core library (N5) is kept UI-agnostic — decide that now, ship the CLI later. | Turns a GUI into a platform | 19 |
| L4 | **Data anonymisation on restore** — optional PII scrub after import. Off the pure "local beats Sprintr" axis, but may be what makes the flagship flow *allowed* (not banned) at customers holding prod PII. Re-evaluate for promotion once N8 has real adoption. | Compliance enabler | 21 |
| L5 | **Scheduled local pulls** — nightly prod snapshot → refreshed local DB. Needs a Task Scheduler entry + the CLI (L3); MAUI has no service story. | Safety net | 18 |
| L6 | **Multi-profile credentials** — one API key/PAT per customer org. Validate the need before building; it complicates every API call path. | Real for some consultants | 22 |
| L7 | **Slack deploy notifications** — the switch already in the design. Trivial, but premature until deploy ships; implies a notifications abstraction. | Team convenience | 12 |

### KILLED / PARKED

- **Idea 20 — Restore + run locally with a Mendix runtime.** Killed for the foreseeable
  future. XL effort, per-version runtimes, licensing/constants/secrets — it drifts into
  rebuilding Studio Pro's run-locally, which is a declared non-goal. If demand is real,
  the answer is "restore the DB and open your app in Studio Pro," not us hosting a runtime.
- **Idea 23 — Environment diff.** Parked. Genuinely nothing in Sprintr does side-by-side
  diff, but it's speculative and unvalidated. Revisit only if users ask.

---

## Open decisions this document will record (gating unknowns)

These are the calls the Visionair owns; the answers land here as they're made.

- **D1 — RESOLVED 2026-07-22 (accepted, MT-01 spike).** Target the **API-key generation**
  for the whole product: Deploy API v1 (+ v2 for >300 MB uploads) and Backups API v2, all
  on `Mendix-Username` + `Mendix-ApiKey`. **One credential pair covers environments,
  backups, and deploy.** Deploy API v4 (PAT) is skipped — it is read-only apps/environments
  only (no backups, no transport, no upload/start/stop), so adopting it would cost a second
  credential for zero capability we need. This is the right call against the north star: the
  flagship flow (backup → local Postgres) and deploy both live entirely in the API-key APIs,
  and a single credential is the calmest onboarding. Revisit only if Mendix extends the
  PAT API family to backups/transport.
  - **Consequences now baked into the Now-list:** the designed Credentials tab (MT-13)
    changes — see N9. Environment cards (N6) lose fields the API doesn't return — see N6.
  - **Guard reaffirmed:** Deploy API v1 does not require the web UI's production 2FA;
    principle 5 / D5 guard is mandatory for prod transport.
  - **Not yet live-verified (8 items, `docs/spikes/MT-01-verify.ps1`, needs Jeremy's
    account):** real snapshot JSON (does `size`/type exist? decides MT-14 columns), 429
    behaviour, cross-org coverage of `GET /api/1/apps` (decides whether L6 multi-profile
    rises), and the `database_only` archive layout (feeds D4/N8 unpack). These refine
    stories; none reopen the D1 decision.
- **D2 — Postgres: bundle vs. detect.** Decides whether the Local Databases screen gets
  real service control (Stop/Restart/uptime) or ships read-only. Client tools
  (`pg_restore`/`pg_dump`) must be present either way.
- **D3 — Local build feasibility.** If MxBuild per-version management is too costly, Build
  & Deploy stays deploy-only (X3) indefinitely; that is still valuable.
- **D4 — RESOLVED 2026-07-22 (accepted, MT-01 spike).** Default to **DB-only**
  (`data_type=database_only`, confirmed real and documented). The flagship `pg_restore`
  flow needs only the `db/*.backup`; `files_and_database` adds FileDocuments that matter
  only for running the app locally — a declared non-goal (killed idea 20). DB-only archives
  are smaller and soften the confirmed 8-hour download-URL expiry. Keep `files_and_database`
  out of the v1 UI (an overflow option at most). No API checksum exists, so N7/MT-16's
  **local integrity check (zip/tar test + `Content-Length`) is the primary mechanism, not a
  fallback.** One item to verify live: the internal layout of a `database_only` archive
  (single `.backup` vs. still tar.gz-wrapped) — feeds N8's unpack step.
- **D5 — Destructive-action mechanism.** One policy for drop / clean-restore / prod-deploy:
  type-the-name, env-name echo, or non-prod-by-default. Copy style is already set by the
  voice; the mechanism is the open part. Decide before N8 ships.
