namespace Mendix_Tools.Services;

/// <summary>
/// MT-20 — adapts <see cref="AppSettingsService"/> (which reads the OS vault via ISecretStore)
/// to <see cref="IMendixCredentialProvider"/>. Reads BOTH halves of the credential fresh on
/// every call, so a change made in Settings › Credentials takes effect on the next API call
/// without a restart. Returns <c>null</c> when either half is missing (first run / after
/// Remove) — the client turns that into a <see cref="MendixApiOutcome.NoCredentials"/> result.
///
/// Deliberately kept out of the test project's source-link set: tests use a fake
/// <see cref="IMendixCredentialProvider"/> and never touch the MAUI SecureStorage-backed vault.
/// </summary>
public sealed class AppSettingsMendixCredentialProvider : IMendixCredentialProvider
{
    private readonly AppSettingsService _settings;

    public AppSettingsMendixCredentialProvider(AppSettingsService settings) => _settings = settings;

    public async Task<MendixCredential?> GetCredentialAsync(CancellationToken ct = default)
    {
        var username = await _settings.GetMendixUsernameAsync().ConfigureAwait(false);
        var apiKey = await _settings.GetMendixApiKeyAsync().ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        return new MendixCredential(username, apiKey);
    }
}
