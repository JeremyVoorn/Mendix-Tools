using Microsoft.Maui.Storage;

namespace Mendix_Tools.Services;

/// <summary>
/// Production <see cref="ISettingsStore"/> backed by MAUI <see cref="Preferences"/> (on
/// Windows, the app's local settings store). Only NON-secret values are ever stored here —
/// see <see cref="ISettingsStore"/>. Isolated so the MAUI dependency stays out of the
/// settings logic.
/// </summary>
public sealed class MauiSettingsStore : ISettingsStore
{
    public string GetString(string key, string defaultValue) => Preferences.Default.Get(key, defaultValue);

    public void SetString(string key, string value) => Preferences.Default.Set(key, value);

    public bool GetBool(string key, bool defaultValue) => Preferences.Default.Get(key, defaultValue);

    public void SetBool(string key, bool value) => Preferences.Default.Set(key, value);
}
