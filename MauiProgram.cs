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
        builder.Services.AddSingleton<Mendix_Tools.Components.Layout.ThemeService>();
        builder.Services.AddSingleton<Mendix_Tools.Components.Layout.ShellState>();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}