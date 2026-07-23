using Mendix_Tools.Services;
using MendixTools.Core.Integrity;
using MendixTools.Core.Jobs;
using MendixTools.Core.Metadata;
using MendixTools.Core.Models;
using Xunit;

namespace MendixTools.Core.Tests;

/// <summary>
/// MT-17 — orchestration tests for <see cref="RestoreJobs"/>: the clean-restore state machine runs
/// through a REAL <see cref="JobEngine"/> with a FAKE <see cref="IRestoreRunner"/>, a fake metadata
/// store and a fake notifier.
///
/// ⛔ SAFETY: NO real process is spawned and NO real database is EVER touched — every runner step is
/// an in-memory no-op (or a scripted throw). The most important test here proves the destructive
/// drop/recreate is UNREACHABLE unless the caller passes <c>confirmed: true</c>. No credential or
/// password appears anywhere.
/// </summary>
public sealed class RestoreJobsTests
{
    private const string TargetDb = "acme_local";

    // ── Happy path ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ConfirmedRestore_RunsPhasesInOrder_RecordsProvenance_Succeeds_FiresToast()
    {
        using var archive = TempArchive.PgDump();
        var runner = new FakeRunner();
        var (jobs, engine, store, notifier) = NewJobs(runner);

        var job = jobs.StartRestore(
            archive.Path, TargetDb, "snap-1", "Acme Insurance · Production", confirmed: true,
            provenance: new RestoreProvenanceInfo("Acme Insurance", "env-1", "10.24.16", DateTimeOffset.UnixEpoch));
        await engine.WaitAsync(job.Id);

        Assert.Equal(JobState.Succeeded, job.State);
        Assert.Equal(
            ["Preparing", "Terminating connections", $"Dropping & recreating {TargetDb}", $"Importing into {TargetDb}", "Verifying", "Done"],
            job.Phases);

        // Destructive steps each ran exactly once, and only after the non-destructive checks.
        Assert.Equal(1, runner.EnsureToolCalls);
        Assert.Equal(1, runner.VerifyServerCalls);
        Assert.Equal(1, runner.TerminateCalls);
        Assert.Equal(1, runner.DropCalls);
        Assert.Equal(1, runner.ImportCalls);
        Assert.Equal(1, runner.VerifyRestoreCalls);
        Assert.True(runner.ServerVerifiedBeforeDrop);

        // Provenance recorded on success.
        var record = Assert.Single(store.Restored);
        Assert.Equal(TargetDb, record.TargetDatabaseName);
        Assert.Equal("Acme Insurance", record.SourceApp);
        Assert.Equal("env-1", record.SourceEnvironmentId);
        Assert.Equal("snap-1", record.SnapshotId);
        Assert.Equal("10.24.16", record.MendixVersion);
        Assert.Equal(RestoreStatus.Succeeded, record.Status);

        // Terminal job-history row + success toast in the product voice.
        var row = Assert.Single(await store.GetJobsAsync());
        Assert.Equal("restore", row.JobType);
        Assert.Equal(JobResult.Succeeded, row.Result);
        var toast = Assert.Single(notifier.Successes);
        Assert.Contains($"Backup restored to {TargetDb}", toast.Title);
        Assert.Empty(notifier.Errors);
    }

    [Fact]
    public async Task ConfirmedRestore_NoProvenanceInfo_FallsBackToEnvLabel()
    {
        using var archive = TempArchive.PgDump();
        var runner = new FakeRunner();
        var (jobs, engine, store, _) = NewJobs(runner);

        var job = jobs.StartRestore(archive.Path, TargetDb, "snap-1", "Acme · Production", confirmed: true);
        await engine.WaitAsync(job.Id);

        Assert.Equal(JobState.Succeeded, job.State);
        var record = Assert.Single(store.Restored);
        Assert.Equal("Acme · Production", record.SourceApp);
        Assert.Equal("Acme · Production", record.SourceEnvironmentId);
    }

