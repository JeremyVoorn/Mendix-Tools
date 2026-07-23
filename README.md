# Mendix Tools

A desktop utility for **Mendix consultants** that removes the manual friction between the Mendix cloud and your local machine. It talks to the Mendix Platform APIs and to a **local PostgreSQL** server so you can, from one app:

- **Browse cloud environments** across every app your account can see (production / acceptance / test, plus personal sandboxes) — status, Mendix version, host, and last backup at a glance.
- **Manage backups** — list cloud snapshots per environment, create a new snapshot, and download a snapshot's database archive with a live progress + integrity check.
- **Restore a snapshot into local PostgreSQL** — the flagship: pull a cloud backup straight into a ready-to-use local database in one guarded flow (this is what Sprintr does not do).

> "Mendix Tools" here is this **consultant utility**, not the official Mendix low-code platform. No official Mendix branding is used.

The north star and the feature roadmap live in [`docs/vision.md`](docs/vision.md); the story-by-story build state is in [`docs/backlog.md`](docs/backlog.md); the design system it's built from is under [`docs/design-system/`](docs/design-system/).

---

## ⚠️ Handling production data

Restoring is **destructive to local data**. A "clean restore" **drops and recreates** the target local database, and the data you pull down may be a **copy of customer production data**. Two consequences:

- Every irreversible action is gated behind a **typed-confirmation guard** — you must type the exact database name before the drop proceeds.
- Always restore into a **throwaway / disposable** local database, never over one holding data you care about.

---

## Requirements

### To run the app
| Dependency | Why | Notes |
|---|---|---|
| **Windows 10 (build 17763+) / 11** | Primary target | The app also targets Android, iOS and MacCatalyst, but development so far is Windows-only. |
| **PostgreSQL client tools** — `pg_restore.exe` and `psql.exe` | Backup download & restore shell out to them | **Must be reachable on your `PATH`.** See [PostgreSQL client tools](#postgresql-client-tools) below — this is the most common setup snag. |
| **A local PostgreSQL server** | Restore target | Configure host/port/credentials in **Settings › Database**; use **Test connection** to verify. |
| **A Mendix account + API key** | Lists environments, pulls/creates backups | Enter your **Mendix username + API key** in **Settings › Credentials**. Stored only in the OS credential vault (never on disk in plaintext). |

### To build from source
| Dependency | Version |
|---|---|
| **.NET SDK** | 10.0 |
| **.NET MAUI workload** | `dotnet workload install maui` |
| **NuGet packages** (restored automatically) | `Microsoft.Maui.Controls`, `Microsoft.AspNetCore.Components.WebView.Maui`, `Microsoft.Extensions.Http` 10.0.0, `Microsoft.Extensions.Logging.Debug` 10.0.0, `Npgsql` 10.0.3 (local-Postgres probe/restore), `Microsoft.Data.Sqlite` 10.0.0 + `SQLitePCLRaw.bundle_e_sqlite3` 3.0.4 (local metadata store) |
| **Test frameworks** | xUnit (core logic) + bUnit (Razor components) |

Fonts (IBM Plex Sans/Mono) and icons (Lucide) are **bundled offline** — no CDN needed at runtime.

---

## PostgreSQL client tools

The download and restore features run PostgreSQL's command-line tools (`pg_restore`, `psql`). They ship with any standard PostgreSQL install (e.g. `C:\Program Files\PostgreSQL\<version>\bin`), but the installer usually does **not** add that `bin` folder to your `PATH`, so the app can't find them and reports:

> `pg_restore not found — install PostgreSQL client tools or set the path in Settings.`

**Fix — add the `bin` folder to your user `PATH`, then fully restart the app.** For a default v18 install:

```powershell
$bin = 'C:\Program Files\PostgreSQL\18\bin'
$u = [Environment]::GetEnvironmentVariable('Path','User')
if ($u -notlike "*$bin*") { [Environment]::SetEnvironmentVariable('Path', ($u.TrimEnd(';') + ';' + $bin), 'User') }
```

(Adjust the version number to match your install.) Verify with `pg_restore --version` in a **new** terminal.

> **Known gap:** the error message mentions "set the path in Settings", but that setting is **not built yet** (tracked as story **MT-11b** — auto-detect `C:\Program Files\PostgreSQL\*\bin` plus a Settings override). Until then, the `PATH` route above is the way to point the app at the tools.

---

## Getting started

```bash
# from the repo root
dotnet workload install maui        # once, if not already installed
dotnet build "Mendix Tools.csproj" -f net10.0-windows10.0.19041.0
dotnet run   --project "Mendix Tools.csproj" -f net10.0-windows10.0.19041.0
```

Then, in the app:

1. **Settings › Credentials** — enter your Mendix username + API key.
2. **Settings › Database** — enter your local PostgreSQL host/port/user/password and data directory; click **Test connection**.
3. **Environments** — confirm your apps/environments load.
4. **Backups** — pick an environment, then create / download / restore a snapshot. For a restore, use a **disposable** target database.

### Run the tests

```bash
dotnet test MendixTools.Core.Tests/MendixTools.Core.Tests.csproj          # core logic (xUnit)
dotnet test MendixTools.Components.Tests/MendixTools.Components.Tests.csproj  # Razor components (bUnit)
```

---

## Releasing

Releases are automated by [`.github/workflows/release.yml`](.github/workflows/release.yml).
Pushing a version tag builds the unpackaged Windows app and publishes a GitHub
Release with an auto-generated changelog and the build zip attached:

```bash
git tag v1.2.0
git push origin v1.2.0
```

The tag (minus the leading `v`) becomes `ApplicationDisplayVersion`, so there is
no need to hand-edit the version in `Mendix Tools.csproj`.

> The published `.exe` is **unsigned**, so Windows SmartScreen will warn on first
> run on another machine. That is fine for personal use; distributing more widely
> would need a code-signing certificate.

---

## Project layout

```
Mendix Tools.csproj          .NET MAUI Blazor Hybrid app (Windows/Android/iOS/MacCatalyst)
  Components/UI/             Design-system primitives ported to Razor (Button, Dialog, DataTable, …)
  Components/Layout/         App shell (sidebar + topbar), theme service, page-header state
  Components/Pages/          Screens: Environments, Backups, Deploy, Databases, Settings
  Services/                 Mendix API client, backup/restore orchestration, settings, Postgres probe
  wwwroot/css/              Design tokens + component styles; fonts bundled under wwwroot/fonts
MendixTools.Core/            UI-agnostic core: SQLite metadata store, background job engine, integrity
MendixTools.Core.Tests/      xUnit tests for the core
MendixTools.Components.Tests/ bUnit tests for the Razor components
docs/                        vision, backlog, the imported design system, and API spikes
```

---

## Security notes

- **Credentials** (Mendix API key, local Postgres password) live **only** in the OS credential vault (MAUI `SecureStorage`) — never in the metadata database, logs, or on disk in plaintext.
- **The app never verifies credentials by silently calling the API** on startup — the first real call happens when you open a cloud screen, and an invalid key surfaces a clear "Credentials invalid — check Settings" message rather than a crash.
- **Destructive actions** (clean restore; future: drop database, production deploy) require typing the exact target identifier to confirm.
