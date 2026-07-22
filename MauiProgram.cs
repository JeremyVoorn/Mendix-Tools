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

        // ── MT-08 metadata store (DI) — merge point with MT-10; keep this block intact ──
        // UI-agnostic SQLite store in the MAUI app-data directory. Registered as a
        // singleton (a connection is opened per operation, so it is safe to share).
        var metadataDbPath = Path.Combine(FileSystem.AppDataDirectory, "mendixtools.db");
        builder.Services.AddSingleton<IMetadataStore>(_ => new SqliteMetadataStore(metadataDbPath));
        // ── end MT-08 metadata store (DI) ──────────────────────────────────────────────

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