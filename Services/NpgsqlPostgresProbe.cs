using System.Diagnostics;
using System.Net.Sockets;
using Npgsql;

namespace Mendix_Tools.Services;

/// <summary>
/// MT-11 production <see cref="IPostgresProbe"/> — a real Npgsql connection attempt against
/// the user's local Postgres. Opens a connection with a short timeout, reads the server
/// version, and times the round-trip so the tab can render "PostgreSQL 16.2 — 12 ms".
///
/// Every failure path is mapped to a classified <see cref="PostgresProbeResult"/> with an
/// actionable message — a raw exception or stack trace is never surfaced, and the password
/// never appears in any returned string (MT-11 AC: "never a raw stack trace"; "the password
/// is never rendered or logged").
/// </summary>
public sealed class NpgsqlPostgresProbe : IPostgresProbe
{
    // Short, so an unreachable host fails fast rather than hanging the user's Test click.
    private const int ConnectTimeoutSeconds = 5;

    public async Task<PostgresProbeResult> TestAsync(PostgresConnectionInfo info, CancellationToken ct = default)
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = info.Host,
            Port = info.Port,
            Username = info.Username,
            Password = info.Password,
            Database = string.IsNullOrWhiteSpace(info.Database) ? "postgres" : info.Database,
            Timeout = ConnectTimeoutSeconds,
            CommandTimeout = ConnectTimeoutSeconds,
            // Keep the probe cheap and self-contained: no pooling side effects to clean up.
            Pooling = false,
        };

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await using var connection = new NpgsqlConnection(builder.ConnectionString);
            await connection.OpenAsync(ct).ConfigureAwait(false);

            // PostgreSqlVersion is parsed from the handshake; ServerVersion is the raw string.
            var version = connection.PostgreSqlVersion.ToString();
            stopwatch.Stop();
            return PostgresProbeResult.Ok($"PostgreSQL {version}", stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            stopwatch.Stop();
            return PostgresProbeResult.Fail(
                PostgresProbeError.Timeout,
                "Test cancelled.",
                stopwatch.ElapsedMilliseconds);
        }
        catch (PostgresException pg)
        {
            // Server answered but rejected us. 28P01 = invalid password, 28000 = invalid auth.
            stopwatch.Stop();
            var message = pg.SqlState is "28P01" or "28000"
                ? "Authentication failed — check the username and password."
                : $"Server rejected the connection ({pg.SqlState}).";
            return PostgresProbeResult.Fail(PostgresProbeError.AuthFailed, message, stopwatch.ElapsedMilliseconds);
        }
        catch (NpgsqlException ex)
        {
            stopwatch.Stop();
            return ClassifyNpgsql(ex, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception)
        {
            // Never surface the raw exception text (may echo connection details). Generic,
            // actionable message only.
            stopwatch.Stop();
            return PostgresProbeResult.Fail(
                PostgresProbeError.Unknown,
                "Could not connect — check the host, port, and that Postgres is running.",
                stopwatch.ElapsedMilliseconds);
        }
    }

    private static PostgresProbeResult ClassifyNpgsql(NpgsqlException ex, long elapsedMs)
    {
        // A timeout surfaces as NpgsqlException with a TimeoutException inner.
        if (ex.InnerException is TimeoutException)
        {
            return PostgresProbeResult.Fail(
                PostgresProbeError.Timeout,
                "Connection timed out — the host did not respond.",
                elapsedMs);
        }

        // An unreachable host/port surfaces as a SocketException inner.
        if (ex.InnerException is SocketException socket)
        {
            var message = socket.SocketErrorCode switch
            {
                SocketError.ConnectionRefused =>
                    "Host unreachable — nothing is listening on that host and port.",
                SocketError.HostNotFound or SocketError.HostUnreachable or SocketError.NetworkUnreachable =>
                    "Host unreachable — the host name could not be resolved.",
                SocketError.TimedOut =>
                    "Connection timed out — the host did not respond.",
                _ => "Host unreachable — check the host and port.",
            };
            var kind = socket.SocketErrorCode == SocketError.TimedOut
                ? PostgresProbeError.Timeout
                : PostgresProbeError.Unreachable;
            return PostgresProbeResult.Fail(kind, message, elapsedMs);
        }

        return PostgresProbeResult.Fail(
            PostgresProbeError.Unreachable,
            "Could not connect — check the host, port, and that Postgres is running.",
            elapsedMs);
    }
}
