using Microsoft.Maui.Storage;

namespace Mendix_Tools.Services;

/// <summary>
/// Production <see cref="ISecretStore"/> backed by MAUI <see cref="SecureStorage"/> — on
/// Windows this is the Windows Credential Manager (DPAPI-protected). Isolated in its own
/// file so the MAUI dependency stays out of the settings logic, keeping the seam testable.
///
/// Nothing here logs a value: keys are opaque names ("mxt.mendix.apikey" etc.), and the
/// secret value never leaves this method except back to the caller that will hand it to the
/// vault or a live connection.
/// </summary>
public sealed class MauiSecretStore : ISecretStore
{
    public Task<string?> GetAsync(string key) => SecureStorage.Default.GetAsync(key);

    public Task SetAsync(string key, string value) => SecureStorage.Default.SetAsync(key, value);

    public bool Remove(string key) => SecureStorage.Default.Remove(key);
}
