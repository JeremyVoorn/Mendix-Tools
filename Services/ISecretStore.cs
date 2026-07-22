namespace Mendix_Tools.Services;

/// <summary>
/// MT-11/MT-13 secrets seam. The ONLY place any secret (Mendix API key, local Postgres
/// password) is persisted is the OS secure vault — on Windows that is the Windows
/// Credential Manager, reached through MAUI <c>SecureStorage</c>. Secrets are NEVER written
/// to the metadata DB, MAUI Preferences, a file, a log, or committed config (project
/// security rule; see MT-11/MT-13 acceptance criteria).
///
/// This interface is a thin seam so the credential logic can be exercised without the MAUI
/// runtime: the production adapter is <see cref="MauiSecretStore"/> (wraps
/// <c>SecureStorage.Default</c>); a fake can stand in for tests.
/// </summary>
public interface ISecretStore
{
    /// <summary>Reads a stored secret, or <c>null</c> when absent.</summary>
    Task<string?> GetAsync(string key);

    /// <summary>Writes (or replaces) a secret in the OS vault.</summary>
    Task SetAsync(string key, string value);

    /// <summary>Deletes a secret from the OS vault. Returns true when a value was removed.</summary>
    bool Remove(string key);
}
