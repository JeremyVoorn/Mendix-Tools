namespace Mendix_Tools.Services;

/// <summary>
/// MT-11 "Test connection" seam. The Database tab calls <see cref="TestAsync"/> to attempt a
/// real connection to the user's LOCAL Postgres and report server version + latency. It is
/// always user-initiated at runtime; nothing in the app fires it automatically.
///
/// Behind a seam so the tab logic is testable with a fake, and so a non-Npgsql probe could
/// be substituted later; the production adapter is <see cref="NpgsqlPostgresProbe"/>.
/// </summary>
public interface IPostgresProbe
{
    /// <summary>
    /// Attempts one connection and returns a structured result. Implementations must NEVER
    /// throw for a connection/auth failure — they map it to <see cref="PostgresProbeResult"/>
    /// with an actionable message and never leak the password or a raw stack trace.
    /// </summary>
    Task<PostgresProbeResult> TestAsync(PostgresConnectionInfo info, CancellationToken ct = default);
}

/// <summary>
/// The connection values the probe needs. The password is passed transiently (read from the
/// OS vault by the caller) and is never persisted, logged, or echoed by the probe.
/// </summary>
public sealed record PostgresConnectionInfo(
    string Host,
    int Port,
    string Username,
    string? Password,
    string Database = "postgres");

/// <summary>Structured, UI-ready outcome of a connection test. No exceptions escape the probe.</summary>
public sealed record PostgresProbeResult(
    bool Success,
    long ElapsedMs,
    string? ServerVersion = null,
    PostgresProbeError Error = PostgresProbeError.None,
    string? Message = null)
{
    public static PostgresProbeResult Ok(string serverVersion, long elapsedMs)
        => new(true, elapsedMs, serverVersion);

    public static PostgresProbeResult Fail(PostgresProbeError error, string message, long elapsedMs)
        => new(false, elapsedMs, Error: error, Message: message);
}

/// <summary>Classified failure kinds so the UI can state "what happened" without a stack trace.</summary>
public enum PostgresProbeError
{
    None,
    Unreachable,
    AuthFailed,
    Timeout,
    Unknown,
}
