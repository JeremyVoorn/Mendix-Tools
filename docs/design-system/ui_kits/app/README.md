# Mendix Tools — App UI Kit

Interactive recreation of the **Mendix Tools** desktop app (a .NET MAUI Blazor Hybrid application for Mendix consultants). One shared shell (`AppShell`) with a sidebar + topbar, and five screens wired into a click-through in `index.html`.

## Files
- `AppShell.jsx` — sidebar nav, wordmark, account footer, topbar (title/subtitle/actions), theme toggle.
- `EnvironmentsScreen.jsx` — dashboard: stat row + environment cards across multiple customer apps.
- `BackupsScreen.jsx` — cloud backup list, restore-to-local-Postgres dialog (strategy, target DB), live restore progress.
- `DatabasesScreen.jsx` — local PostgreSQL server status, storage, and restored-database table.
- `DeployScreen.jsx` — package build config, deploy target, streaming build/deploy log, deployment history.
- `SettingsScreen.jsx` — tabbed Credentials / Database / Preferences.

## Composition
Screens compose design-system primitives (Card, Button, DataTable, Badge, StatusDot, Dialog, ProgressBar, LogViewer, etc.) — they do **not** re-implement primitives.

All data is mock/hardcoded — this is a visual + interaction recreation, not production code.
