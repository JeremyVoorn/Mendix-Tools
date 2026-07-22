# Mendix Tools — Design System

> Geïmporteerd uit claude.ai/design project `3bc0777b-f4f0-4449-9fb5-db684a3fa82f` op 2026-07-22.

A design system for **Mendix Tools**, a desktop utility for Mendix consultants. The app (built as a **.NET MAUI Blazor Hybrid** application) helps consultants manage their Mendix environments end-to-end: browsing cloud environments, downloading and restoring backups into a **local PostgreSQL** server, and building and deploying packages.

This system gives design agents everything needed to produce on-brand Mendix Tools screens, mocks, and prototypes: color and type tokens (light + dark), reusable React primitives, foundation specimen cards, and a full interactive UI-kit recreation of the app.

## Sources
This system was created **from a written brief only** — no codebase, Figma file, screenshots, or brand assets were provided. Direction (light-first with a polished dark mode, humanist-sans typography, blue accent, comfortable density, 8px radius) was chosen with the user and built from scratch. If a real codebase, Figma library, or brand kit exists, attach it and this system should be reconciled against it.

> "Mendix Tools" here refers to this **consultant utility**, not the official Mendix low-code platform. No official Mendix branding or logo is used or reproduced.

## Fonts
- **IBM Plex Sans** — UI and prose.
- **IBM Plex Mono** — data, identifiers, versions, hosts, sizes, logs.

⚠️ **Font substitution:** no brand fonts were supplied, so IBM Plex is used and loaded from **Google Fonts (gstatic CDN)** via `@font-face` in `tokens/fonts.css`. These are CDN references, not bundled binaries. **If you have licensed/self-hosted fonts, send them and I'll swap the `src` URLs** for offline use.

## Logo
⚠️ **No logo was supplied.** The brand is set in type as a wordmark — **Mendix Tools** — with a placeholder monospace "mt" initials tile as the app glyph. This tile is a stand-in, not an official mark. **Please provide a real logo/mark** and it will replace the placeholder in the sidebar, thumbnail, and wordmark card.

---

## CONTENT FUNDAMENTALS
The voice is **direct, technical, and calm** — a tool made by consultants for consultants.

- **Casing:** Sentence case for labels, buttons, and messages ("Create backup", "Restore to local Postgres"). Title Case only for proper product/screen names ("Build & Deploy"). ALL-CAPS reserved for tiny eyebrow labels (metadata keys like `DB SIZE`), letter-spaced.
- **Person:** Second person, implicit. Buttons are imperatives ("Restore", "Deploy", "Test connection"). Status is stated as fact ("Backup restored to `acme_local` (2.4 GB)."), not celebrated.
- **Numbers & identifiers are data:** versions (`10.12.4`), sizes (`2.4 GB`), hosts (`localhost:5432`), package names (`acme-10.12.4.mpk`), and connection strings are always set in **mono**. Prefer exact values over vague words.
- **Messages** say *what happened* and *what to do next*: "Deploy failed — build exited with code 1. View logs." Confirmations state consequences plainly: "This drops and recreates `acme_local`. This cannot be undone."
- **No hype, no filler, no emoji.** Avoid "Oops!", "Woohoo!", exclamation marks, and rhetorical "Are you sure you really…". See the **Voice & tone** brand card for do/don't examples.
- **Terminology:** Environment, Backup, Restore, Package (`.mpk`), Deploy, Local database, PAT (Personal Access Token). Environments are named `<app> · <Production|Acceptance|Test>`.