    [Fact]
    public async Task ConfirmedRestore_KeepFileFalse_DeletesArchiveAfterSuccess()
    {
        using var archive = TempArchive.PgDump();
        var runner = new FakeRunner();
        var (jobs, engine, _, _) = NewJobs(runner);

        var job = jobs.StartRestore(archive.Path, TargetDb, "snap-1", "Acme · Production", confirmed: true, keepFile: false);
        await engine.WaitAsync(job.Id);

        Assert.Equal(JobState.Succeeded, job.State);
        Assert.False(File.Exists(archive.Path)); // consumed + removed per the keep-file preference
    }

    // ── SAFETY — the most important test ─────────────────────────────────────────────────────

    [Fact]
    public async Task NotConfirmed_FailsImmediately_NoDestructiveCallsAtAll()
    {
        using var archive = TempArchive.PgDump();
        var runner = new FakeRunner();
        var (jobs, engine, store, notifier) = NewJobs(runner);

        var job = jobs.StartRestore(archive.Path, TargetDb, "snap-1", "Acme · Production", confirmed: false);
        await engine.WaitAsync(job.Id);

        Assert.Equal(JobState.Failed, job.State);
        Assert.Contains("not confirmed", job.Message, StringComparison.OrdinalIgnoreCase);

        // NOTHING touched the database — not even the non-destructive probes ran.
        Assert.Equal(0, runner.EnsureToolCalls);
        Assert.Equal(0, runner.VerifyServerCalls);
        Assert.Equal(0, runner.TerminateCalls);
        Assert.Equal(0, runner.DropCalls);
        Assert.Equal(0, runner.ImportCalls);

        Assert.Empty(store.Restored);          // no provenance
        Assert.Single(notifier.Errors);        // a failure toast, but no data-destroying call
        Assert.Empty(notifier.Successes);
    }

    // ── Runner failure mid-import ────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunnerFailsMidImport_JobFailed_WithRunnerMessage_NoProvenance()
    {
        using var archive = TempArchive.PgDump();
        var runner = new FakeRunner
        {
            OnImport = () => throw new RestoreRunnerException(
                RestoreFailureKind.ImportFailed, "Import failed — pg_restore exited with code 1. Open the log for details."),
        };
        var (jobs, engine, store, notifier) = NewJobs(runner);

        var job = jobs.StartRestore(archive.Path, TargetDb, "snap-1", "Acme · Production", confirmed: true);
        await engine.WaitAsync(job.Id);

        Assert.Equal(JobState.Failed, job.State);
        Assert.Equal("Import failed — pg_restore exited with code 1. Open the log for details.", job.Message);
        Assert.Equal(1, runner.DropCalls);     // drop happened, then import failed
        Assert.Empty(store.Restored);          // no provenance recorded on failure
        // The half-restored state is documented honestly in the log (no silent half-restore).
        Assert.Contains(job.Log, l => l.Level == JobLogLevel.Error && l.Message.Contains("NOT usable"));
        var error = Assert.Single(notifier.Errors);
        Assert.Equal("Restore failed", error.Title);
    }

    [Fact]
    public async Task ToolNotFound_FailsBeforeAnyDestructiveStep()
    {
        using var archive = TempArchive.PgDump();
        var runner = new FakeRunner
        {
            OnEnsureTool = () => throw new RestoreRunnerException(
                RestoreFailureKind.ToolNotFound,
                "pg_restore not found — install PostgreSQL client tools or set the path in Settings."),
        };
        var (jobs, engine, store, _) = NewJobs(runner);

        var job = jobs.StartRestore(archive.Path, TargetDb, "snap-1", "Acme · Production", confirmed: true);
        await engine.WaitAsync(job.Id);

        Assert.Equal(JobState.Failed, job.State);
        Assert.Contains("pg_restore not found", job.Message);
        Assert.Equal(0, runner.TerminateCalls); // failed before the point of no return
        Assert.Equal(0, runner.DropCalls);
        Assert.Empty(store.Restored);
    }

