using Microsoft.Maui.Storage;

namespace Mendix_Tools.Components.Layout;

/// <summary>
/// Production <see cref="IThemeStore"/> backed by MAUI <see cref="Preferences"/> (on Windows,
/// the app's local settings store). Isolated in its own file so the MAUI dependency stays out
/// of <see cref="ThemeService"/>, keeping that service unit-testable without the MAUI runtime.
/// </summary>
public sealed class MauiThemeStore : IThemeStore
{
    public string Get(string key, string defaultValue) => Preferences.Default.Get(key, defaultValue);

    public void Set(string key, string value) => Preferences.Default.Set(key, value);
}
