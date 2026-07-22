namespace Mendix_Tools.Services;

/// <summary>
/// MT-11/MT-12/MT-13 — the single typed settings surface the Settings screen writes and the
/// later cloud/restore stories read (MT-12 AC: "exposed via a typed settings service the
/// Backups/Restore stories consume"):
///   • MT-16 reads <see cref="VerifyChecksumAfterDownload"/> and <see cref="DataDirectory"/>;
///   • MT-18 reads <see cref="KeepBackupFilesAfterRestore"/> for the restore dialog default;
///   • MT-20 reads <see cref="AutoRefreshEnabled"/> for the 30s dashboard poll;
///   • MT-14/16/20 read the Mendix credential; MT-17 reads the Postgres connection.
///
/// Split by sensitivity, enforced by the two seams this service composes:
///   • NON-secret values (Postgres host/port/username, data directory, the three boolean
///     preferences) → <see cref="ISettingsStore"/> (MAUI Preferences). Synchronous.
///   • SECRETS (Mendix API key, Postgres password) → <see cref="ISecretStore"/> (OS vault).
///     Async, and never returned into any non-secret store, log, or rendered string.
///
/// The theme preference is intentionally NOT duplicated here: it is owned end-to-end by
/// <c>ThemeService</c> (the topbar toggle's single source of truth); the Preferences tab
/// drives that service directly so the two stay in sync (MT-12).
/// </summary>
public sealed class AppSettingsService
{
    // Non-secret preference keys (MAUI Preferences).
    private const string KeyDbHost = "mxt.db.host";
    private const string KeyDbPort = "mxt.db.port";
    private const string KeyDbUsername = "mxt.db.username";
    private const string KeyDataDirectory = "mxt.db.datadir";
    private const string KeyAutoRefresh = "mxt.pref.autorefresh";
    private const string KeyKeepBackupFiles = "mxt.pref.keepbackupfiles";
    private const string KeyVerifyChecksum = "mxt.pref.verifychecksum";

    // Secret keys (OS vault). Names only — never the values — appear in source.
    private const string SecretMendixUsername = "mxt.mendix.username";
    private const string SecretMendixApiKey = "mxt.mendix.apikey";
    private const string SecretDbPassword = "mxt.db.password";

    private readonly ISettingsStore _prefs;
    private readonly ISecretStore _secrets;

    public AppSettingsService(ISettingsStore prefs, ISecretStore secrets)
    {
        _prefs = prefs;
        _secrets = secrets;
    }

    // ── MT-11 Database (non-secret) ─────────────────────────────────────────────

    /// <summary>Local Postgres host. Defaults to the JSX mock's "localhost".</summary>
    public string DbHost
    {
        get => _prefs.GetString(KeyDbHost, "localhost");
        set => _prefs.SetString(KeyDbHost, value);
    }

    /// <summary>Local Postgres port. Stored as text (mono field); defaults to "5432".</summary>
    public string DbPort
    {
        get => _prefs.GetString(KeyDbPort, "5432");
        set => _prefs.SetString(KeyDbPort, value);
    }

    /// <summary>Local Postgres username (not a secret; the password is vault-only).</summary>
    public string DbUsername
    {
        get => _prefs.GetString(KeyDbUsername, "postgres");
        set => _prefs.SetString(KeyDbUsername, value);
    }

    /// <summary>Where restored .backup files and dumps are written (MT-16/MT-17 consume this).</summary>
    public string DataDirectory
    {
        get => _prefs.GetString(KeyDataDirectory, "");
        set => _prefs.SetString(KeyDataDirectory, value);
    }

    /// <summary>Parses <see cref="DbPort"/> to an int, falling back to 5432 on garbage input.</summary>
    public int DbPortNumber => int.TryParse(DbPort, out var p) && p is > 0 and <= 65535 ? p : 5432;

    // ── MT-12 Preferences (non-secret, instant-effect) ──────────────────────────

    /// <summary>Poll environment status in the background every 30s (MT-20 consumer; persists now).</summary>
    public bool AutoRefreshEnabled
    {
        get => _prefs.GetBool(KeyAutoRefresh, true);
        set => _prefs.SetBool(KeyAutoRefresh, value);
    }

    /// <summary>Keep downloaded .backup files after a restore completes (MT-18 dialog default).</summary>
    public bool KeepBackupFilesAfterRestore
    {
        get => _prefs.GetBool(KeyKeepBackupFiles, true);
        set => _prefs.SetBool(KeyKeepBackupFiles, value);
    }

    /// <summary>Run the local integrity check after a download (MT-16 primary check; D4 — no API checksum).</summary>
    public bool VerifyChecksumAfterDownload
    {
        get => _prefs.GetBool(KeyVerifyChecksum, true);
        set => _prefs.SetBool(KeyVerifyChecksum, value);
    }

    // ── MT-13 Mendix credential (SECRET — OS vault only) ─────────────────────────

    /// <summary>Reads the stored Mendix username, or <c>null</c> when unset.</summary>
    public Task<string?> GetMendixUsernameAsync() => _secrets.GetAsync(SecretMendixUsername);

    /// <summary>Reads the stored Mendix API key, or <c>null</c> when unset.</summary>
    public Task<string?> GetMendixApiKeyAsync() => _secrets.GetAsync(SecretMendixApiKey);

    /// <summary>True when both halves of the credential pair are present in the vault.</summary>
    public async Task<bool> HasMendixCredentialAsync()
    {
        var user = await GetMendixUsernameAsync().ConfigureAwait(false);
        var key = await GetMendixApiKeyAsync().ConfigureAwait(false);
        return !string.IsNullOrWhiteSpace(user) && !string.IsNullOrWhiteSpace(key);
    }

    /// <summary>Writes the Mendix username + API key to the OS vault (MT-13 Save).</summary>
    public async Task SetMendixCredentialAsync(string username, string apiKey)
    {
        await _secrets.SetAsync(SecretMendixUsername, username).ConfigureAwait(false);
        await _secrets.SetAsync(SecretMendixApiKey, apiKey).ConfigureAwait(false);
    }

    /// <summary>Updates just the Mendix username, leaving an already-stored API key untouched
    /// (MT-13 Save when the masked key field is left blank to keep the existing key).</summary>
    public Task SetMendixUsernameAsync(string username) => _secrets.SetAsync(SecretMendixUsername, username);

    /// <summary>Deletes the Mendix credential from the OS vault only (MT-13 Remove — no server call).</summary>
    public void RemoveMendixCredential()
    {
        _secrets.Remove(SecretMendixUsername);
        _secrets.Remove(SecretMendixApiKey);
    }

    // ── MT-11 Postgres password (SECRET — OS vault only) ─────────────────────────

    /// <summary>Reads the stored Postgres password, or <c>null</c> when unset.</summary>
    public Task<string?> GetDbPasswordAsync() => _secrets.GetAsync(SecretDbPassword);

    /// <summary>True when a Postgres password is present in the vault.</summary>
    public async Task<bool> HasDbPasswordAsync()
        => !string.IsNullOrEmpty(await GetDbPasswordAsync().ConfigureAwait(false));

    /// <summary>Writes the Postgres password to the OS vault (MT-11 Save).</summary>
    public Task SetDbPasswordAsync(string password) => _secrets.SetAsync(SecretDbPassword, password);

    /// <summary>Deletes the Postgres password from the OS vault.</summary>
    public void RemoveDbPassword() => _secrets.Remove(SecretDbPassword);
}
