# Release process & auto-update (for agents)

This note is for the agent team (Scrum-master, Ontwikkelaar, Tester/Reviewer). It records
how Mendix Tools ships to users so nobody has to reverse-engineer it from the workflow file.

## TL;DR

- **How a release is cut:** push a version tag (`vX.Y.Z`). That's the only trigger.
- **What ships:** a Velopack **single-file installer** (`Setup.exe`) + an update manifest,
  published to a **GitHub Release**.
- **Auto-update:** installed apps check the GitHub Releases feed on startup, download a newer
  version in the background, and apply it on the next launch.

## The pieces (keep these in sync)

| Concern | Where |
|---|---|
| Trigger + build + pack + publish | [`.github/workflows/release.yml`](../.github/workflows/release.yml) |
| Update-framework bootstrap (`VelopackApp.Build().Run()`) | [`MauiProgram.cs`](../MauiProgram.cs), first line of `CreateMauiApp` under `#if WINDOWS` |
| Update client (checks feed, downloads, notifies) | [`Platforms/Windows/Services/WindowsUpdateService.cs`](../Platforms/Windows/Services/WindowsUpdateService.cs) |
| NuGet dependency | `Velopack` 1.2.0 in [`Mendix Tools.csproj`](../Mendix%20Tools.csproj), scoped to the Windows TFM |
| CLI used by CI | `vpk` (Velopack global tool), pinned to 1.2.0 |

**Version pinning rule:** the `Velopack` package, the `vpk` CLI, and the docs must all use the
**same version** (currently `1.2.0`). Velopack requires the runtime package and the packing CLI
to match. If you bump one, bump all three.

## How to cut a release

```bash
git tag vX.Y.Z
git push main vX.Y.Z    # NB: this repo's git remote is named "main", not "origin"
```

- The tag drives the version — do **not** hand-edit `ApplicationDisplayVersion` in the csproj.
- Release notes are generated automatically from `git log` since the previous tag.
- Use a **new, higher** version each time. Never move a published tag — installed clients key
  off the version, and rewriting a released version breaks their update state.

## What the workflow does

1. Derives the version from the tag and builds a changelog from the git log.
2. Publishes the MAUI app **self-contained** for `win10-x64` (no runtime prerequisite on the
   user's machine).
3. `vpk pack` turns the publish folder into `Setup.exe` + release packages (with delta updates
   against the previous release when one exists).
4. `vpk upload github` creates/publishes the GitHub Release and uploads the assets.

## Known limitations / backlog candidates

- **Unsigned.** Installer and app are unsigned → SmartScreen warns on first install. A
  code-signing certificate (wired into `vpk pack --signParams`) would remove this. Worth a
  backlog story if the tool is ever distributed beyond the author.
- **Windows only.** macOS (MacCatalyst) is stubbed as a commented `macos` job in the workflow
  and a TODO in the csproj — Velopack supports `.pkg` + auto-update, but it needs Apple signing
  before it works on other Macs.
- **Toast timing.** The "update ready" toast fires ~5s after startup (once the WebView shell is
  up). The update still downloads/applies regardless of whether the toast is seen.
- **First-run testing.** Because the Windows build only runs in CI, the first tag after any
  change to the release pipeline is effectively the integration test — watch the Actions run.
