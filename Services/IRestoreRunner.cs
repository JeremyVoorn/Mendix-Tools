using MendixTools.Core.Integrity;
using MendixTools.Core.Jobs;

namespace Mendix_Tools.Services;

/// <summary>
/// MT-17 — the testable seam over the ACTUAL local-Postgres restore operations. This is the
/// process/exec boundary: <see cref="RestoreJobs"/> orchestrates phases and log lines but never
/// shells out or opens a connection itself — it drives an <see cref="IRestoreRunner"/>. The
/// production adapter (<see cref="PostgresRestoreRunner"/>) uses Npgsql for the DROP/CREATE and
/// shells out to <c>pg_restore</c>/<c>psql</c> for the import; unit tests inject a FAKE runner so
/// NO real process is spawned and NO real database is ever touched.
///
/// SAFETY (non-negotiable, structural): the three destructive operations — terminate connections,
/// drop &amp; recreate, import — REQUIRE a <see cref="RestoreConfirmation"/> token. The only way to
/// obtain one is <see cref="RestoreConfirmation.ForConfirmed"/>, which returns <c>null</c> unless
/// the caller passed <c>confirmed: true</c>. There is therefore no code path that can invoke a
/// drop/recreate with an unconfirmed restore — the type system makes it unreachable, not just a
/// runtime <c>if</c>. Non-destructive steps (locate tool, verify server, verify restore) take no
/// token.
///
/// Every implementation MUST classify failures into <see cref="RestoreRunnerException"/> with an
/// actionable, secret-free message (never a raw stack trace, never the Postgres password) — the
/// same discipline as <c>NpgsqlPostgresProbe</c>. Each step reports progress/log through the
/// supplied <see cref="IJobContext"/>.
/// </summary>
public interface IRestoreRunner
{
    /// <summary>
    /// Locates the client tool the import will need (<c>pg_restore</c> for a custom-format dump,
    /// <c>psql</c> for plain SQL) on PATH or a configured location. Non-destructive — runs BEFORE
    /// any drop so an absent tool fails the job with
    /// "pg_restore not found — install PostgreSQL client tools or set the path in Settings" while
    /// the target database is still untouched. Throws <see cref="RestoreRunnerException"/>
    /// (<see cref="RestoreFailureKind.ToolNotFound"/>) when absent.
    /// </summary>
    Task EnsureClientToolAvailableAsync(RestorePlan plan, IJobContext ctx, CancellationToken ct = default);

    /// <summary>
    /// Verifies the LOCAL Postgres is reachable and the stored credentials are accepted, WITHOUT
    /// changing anything. Non-destructive — runs before any drop so an unreachable server / bad
    /// credentials fails the job while the target database is still untouched. Throws
    /// <see cref="RestoreRunnerException"/> (<see cref="RestoreFailureKind.Unreachable"/> /
    /// <see cref="RestoreFailureKind.AuthFailed"/>).
    /// </summary>
    Task VerifyServerAsync(RestorePlan plan, IJobContext ctx, CancellationToken ct = default);

    /// <summary>
    /// DESTRUCTIVE. Terminates open connections to the target database (<c>pg_terminate_backend</c>)
    /// so the subsequent drop cannot be blocked by an open session. Requires a
    /// <see cref="RestoreConfirmation"/>.
    /// </summary>
    Task TerminateConnectionsAsync(RestoreConfirmation confirmation, RestorePlan plan, IJobContext ctx, CancellationToken ct = default);

    /// <summary>
    /// DESTRUCTIVE and IRREVERSIBLE. <c>DROP DATABASE IF EXISTS</c> then <c>CREATE DATABASE</c> the
    /// target. Requires a <see cref="RestoreConfirmation"/> — this is the one-way door the MT-19
    /// Tier-2 guard protects at the UI, and that the confirmed-flag gate protects in the engine.
    /// </summary>
    Task DropAndRecreateDatabaseAsync(RestoreConfirmation confirmation, RestorePlan plan, IJobContext ctx, CancellationToken ct = default);

    /// <summary>
    /// Imports the archive into the freshly-created target database — <c>pg_restore</c> for a
    /// custom-format dump, <c>psql</c> for plain SQL, chosen from <see cref="RestorePlan.ImportMethod"/>.
    /// Streams the tool's stdout/stderr to the job log. Requires a <see cref="RestoreConfirmation"/>
    /// (it writes into the just-recreated database). Throws <see cref="RestoreRunnerException"/>
    /// (<see cref="RestoreFailureKind.ImportFailed"/>, naming the exit code) on a non-zero exit.
    /// </summary>
    Task ImportAsync(RestoreConfirmation confirmation, RestorePlan plan, IJobContext ctx, CancellationToken ct = default);

    /// <summary>
    /// Confirms the import produced a usable database (e.g. a non-empty schema). Non-destructive
    /// (read-only). Throws <see cref="RestoreRunnerException"/> on a failed verification.
    /// </summary>
    Task VerifyRestoreAsync(RestorePlan plan, IJobContext ctx, CancellationToken ct = default);
}

/// <summary>How the archive is imported into the target database.</summary>
public enum RestoreImportMethod
{
    /// <summary><c>pg_restore</c> — a PostgreSQL custom-format dump (<c>pg_dump -Fc</c>).</summary>
    PgRestore,

