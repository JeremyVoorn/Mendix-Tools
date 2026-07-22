namespace Mendix_Tools.Components.Layout;

/// <summary>
/// A minimal key/value persistence seam for <see cref="ThemeService"/>. Kept MAUI-free so
/// the service's logic is unit-testable without the MAUI runtime (MT-08 scaffold); the
/// production adapter <c>MauiThemeStore</c> wraps MAUI <c>Preferences.Default</c>.
/// </summary>
public interface IThemeStore
{
    /// <summary>Reads a stored string, or <paramref name="defaultValue"/> when absent.</summary>
    string Get(string key, string defaultValue);

    /// <summary>Persists a string value under <paramref name="key"/>.</summary>
    void Set(string key, string value);
}

/// <summary>
/// MT-07 — holds the app's light/dark choice and persists it via <see cref="IThemeStore"/>
/// (Windows: the MAUI local settings store), so the choice is restored on next app start.
/// The design system reads <c>[data-theme="dark"]</c> on <c>&lt;html&gt;</c>; applying that
/// attribute is a DOM concern and lives in the shell (JS interop, <c>mxt-interop.js</c>
/// <c>setTheme</c>) — this service only owns state + persistence and has no MAUI/Blazor
/// dependency. A localStorage mirror (written by <c>setTheme</c>) lets the inline boot
/// script in <c>index.html</c> avoid a flash; the store stays authoritative. Default is light.
/// </summary>
public sealed class ThemeService
{
    private const string PreferenceKey = "mxt-theme";
    private readonly IThemeStore _store;
    private bool _loaded;
    private bool _isDark;

    /// <summary>Injects the persistence seam (DI supplies the MAUI-backed adapter; tests a fake).</summary>
    public ThemeService(IThemeStore store)
    {
        _store = store;
    }

    /// <summary>True when dark mode is active. Reads the persisted value on first access.</summary>
    public bool IsDark
    {
        get
        {
            if (!_loaded)
            {
                _isDark = _store.Get(PreferenceKey, "light") == "dark";
                _loaded = true;
            }

            return _isDark;
        }
    }

    /// <summary>The theme string the shell hands to <c>setTheme</c>: <c>"dark"</c> or <c>"light"</c>.</summary>
    public string Current => IsDark ? "dark" : "light";

    /// <summary>Flips the theme and persists the new choice. Returns the new theme string.</summary>
    public string Toggle()
    {
        _isDark = !IsDark; // IsDark getter ensures _loaded is set before we flip.
        _loaded = true;
        _store.Set(PreferenceKey, _isDark ? "dark" : "light");
        return Current;
    }
}
