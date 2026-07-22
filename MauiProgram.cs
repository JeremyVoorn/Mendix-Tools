using MendixTools.Core.Metadata;
using Microsoft.Extensions.Logging;

namespace Mendix_Tools;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts => { fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular"); });

        builder.Services.AddMauiBlazorWebView();

        // Feedback primitives (MT-05): one ToastService drives the single app-root ToastStack.
        builder.Services.AddSingleton<Mendix_Tools.Components.UI.ToastService>();

        // App shell (MT-07): persisted theme + per-page topbar contract. One shell for the
        // app's lifetime in the BlazorWebView, so both are singletons.
        builder.Services.AddSingleton<Mendix_Tools.Components.Layout.IThemeStore, Mendix_Tools.Components.Layout.MauiThemeStore>();
        builder.Services.AddSingleton<Mendix_Tools.Components.Layout.ThemeService>();
        builder.Services.AddSingleton<Mendix_Tools.Components.Layout.ShellState>();

        // ── MT-08 metadata store (DI) ──
        // UI-agnostic SQLite store in the MAUI app-data directory. Registered as a
        // singleton (a connection is opened per operation, so it is safe to share).
        var metadataDbPath = Path.Combine(FileSystem.AppDataDirectory, "mendixtools.db");
        builder.Services.AddSingleton<IMetadataStore>(_ => new SqliteMetadataStore(metadataDbPath));

        // ── MT-09 job engine (DI) ─────────────────────────────────────────────────────
        // UI-agnostic background-job backbone for the flagship flows (backup download,
        // restore, deploy). Singleton so jobs survive Blazor navigation; persists terminal
        // job history via the MT-08 store and writes each job's retained log under app-data.
        var jobLogDirectory = Path.Combine(FileSystem.AppDataDirectory, "job-logs");
        builder.Services.AddSingleton<MendixTools.Core.Jobs.IJobEngine>(sp =>
            new MendixTools.Core.Jobs.JobEngine(
                sp.GetRequiredService<IMetadataStore>(),
                logDirectory: jobLogDirectory));
        // ── end MT-09 job engine ──────────────────────────────────────────────────────

        // ── MT-10 Environments seam ──
        // The dashboard talks only to IEnvironmentService; MT-20 swaps this one line for
        // the real Deploy-v1/Backups-v2 client with no page changes. Mock carries no state.
        builder.Services.AddSingleton<Mendix_Tools.Services.IEnvironmentService,
            Mendix_Tools.Services.MockEnvironmentService>();

        // ── MT-14 Backups seam ──
        // The Backups screen talks only to IBackupService; MT-20 swaps this one line for the
        // real Backups API v2 client with no page changes. Mock carries no state, no secrets,
        // and makes no HTTP call. (Merge note: MT-20 adds a DIFFERENT Services/ file here.)
        builder.Services.AddSingleton<Mendix_Tools.Services.IBackupService,
            Mendix_Tools.Services.MockBackupService>();
        // ── end MT-14 Backups seam ──

        // ── MT-11/12/13 Settings (DI) ──
        // Secrets (Mendix API key, Postgres password) live ONLY in the OS vault via
        // ISecretStore→SecureStorage; non-secret prefs (host/port/user/data-dir + the three
        // preference switches) via ISettingsStore→Preferences. AppSettingsService is the one
        // typed surface MT-14/16/17/18/20 read. IPostgresProbe backs the user-initiated
        // "Test connection" against the LOCAL Postgres (Npgsql); IFolderPicker is the
        // data-directory browse. All singletons — one settings surface for the app lifetime.
        builder.Services.AddSingleton<Mendix_Tools.Services.ISecretStore, Mendix_Tools.Services.MauiSecretStore>();
        builder.Services.AddSingleton<Mendix_Tools.Services.ISettingsStore, Mendix_Tools.Services.MauiSettingsStore>();
        builder.Services.AddSingleton<Mendix_Tools.Services.AppSettingsService>();
        builder.Services.AddSingleton<Mendix_Tools.Services.IPostgresProbe, Mendix_Tools.Services.NpgsqlPostgresProbe>();
        builder.Services.AddSingleton<Mendix_Tools.Services.IFolderPicker, Mendix_Tools.Services.MauiFolderPicker>();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        var app = builder.Build();

        // MT-08 — create the DB file and run migrations on startup (AC: "on first run,
        // when the app starts, a SQLite DB is created … with schema/migrations").
        app.Services.GetRequiredService<IMetadataStore>().InitializeAsync().GetAwaiter().GetResult();

        return app;
    }
}