    /// <summary><c>psql</c> — a plain-SQL text dump piped in.</summary>
    Psql,
}

/// <summary>
/// The immutable inputs one restore run needs. The Postgres connection is deliberately NOT here:
/// the production runner reads host/port/user + the vault-only password from <c>AppSettingsService</c>
/// AT CALL TIME (so a Settings change takes effect without restart and no secret is captured in a
/// constructor), mirroring how the Mendix credential provider works for the cloud calls.
/// </summary>
/// <param name="ArchivePath">The downloaded, integrity-checked archive on disk (from MT-16).</param>
/// <param name="TargetDatabaseName">The local database to drop/recreate and import into.</param>
/// <param name="ArchiveFormat">Detected via <see cref="ArchiveIntegrity.DetectFileFormat"/>.</param>
/// <param name="ImportMethod">Chosen from the format via <see cref="RestorePlanner.DecideImportMethod"/>.</param>
public sealed record RestorePlan(
    string ArchivePath,
    string TargetDatabaseName,
    ArchiveFormat ArchiveFormat,
    RestoreImportMethod ImportMethod);

/// <summary>
/// The capability token that authorises a destructive restore step. It is unforgeable in the sense
/// that matters here: its constructor is private and the ONLY factory
/// (<see cref="ForConfirmed"/>) returns <c>null</c> unless <c>confirmed == true</c>. Because
/// <see cref="IRestoreRunner"/>'s destructive methods take a non-null token, "drop the database
/// when the restore was not confirmed" is not an <c>if</c> a caller can forget — it is a state the
/// type system cannot represent.
/// </summary>
public sealed class RestoreConfirmation
{
    private RestoreConfirmation(string targetDatabaseName) => TargetDatabaseName = targetDatabaseName;

    /// <summary>The database name the caller confirmed — cross-checked by the runner against the plan.</summary>
    public string TargetDatabaseName { get; }

    /// <summary>
    /// The single source of a confirmation token. Returns a token ONLY when <paramref name="confirmed"/>
    /// is true (and a target name is present); otherwise <c>null</c>, which forces the orchestrator to
    /// fail the restore before any destructive call.
    /// </summary>
    public static RestoreConfirmation? ForConfirmed(bool confirmed, string targetDatabaseName)
        => confirmed && !string.IsNullOrWhiteSpace(targetDatabaseName)
            ? new RestoreConfirmation(targetDatabaseName)
            : null;
}

/// <summary>
/// MT-17 — maps a detected <see cref="ArchiveFormat"/> to the import tool, reusing MT-16's
/// signature detection rather than re-sniffing bytes. Pure and side-effect-free so it is unit
/// tested directly.
/// </summary>
public static class RestorePlanner
{
    /// <summary>
    /// PostgreSQL custom-format dump → <c>pg_restore</c>; anything with no recognised magic (a plain
    /// SQL text dump) → <c>psql</c>. A still-compressed <c>.tar.gz</c>/<c>.zip</c> is rejected with an
    /// actionable message — database-only archives are expected to be a bare custom-format dump
    /// (D4 / MT-01 §A5); wrapped archives must be extracted first.
    /// </summary>
    public static RestoreImportMethod DecideImportMethod(ArchiveFormat format) => format switch
    {
        ArchiveFormat.PgDumpCustom => RestoreImportMethod.PgRestore,
        ArchiveFormat.Unknown => RestoreImportMethod.Psql,
        _ => throw new RestoreRunnerException(
            RestoreFailureKind.UnsupportedArchive,
            "The archive is compressed (.tar.gz/.zip). Download a database-only archive, or extract the .backup first."),
    };
}

/// <summary>Classified restore failure kinds, so the orchestrator can state "what happened" with
/// an actionable, secret-free message — the same discipline as <c>PostgresProbeError</c>.</summary>
public enum RestoreFailureKind
{
    /// <summary><c>pg_restore</c>/<c>psql</c> could not be located.</summary>
    ToolNotFound,

    /// <summary>The local Postgres server could not be reached.</summary>
    Unreachable,

    /// <summary>The server rejected the stored credentials.</summary>
    AuthFailed,

    /// <summary>The archive is a format the engine cannot import directly.</summary>
    UnsupportedArchive,

    /// <summary>The archive file is missing on disk.</summary>
    ArchiveMissing,

    /// <summary>The data directory / target is out of space.</summary>
    DiskFull,

    /// <summary>An I/O error touching the archive or data directory.</summary>
    IoError,

    /// <summary>The import tool exited non-zero.</summary>
    ImportFailed,

    /// <summary>Anything else the runner classified but could not attribute.</summary>
    Unknown,
}

/// <summary>
/// The one exception an <see cref="IRestoreRunner"/> raises. Its <see cref="Exception.Message"/> is
/// guaranteed by the implementation to be user-safe (actionable, no stack trace, no password), so
/// <see cref="RestoreJobs"/> can surface it verbatim as the job's failure message and failure toast.
/// </summary>
public sealed class RestoreRunnerException : Exception
{
    public RestoreRunnerException(RestoreFailureKind kind, string message) : base(message) => Kind = kind;

    public RestoreRunnerException(RestoreFailureKind kind, string message, Exception inner)
        : base(message, inner) => Kind = kind;

    /// <summary>The classified failure kind.</summary>
    public RestoreFailureKind Kind { get; }
}