    [Fact]
    public async Task ArchiveMissing_FailsBeforeAnyDestructiveStep()
    {
        var runner = new FakeRunner();
        var (jobs, engine, _, _) = NewJobs(runner);

        var job = jobs.StartRestore(
            Path.Combine(Path.GetTempPath(), $"mxt-missing-{Guid.NewGuid():N}.backup"),
            TargetDb, "snap-1", "Acme · Production", confirmed: true);
        await engine.WaitAsync(job.Id);

        Assert.Equal(JobState.Failed, job.State);
        Assert.Contains("could not be found", job.Message);
        Assert.Equal(0, runner.DropCalls);
    }

    // ── Cancellation ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CancelledMidImport_EndsCancelled_DocumentsPartialState_NoToast()
    {
        using var archive = TempArchive.PgDump();
        var reachedImport = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new FakeRunner
        {
            OnImportAsync = async ct =>
            {
                reachedImport.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            },
        };
        var (jobs, engine, store, notifier) = NewJobs(runner);

        var job = jobs.StartRestore(archive.Path, TargetDb, "snap-1", "Acme · Production", confirmed: true);

        await reachedImport.Task;              // import has begun (drop already ran)
        Assert.True(jobs.Cancel(job.Id));
        await engine.WaitAsync(job.Id);

        Assert.Equal(JobState.Cancelled, job.State);
        Assert.Equal(1, runner.DropCalls);     // the drop already happened before cancellation
        Assert.Empty(store.Restored);          // no provenance on cancel
        Assert.Contains(job.Log, l => l.Level == JobLogLevel.Error && l.Message.Contains("NOT usable"));
        Assert.Empty(notifier.Successes);
        Assert.Empty(notifier.Errors);         // cancellation is user-initiated → no toast
    }

    // ── Local Postgres unreachable / bad credentials ─────────────────────────────────────────

    [Fact]
    public async Task ServerUnreachable_ActionableMessage_NoPassword_NoDestructiveCalls()
    {
        const string secret = "sup3r-s3cret-pw";
        using var archive = TempArchive.PgDump();
        var runner = new FakeRunner
        {
            OnVerifyServer = () => throw new RestoreRunnerException(
                RestoreFailureKind.Unreachable,
                "Local Postgres is unreachable — check the host, port, and that it is running."),
        };
        var (jobs, engine, store, _) = NewJobs(runner);

        var job = jobs.StartRestore(archive.Path, TargetDb, "snap-1", "Acme · Production", confirmed: true);
        await engine.WaitAsync(job.Id);

        Assert.Equal(JobState.Failed, job.State);
        Assert.Equal("Local Postgres is unreachable — check the host, port, and that it is running.", job.Message);
        Assert.DoesNotContain(secret, job.Message ?? string.Empty);
        Assert.Equal(0, runner.TerminateCalls); // failed before any destructive step
        Assert.Equal(0, runner.DropCalls);
        Assert.Empty(store.Restored);
    }

    [Fact]
    public async Task BadCredentials_ActionableMessage_NoDestructiveCalls()
    {
        using var archive = TempArchive.PgDump();
        var runner = new FakeRunner
        {
            OnVerifyServer = () => throw new RestoreRunnerException(
                RestoreFailureKind.AuthFailed,
                "Authentication failed — check the local Postgres username and password in Settings."),
        };
        var (jobs, engine, _, _) = NewJobs(runner);

        var job = jobs.StartRestore(archive.Path, TargetDb, "snap-1", "Acme · Production", confirmed: true);
        await engine.WaitAsync(job.Id);

        Assert.Equal(JobState.Failed, job.State);
        Assert.Contains("Authentication failed", job.Message);
        Assert.Equal(0, runner.DropCalls);
    }