## VISUAL FOUNDATIONS
- **Mood:** sleek, technical, information-dense but comfortable. A developer tool, not a consumer app.
- **Color:** neutral **cool-slate** canvas with a single **blue** accent (`--blue-600` light / `--blue-500` dark). Semantic hues map to state: green = running/success, amber = degraded/warning, red = stopped/failed/destructive. Two domain accents: **teal** for Postgres/databases (`--db`), **violet** for packages/artifacts (`--package`). Max one accent per view; color is used sparingly and always meaningfully (mostly through Badge/StatusDot).
- **Themes:** light-first (`:root`) with a fully realized dark theme (`[data-theme="dark"]` on `<html>` or any container). Dark uses a slightly blue-black canvas (`#0b111b`) and raised surfaces, not pure gray.
- **Type:** IBM Plex Sans for everything readable; IBM Plex Mono for all data. Base UI size 14px. Headings semibold with slight negative tracking.
- **Spacing:** 4px base grid; comfortable control heights (28/34/40px). Generous 24px screen padding, 14px gaps between cards.
- **Backgrounds:** flat. No gradients, no imagery, no textures or patterns. Depth comes from surface layering (`--bg-app` < `--bg-surface`) and subtle shadow — never from color washes.
- **Borders:** hairline `1px` `--border`; a slightly stronger `--border-strong` on inputs and secondary buttons. Dividers inside cards use the fainter `--border-subtle`.
- **Radii:** 8px is the default (buttons, inputs, cards use `--radius-lg`/`--radius-xl`); pills (`--radius-full`) for badges/tags/switch tracks.
- **Shadows:** soft, low-opacity, cool-tinted. `sm` for resting cards, `md` on hover, `lg` for popovers/toasts, `xl` for dialogs. Dark theme uses deeper black shadows.
- **Cards:** white (light) / raised slate (dark) surface, hairline border, `--radius-xl`, `--shadow-sm`. Optional header (title + subtitle + right-aligned actions) divided by a subtle border. Interactive cards lift to `--shadow-md` and darken their border on hover.
- **Animation:** quick and restrained. `--duration-fast` (120ms) for hovers/presses, `--duration-normal` (200ms) for toggles/dialogs, `--ease-standard`/`--ease-out`. Dialogs fade + pop 8px; toasts slide in. Running jobs use a pulsing StatusDot and indeterminate/determinate ProgressBar. **No bounces, no decorative motion.**
- **Hover states:** buttons darken (solid) or pick up `--bg-hover` (ghost/secondary); rows tint with `--bg-hover`; icon buttons get a faint fill.
- **Press states:** solid buttons nudge down ~0.5px and scale to 0.99; icon buttons scale to 0.94. No color inversion.
- **Focus:** 2px `--focus-ring` outline (offset), plus a `--shadow-focus` ring on inputs.
- **Transparency/blur:** used only for the modal overlay (`--overlay` + a 2px backdrop blur) and subtle dark-theme tint fills on badges. Not decorative.
- **Layout:** fixed 248px sidebar, fixed 52px topbar, scrolling content capped at 1200px and centered.

## ICONOGRAPHY
- **Library:** [**Lucide**](https://lucide.dev) — outline icons, ~1.5px stroke, rounded joins. Matches the clean technical feel. Loaded from CDN (`unpkg.com/lucide`) in cards and the UI kit; call `lucide.createIcons()` after render.
- **Substitution note:** no brand icon set was provided, so Lucide is the chosen standard. If the real app uses a specific set (e.g. Fluent UI System Icons, common in .NET MAUI apps), attach it and icons should be swapped.
- **Usage:** icons are functional, not decorative — one leading icon per button/nav item/status. Sizes 14–17px inline, 20–22px in dialog/section headers. `IconButton` wraps a Lucide name for toolbar/row actions. Colors inherit `currentColor` (usually `--text-secondary`), tinted only to carry state (danger red, db teal).
- **Common glyphs:** `layout-grid` (environments), `database-backup` (backups), `rocket` (deploy), `database` (local DB), `settings`, `refresh-cw`, `download`, `play`, `trash-2`, `more-horizontal`, `git-branch`, `terminal`, `plug`, `check-circle-2`, `alert-triangle`.
- **No emoji.** No hand-drawn or bespoke SVG icons. Unicode is not used as iconography.

---

## Components
React primitives under `components/<group>/`.

**Forms** (`components/forms/`)
- **Button** — primary/secondary/ghost/subtle/danger actions, sm/md/lg, icons, loading.
- **IconButton** — square icon-only action for toolbars and table rows.
- **Input** — labeled text field; `mono` mode for data; error/hint, icons, slots.
- **Select** — styled native dropdown matching Input.
- **Checkbox** — multi-select + indeterminate ("select all") state.
- **Radio** — single choice within a named group.
- **Switch** — instant-effect on/off toggle for settings.

**Display** (`components/display/`)
- **Card** — surface container with optional header/actions; interactive variant.
- **Badge** — status/label pill (success/warning/danger/info/db/package/accent/neutral).
- **StatusDot** — live status indicator, optional pulse, optional label.
- **Tag** — filter/selection chip, removable, `mono` for identifiers.
- **Tabs** — in-screen view switcher with counts and icons.

**Feedback** (`components/feedback/`)
- **Dialog** — modal for confirmations and short forms.
- **Toast** + **ToastStack** — transient result notifications with a fixed stack container.
- **Tooltip** — hover hint for icon buttons and truncated values.
- **ProgressBar** — determinate/indeterminate progress for long-running jobs.

**Data** (`components/data/`)
- **DataTable** — selectable table with custom cell renderers and mono columns.
- **LogViewer** — dark terminal-style console for build/deploy/restore output.

## UI Kits
- **`ui_kits/app/`** — full interactive recreation of the Mendix Tools desktop app: `AppShell` + five screens (Environments, Backups, Build & Deploy, Local Databases, Settings). See its `README.md`.
