namespace Mendix_Tools.Services;

/// <summary>
/// MT-11/MT-12 non-secret preference seam. Backs the fields that are NOT secrets — Postgres
/// host/port/username, the data directory, and the boolean preferences (auto-refresh,
/// keep-backup-files, verify-checksum). On Windows the production adapter
/// (<see cref="MauiSettingsStore"/>) persists these in MAUI <c>Preferences</c> (the app's
/// local settings store), so they survive restart.
///
/// Secrets never travel through this seam — they go to <see cref="ISecretStore"/> only.
/// Kept MAUI-free so <see cref="AppSettingsService"/> logic is testable with a fake.
/// </summary>
public interface ISettingsStore
{
    string GetString(string key, string defaultValue);
    void SetString(string key, string value);
    bool GetBool(string key, bool defaultValue);
    void SetBool(string key, bool value);
}