    [Fact]
    public async Task UnexpectedRunnerException_IsNeutralised_NoRawDetailsLeak()
    {
        using var archive = TempArchive.PgDump();
        var runner = new FakeRunner
        {
            // A raw driver exception that could echo a connection string / password.
            OnImport = () => throw new InvalidOperationException("Host=db;Password=sup3r-s3cret-pw;boom"),
        };
        var (jobs, engine, _, _) = NewJobs(runner);

        var job = jobs.StartRestore(archive.Path, TargetDb, "snap-1", "Acme · Production", confirmed: true);
        await engine.WaitAsync(job.Id);

        Assert.Equal(JobState.Failed, job.State);
        Assert.Equal("The restore failed unexpectedly. Open the log for details.", job.Message);
        Assert.DoesNotContain("sup3r-s3cret-pw", job.Message);
    }

    // ── Format detection (reuse ArchiveIntegrity) ────────────────────────────────────────────

    [Fact]
    public void FormatDetection_PicksPgRestoreForCustomDump()
    {
        using var archive = TempArchive.PgDump();
        var format = ArchiveIntegrity.DetectFileFormat(archive.Path);
        Assert.Equal(ArchiveFormat.PgDumpCustom, format);
        Assert.Equal(RestoreImportMethod.PgRestore, RestorePlanner.DecideImportMethod(format));
    }

    [Fact]
    public void FormatDetection_PicksPsqlForPlainSql()
    {
        using var archive = TempArchive.PlainSql();
        var format = ArchiveIntegrity.DetectFileFormat(archive.Path);
        Assert.Equal(ArchiveFormat.Unknown, format); // plain SQL carries no magic bytes
        Assert.Equal(RestoreImportMethod.Psql, RestorePlanner.DecideImportMethod(format));
    }

    [Fact]
    public void FormatDetection_RejectsCompressedArchive()
    {
        var ex = Assert.Throws<RestoreRunnerException>(() => RestorePlanner.DecideImportMethod(ArchiveFormat.Gzip));
        Assert.Equal(RestoreFailureKind.UnsupportedArchive, ex.Kind);
    }

    // ── Helpers, fakes & fixtures ────────────────────────────────────────────────────────────

    private static (RestoreJobs jobs, JobEngine engine, FakeStore store, FakeNotifier notifier) NewJobs(FakeRunner runner)
    {
        var store = new FakeStore();
        var engine = new JobEngine(store);
        var notifier = new FakeNotifier();
        var jobs = new RestoreJobs(engine, runner, notifier, store);
        return (jobs, engine, store, notifier);
    }

    /// <summary>A temp file with a recognisable header, cleaned up on dispose.</summary>
    private sealed class TempArchive : IDisposable
    {
        public string Path { get; }

