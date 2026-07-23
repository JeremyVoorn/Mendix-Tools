using System.Diagnostics;
using System.Net.Sockets;
using MendixTools.Core.Jobs;
using Npgsql;

namespace Mendix_Tools.Services;

/// <summary>
/// MT-17 production <see cref="IRestoreRunner"/> — the real local-Postgres restore engine. It uses
/// Npgsql for the connection-terminate / DROP / CREATE (server-side SQL) and shells out to the
/// PostgreSQL client tools (<c>pg_restore</c> for a custom-format dump, <c>psql</c> for plain SQL)
/// for the import, streaming their stdout/stderr into the job log.
///
/// ⛔ This class performs DESTRUCTIVE, IRREVERSIBLE operations against a real LOCAL database and is
/// NOT exercised by any automated test — the orchestration tests inject a fake
/// <see cref="IRestoreRunner"/>. It runs only when a user starts a confirmed restore at runtime.
///
/// Secrets: the Postgres password is read transiently from the OS vault via
/// <see cref="AppSettingsService"/>, passed to Npgsql via a connection-string builder and to the
/// child process via the <c>PGPASSWORD</c> environment variable (never on the command line, never
/// logged). Failures are classified into <see cref="RestoreRunnerException"/> with an actionable,
/// secret-free message — never a raw stack trace, never the password (the same discipline as
/// <see cref="NpgsqlPostgresProbe"/>).
///
/// The destructive methods assert a non-null <see cref="RestoreConfirmation"/> whose target matches
/// the plan — defence-in-depth behind the type-level gate in <see cref="RestoreJobs"/>.
/// </summary>
public sealed class PostgresRestoreRunner : IRestoreRunner
{
    private const int ConnectTimeoutSeconds = 5;

    // Client tools we shell out to, by import method.
    private static readonly string PgRestoreExe = OperatingSystem.IsWindows() ? "pg_restore.exe" : "pg_restore";
    private static readonly string PsqlExe = OperatingSystem.IsWindows() ? "psql.exe" : "psql";

    private readonly AppSettingsService _settings;

    public PostgresRestoreRunner(AppSettingsService settings)
        => _settings = settings ?? throw new ArgumentNullException(nameof(settings));

    public Task EnsureClientToolAvailableAsync(RestorePlan plan, IJobContext ctx, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var exe = plan.ImportMethod == RestoreImportMethod.PgRestore ? PgRestoreExe : PsqlExe;
        var located = LocateOnPath(exe);
        if (located is null)
        {
            var tool = plan.ImportMethod == RestoreImportMethod.PgRestore ? "pg_restore" : "psql";
            throw new RestoreRunnerException(
                RestoreFailureKind.ToolNotFound,
                $"{tool} not found — install PostgreSQL client tools or set the path in Settings.");
        }

        ctx.LogInfo($"Using client tool at {located}.");
        return Task.CompletedTask;
    }

    public async Task VerifyServerAsync(RestorePlan plan, IJobContext ctx, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var (info, _) = await ReadConnectionAsync().ConfigureAwait(false);

        // Connect to the maintenance database ("postgres") — the target may not exist yet.
        var builder = BuildConnectionString(info, database: "postgres");
        try
        {
            await using var connection = new NpgsqlConnection(builder.ConnectionString);
            await connection.OpenAsync(ct).ConfigureAwait(false);
            ctx.LogInfo($"Connected to local Postgres {connection.PostgreSqlVersion}.");
        }
        catch (Exception ex)
        {
            throw ClassifyConnection(ex);
        }
    }

