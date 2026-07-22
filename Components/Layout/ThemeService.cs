using Microsoft.Maui.Storage;

namespace Mendix_Tools.Components.Layout;

/// <summary>
/// MT-07 — holds the app's light/dark choice and persists it in MAUI
/// <see cref="Preferences"/> (Windows: the app's local settings store), so the choice
/// is restored on next app start. The design system reads <c>[data-theme="dark"]</c> on
/// <c>&lt;html&gt;</c>; applying that attribute is a DOM concern and lives in the shell
/// (JS interop, <c>mxt-interop.js</c> <c>setTheme</c>) — this service is UI-agnostic and
/// only owns state + persistence. A localStorage mirror (written by <c>setTheme</c>) lets
/// the inline boot script in <c>index.html</c> avoid a flash; Preferences stays
/// authoritative. Default is light.
/// </summary>
public sealed class ThemeService
{
    private const string PreferenceKey = "mxt-theme";
    private bool _loaded;
    private bool _isDark;

    /// <summary>True when dark mode is active. Reads the persisted value on first access.</summary>
    public bool IsDark
    {
        get
        {
            if (!_loaded)
            {
                _isDark = Preferences.Default.Get(PreferenceKey, "light") == "dark";
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
        Preferences.Default.Set(PreferenceKey, _isDark ? "dark" : "light");
        return Current;
    }
}