        private TempArchive(byte[] content)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"mxt-restore-{Guid.NewGuid():N}.backup");
            File.WriteAllBytes(Path, content);
        }

        // "PGDMP" magic → custom-format dump → pg_restore.
        public static TempArchive PgDump()
        {
            var bytes = new byte[64];
            "PGDMP"u8.CopyTo(bytes);
            return new TempArchive(bytes);
        }

        // Plain SQL text (no magic) → psql.
        public static TempArchive PlainSql()
            => new(System.Text.Encoding.ASCII.GetBytes("--\nSET statement_timeout = 0;\nCREATE TABLE t (id int);\n"));

        public void Dispose()
        {
            try { if (File.Exists(Path)) File.Delete(Path); } catch { /* best-effort */ }
        }
    }

    private sealed class FakeNotifier : IUserNotifier
    {
        public List<(string Title, string? Message)> Successes { get; } = [];
        public List<(string Title, string? Message)> Errors { get; } = [];
        public void Success(string title, string? message = null) => Successes.Add((title, message));
        public void Error(string title, string? message = null) => Errors.Add((title, message));
    }

    /// <summary>
    /// The only seam the tests touch — a fully in-memory <see cref="IRestoreRunner"/>. No process,
    /// no Npgsql, no database. Each step counts its calls and can be scripted to throw.
    /// </summary>
    private sealed class FakeRunner : IRestoreRunner
    {
        public int EnsureToolCalls { get; private set; }
        public int VerifyServerCalls { get; private set; }
        public int TerminateCalls { get; private set; }
        public int DropCalls { get; private set; }
        public int ImportCalls { get; private set; }
        public int VerifyRestoreCalls { get; private set; }
        public bool ServerVerifiedBeforeDrop { get; private set; }

        public Action? OnEnsureTool { get; set; }
        public Action? OnVerifyServer { get; set; }
        public Action? OnImport { get; set; }
        public Func<CancellationToken, Task>? OnImportAsync { get; set; }

        public Task EnsureClientToolAvailableAsync(RestorePlan plan, IJobContext ctx, CancellationToken ct = default)
        {
            EnsureToolCalls++;
            OnEnsureTool?.Invoke();
            return Task.CompletedTask;
        }

        public Task VerifyServerAsync(RestorePlan plan, IJobContext ctx, CancellationToken ct = default)
        {
            VerifyServerCalls++;
            OnVerifyServer?.Invoke();
            return Task.CompletedTask;
        }

        public Task TerminateConnectionsAsync(RestoreConfirmation confirmation, RestorePlan plan, IJobContext ctx, CancellationToken ct = default)
        {
            Assert.NotNull(confirmation); // the token is required — structural gate
            TerminateCalls++;
            return Task.CompletedTask;
        }

        public Task DropAndRecreateDatabaseAsync(RestoreConfirmation confirmation, RestorePlan plan, IJobContext ctx, CancellationToken ct = default)
        {
            Assert.NotNull(confirmation);
            ServerVerifiedBeforeDrop = VerifyServerCalls > 0;
            DropCalls++;
            return Task.CompletedTask;
        }

        public async Task ImportAsync(RestoreConfirmation confirmation, RestorePlan plan, IJobContext ctx, CancellationToken ct = default)
        {
            Assert.NotNull(confirmation);
            ImportCalls++;
            if (OnImportAsync is not null)
            {
                await OnImportAsync(ct).ConfigureAwait(false);
            }

            OnImport?.Invoke();
        }

        public Task VerifyRestoreAsync(RestorePlan plan, IJobContext ctx, CancellationToken ct = default)
        {
            VerifyRestoreCalls++;
            return Task.CompletedTask;
        }
    }

    /// <summary>In-memory store supporting the provenance + job-history operations these tests use.</summary>
    private sealed class FakeStore : IMetadataStore
    {
        private readonly List<JobHistoryEntry> _jobs = [];
        public List<RestoredDatabase> Restored { get; } = [];
        private long _nextJobId;
        private long _nextRestoredId;

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<long> AddRestoredDatabaseAsync(RestoredDatabase record, CancellationToken cancellationToken = default)
        {
            lock (Restored)
            {
                record.Id = ++_nextRestoredId;
                Restored.Add(record);
                return Task.FromResult(record.Id);
            }
        }

        public Task<long> AddJobAsync(JobHistoryEntry entry, CancellationToken cancellationToken = default)
        {
            lock (_jobs)
            {
                entry.Id = ++_nextJobId;
                _jobs.Add(entry);
                return Task.FromResult(entry.Id);
            }
        }

        public Task<IReadOnlyList<JobHistoryEntry>> GetJobsAsync(CancellationToken cancellationToken = default)
        {
            lock (_jobs)
            {
                return Task.FromResult<IReadOnlyList<JobHistoryEntry>>(_jobs.ToArray());
            }
        }

        public Task<RestoredDatabase?> GetRestoredDatabaseAsync(long id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<RestoredDatabase>> GetRestoredDatabasesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateRestoredDatabaseAsync(RestoredDatabase record, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CacheEnvironmentStateAsync(CachedEnvironmentState state, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CachedEnvironmentState?> GetEnvironmentStateAsync(string environmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CacheLastBackupAsync(string environmentId, DateTimeOffset? lastBackupAt, DateTimeOffset fetchedAt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CachedLastBackup?> GetLastBackupAsync(string environmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RecordSnapshotSizeAsync(string snapshotId, long sizeBytes, DateTimeOffset recordedAt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<long?> GetSnapshotSizeAsync(string snapshotId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