    public async Task TerminateConnectionsAsync(RestoreConfirmation confirmation, RestorePlan plan, IJobContext ctx, CancellationToken ct = default)
    {
        var db = RequireConfirmed(confirmation, plan);
        var (info, _) = await ReadConnectionAsync().ConfigureAwait(false);
        var builder = BuildConnectionString(info, database: "postgres");

        try
        {
            await using var connection = new NpgsqlConnection(builder.ConnectionString);
            await connection.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText =
                "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @db AND pid <> pg_backend_pid();";
            cmd.Parameters.AddWithValue("db", db);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            ctx.LogInfo($"Terminated open connections to {db}.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not RestoreRunnerException)
        {
            throw ClassifyConnection(ex);
        }
    }

    public async Task DropAndRecreateDatabaseAsync(RestoreConfirmation confirmation, RestorePlan plan, IJobContext ctx, CancellationToken ct = default)
    {
        var db = RequireConfirmed(confirmation, plan);
        var (info, _) = await ReadConnectionAsync().ConfigureAwait(false);
        var builder = BuildConnectionString(info, database: "postgres");
        var quoted = QuoteIdentifier(db);

        try
        {
            await using var connection = new NpgsqlConnection(builder.ConnectionString);
            await connection.OpenAsync(ct).ConfigureAwait(false);

            await using (var drop = connection.CreateCommand())
            {
                drop.CommandText = $"DROP DATABASE IF EXISTS {quoted};";
                await drop.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            ctx.LogInfo($"Dropped {db} (if it existed).");

            await using (var create = connection.CreateCommand())
            {
                create.CommandText = $"CREATE DATABASE {quoted};";
                await create.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            ctx.LogInfo($"Created {db}.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not RestoreRunnerException)
        {
            throw ClassifyConnection(ex);
        }
    }

    public async Task ImportAsync(RestoreConfirmation confirmation, RestorePlan plan, IJobContext ctx, CancellationToken ct = default)
    {
        var db = RequireConfirmed(confirmation, plan);
        var (info, password) = await ReadConnectionAsync().ConfigureAwait(false);

        var exe = plan.ImportMethod == RestoreImportMethod.PgRestore ? PgRestoreExe : PsqlExe;
        var startInfo = new ProcessStartInfo
        {
            FileName = exe,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // Connection via flags; NEVER put the password on the command line — pass it via PGPASSWORD.
        startInfo.ArgumentList.Add("-h");
        startInfo.ArgumentList.Add(info.Host);
        startInfo.ArgumentList.Add("-p");
        startInfo.ArgumentList.Add(info.Port.ToString());
        startInfo.ArgumentList.Add("-U");
        startInfo.ArgumentList.Add(info.Username);
        startInfo.ArgumentList.Add("-d");
        startInfo.ArgumentList.Add(db);

        if (plan.ImportMethod == RestoreImportMethod.PgRestore)
        {
            startInfo.ArgumentList.Add("--no-owner");
            startInfo.ArgumentList.Add("--no-privileges");
            startInfo.ArgumentList.Add(plan.ArchivePath);
        }
        else
        {
            startInfo.ArgumentList.Add("-f");
            startInfo.ArgumentList.Add(plan.ArchivePath);
        }

        if (!string.IsNullOrEmpty(password))
        {
            startInfo.Environment["PGPASSWORD"] = password;
        }

        ctx.LogInfo($"Importing into {db} with {(plan.ImportMethod == RestoreImportMethod.PgRestore ? "pg_restore" : "psql")}.");

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => { if (e.Data is { Length: > 0 }) ctx.LogInfo(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is { Length: > 0 }) ctx.LogWarning(e.Data); };

        try
        {
            if (!process.Start())
            {
                throw new RestoreRunnerException(RestoreFailureKind.ImportFailed, "The import tool could not be started.");
            }
        }
        catch (Exception ex) when (ex is not RestoreRunnerException)
        {
            throw new RestoreRunnerException(
                RestoreFailureKind.ToolNotFound,
                "pg_restore not found — install PostgreSQL client tools or set the path in Settings.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        if (process.ExitCode != 0)
        {
            throw new RestoreRunnerException(
                RestoreFailureKind.ImportFailed,
                $"Import failed — {(plan.ImportMethod == RestoreImportMethod.PgRestore ? "pg_restore" : "psql")} exited with code {process.ExitCode}. Open the log for details.");
        }

        ctx.LogInfo($"Import into {db} completed.");
    }

    public async Task VerifyRestoreAsync(RestorePlan plan, IJobContext ctx, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var (info, _) = await ReadConnectionAsync().ConfigureAwait(false);
        var builder = BuildConnectionString(info, database: plan.TargetDatabaseName);

        try
        {
            await using var connection = new NpgsqlConnection(builder.ConnectionString);
            await connection.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT count(*) FROM information_schema.tables WHERE table_schema NOT IN ('pg_catalog','information_schema');";
            var count = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false));
            ctx.LogInfo($"Verified {plan.TargetDatabaseName}: {count} user table(s) present.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not RestoreRunnerException)
        {
            throw ClassifyConnection(ex);
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────

    private static string RequireConfirmed(RestoreConfirmation confirmation, RestorePlan plan)
    {
        ArgumentNullException.ThrowIfNull(confirmation);
        ArgumentNullException.ThrowIfNull(plan);
        if (!string.Equals(confirmation.TargetDatabaseName, plan.TargetDatabaseName, StringComparison.Ordinal))
        {
            throw new RestoreRunnerException(
                RestoreFailureKind.Unknown,
                "Restore aborted — the confirmed database name did not match the target.");
        }

        return plan.TargetDatabaseName;
    }

    private async Task<(PostgresConnectionInfo Info, string? Password)> ReadConnectionAsync()
    {
        var password = await _settings.GetDbPasswordAsync().ConfigureAwait(false);
        var info = new PostgresConnectionInfo(
            _settings.DbHost,
            _settings.DbPortNumber,
            _settings.DbUsername,
            password);
        return (info, password);
    }

    private static NpgsqlConnectionStringBuilder BuildConnectionString(PostgresConnectionInfo info, string database) => new()
    {
        Host = info.Host,
        Port = info.Port,
        Username = info.Username,
        Password = info.Password,
        Database = string.IsNullOrWhiteSpace(database) ? "postgres" : database,
        Timeout = ConnectTimeoutSeconds,
        CommandTimeout = 0, // an import can run long; do not time out the DROP/CREATE round-trips
        Pooling = false,
    };

    /// <summary>Quotes a Postgres identifier, doubling embedded quotes. The DB name still comes from
    /// the confirmed target; this prevents a stray quote from breaking the statement.</summary>
    private static string QuoteIdentifier(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    private static string? LocateOnPath(string exe)
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVar))
        {
            return null;
        }

        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(dir, exe);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
                // A malformed PATH entry must not abort the search.
            }
        }

        return null;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best-effort — the cancellation outcome stands regardless.
        }
    }

    /// <summary>Maps a connection/exec exception to a classified, secret-free
    /// <see cref="RestoreRunnerException"/> — the password never appears in the message.</summary>
    private static RestoreRunnerException ClassifyConnection(Exception ex)
    {
        switch (ex)
        {
            case PostgresException pg when pg.SqlState is "28P01" or "28000":
                return new RestoreRunnerException(
                    RestoreFailureKind.AuthFailed,
                    "Authentication failed — check the local Postgres username and password in Settings.");
            case PostgresException pg when pg.SqlState == "53100":
                return new RestoreRunnerException(
                    RestoreFailureKind.DiskFull,
                    "The restore failed — the disk is full. Free space and retry.");
            case PostgresException pg:
                return new RestoreRunnerException(
                    RestoreFailureKind.Unknown,
                    $"Local Postgres rejected the operation ({pg.SqlState}).");
            case NpgsqlException { InnerException: TimeoutException }:
                return new RestoreRunnerException(
                    RestoreFailureKind.Unreachable,
                    "Local Postgres did not respond — check that it is running.");
            case NpgsqlException { InnerException: SocketException }:
                return new RestoreRunnerException(
                    RestoreFailureKind.Unreachable,
                    "Local Postgres is unreachable — check the host, port, and that it is running.");
            case IOException:
                return new RestoreRunnerException(
                    RestoreFailureKind.IoError,
                    "The restore failed writing to disk — check the data directory and free space.");
            default:
                return new RestoreRunnerException(
                    RestoreFailureKind.Unknown,
                    "The restore could not connect to the local Postgres — check Settings and that it is running.");
        }
    }
}
