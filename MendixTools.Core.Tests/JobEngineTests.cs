using MendixTools.Core.Jobs;
using MendixTools.Core.Metadata;
using MendixTools.Core.Models;
using Xunit;

namespace MendixTools.Core.Tests;

/// <summary>
/// MT-09 — the first real core unit tests: state-transition correctness (happy / failure /
/// cancellation paths), event ordering, cooperative cancellation against a fake long-running
/// job, and terminal persistence to a fake <see cref="IMetadataStore"/>. No real external
/// system is touched — the work is always a fake delegate and the store is in-memory.
/// </summary>
public sealed class JobEngineTests
{
    // ── State transitions ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task HappyPath_RunsThroughRunningToSucceeded()
    {
        var store = new FakeMetadataStore();
        var engine = new JobEngine(store);

        var job = engine.Start("download", async (ctx, ct) =>
        {
            ctx.BeginPhase("Downloading backup");
            ctx.ReportProgress(50);
            ctx.LogInfo("Halfway.");
            ctx.ReportProgress(100);
            await Task.CompletedTask;
        });

        await engine.WaitAsync(job.Id);

        Assert.Equal(JobState.Succeeded, job.State);
        Assert.True(job.IsTerminal);
        Assert.Null(job.Message);
        Assert.Equal(new[] { JobState.Queued, JobState.Running, JobState.Succeeded }, job.StateHistory);
        Assert.NotNull(job.StartedAt);
        Assert.NotNull(job.FinishedAt);
        Assert.Equal(new[] { "Downloading backup" }, job.Phases);
        Assert.Equal(100d, job.Progress);
    }

    [Fact]
    public async Task FailurePath_WorkThrows_EndsFailedWithMessageAndRetainsLog()
    {
        var store = new FakeMetadataStore();
        var engine = new JobEngine(store);

        var job = engine.Start("restore", (ctx, ct) =>
        {
            ctx.LogInfo("Starting restore.");
            throw new InvalidOperationException("pg_restore exited with code 1.");
        });

        await engine.WaitAsync(job.Id);

        Assert.Equal(JobState.Failed, job.State);
        Assert.Equal("pg_restore exited with code 1.", job.Message);
        Assert.Equal(new[] { JobState.Queued, JobState.Running, JobState.Failed }, job.StateHistory);
        // The log is retained after failure so it stays inspectable (MT-09 AC).
        Assert.Contains(job.Log, l => l.Message == "Starting restore.");
    }

    [Fact]
    public async Task FailingJob_NeverSurfacesAsUnobservedTaskException()
    {
        var store = new FakeMetadataStore();
        var engine = new JobEngine(store);

        var job = engine.Start("deploy", (ctx, ct) => throw new Exception("boom"));

        // WaitAsync must complete without throwing — the fault is captured as Failed state.
        await engine.WaitAsync(job.Id);
        Assert.Equal(JobState.Failed, job.State);
    }

    // ── Cancellation ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cancellation_FakeLongRunningJob_CancelsPromptlyAndEndsCancelled()
    {
        var store = new FakeMetadataStore();
        var engine = new JobEngine(store);
        var reachedLoop = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var job = engine.Start("download", async (ctx, ct) =>
        {
            ctx.BeginPhase("Downloading backup");
            reachedLoop.TrySetResult();
            // A fake long-running job: loops until cancel is observed at a safe point.
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(10, ct);
            }
        });

        await reachedLoop.Task;          // ensure the job is actually running its loop
        Assert.True(engine.Cancel(job.Id));

        await engine.WaitAsync(job.Id);

        Assert.Equal(JobState.Cancelled, job.State);
        Assert.Equal(new[] { JobState.Queued, JobState.Running, JobState.Cancelled }, job.StateHistory);
        Assert.NotNull(job.FinishedAt);
    }

    [Fact]
    public async Task Cancel_OnFinishedJob_ReturnsFalse()
    {
        var store = new FakeMetadataStore();
        var engine = new JobEngine(store);

        var job = engine.Start("download", (_, _) => Task.CompletedTask);
        await engine.WaitAsync(job.Id);

        Assert.False(engine.Cancel(job.Id));
    }

    [Fact]
    public void Cancel_UnknownJob_ReturnsFalse()
    {
        var engine = new JobEngine(new FakeMetadataStore());
        Assert.False(engine.Cancel(Guid.NewGuid()));
    }

    // ── Event ordering ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task EventOrdering_ProgressLineAndStateEvents_FireInCallOrder()
    {
        var store = new FakeMetadataStore();
        var engine = new JobEngine(store);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = new List<string>();

        // Work blocks on the gate as its FIRST action, so the job is already Running and our
        // subscription is attached before any progress/line/terminal event fires.
        var job = engine.Start("restore", async (ctx, ct) =>
        {
            await gate.Task;
            ctx.BeginPhase("Importing into acme_local");   // ProgressChanged (phase reset)
            ctx.ReportProgress(25);                          // ProgressChanged
            ctx.LogInfo("25%");                              // LineAppended
            ctx.ReportProgress(100);                         // ProgressChanged
        });

        job.ProgressChanged += (_, _) => events.Add("progress");
        job.LineAppended += (_, _) => events.Add("line");
        job.StateChanged += (_, e) => events.Add($"state:{e.Current}");

        gate.SetResult();
        await engine.WaitAsync(job.Id);

        Assert.Equal(
            new[] { "progress", "progress", "line", "progress", "state:Succeeded" },
            events);
    }

    [Fact]
    public async Task ProgressReports_AfterTerminal_AreIgnored()
    {
        var store = new FakeMetadataStore();
        var engine = new JobEngine(store);
        IJobContext? captured = null;

        var job = engine.Start("download", async (ctx, ct) =>
        {
            captured = ctx;
            ctx.ReportProgress(40);
            await Task.CompletedTask;
        });

        await engine.WaitAsync(job.Id);
        Assert.Equal(JobState.Succeeded, job.State);

        // A stray late report (only valid while Running) must be a no-op, not a crash.
        var progressCount = 0;
        job.ProgressChanged += (_, _) => progressCount++;
        captured!.ReportProgress(99);

        Assert.Equal(0, progressCount);
        Assert.Equal(40d, job.Progress);
    }

    // ── Lifecycle / querying ────────────────────────────────────────────────────────

    [Fact]
    public async Task FinishedJobs_RemainQueryable()
    {
        var store = new FakeMetadataStore();
        var engine = new JobEngine(store);

        var a = engine.Start("download", (_, _) => Task.CompletedTask);
        var b = engine.Start("restore", (_, _) => throw new Exception("nope"));
        await Task.WhenAll(engine.WaitAsync(a.Id), engine.WaitAsync(b.Id));

        Assert.Same(a, engine.Get(a.Id));
        Assert.Same(b, engine.Get(b.Id));
        Assert.Equal(2, engine.Jobs.Count);
        Assert.Equal(new[] { a.Id, b.Id }, engine.Jobs.Select(j => j.Id));   // start order preserved
    }

    [Fact]
    public void Start_RaisesJobStarted()
    {
        var engine = new JobEngine(new FakeMetadataStore());
        Job? started = null;
        engine.JobStarted += (_, j) => started = j;

        var job = engine.Start("download", (_, _) => Task.CompletedTask);

        Assert.Same(job, started);
    }

    [Fact]
    public void Start_EmptyKind_Throws()
    {
        var engine = new JobEngine(new FakeMetadataStore());
        Assert.Throws<ArgumentException>(() => engine.Start(" ", (_, _) => Task.CompletedTask));
    }

    // ── Persistence to job_history (fake store) ───────────────────────────────────────

    [Theory]
    [InlineData(JobOutcome.Succeed, JobResult.Succeeded)]
    [InlineData(JobOutcome.Fail, JobResult.Failed)]
    [InlineData(JobOutcome.Cancel, JobResult.Cancelled)]
    public async Task Persistence_WritesTerminalHistoryRow_WithStartAndFinishAndPhases(
        JobOutcome outcome, JobResult expected)
    {
        var store = new FakeMetadataStore();
        var engine = new JobEngine(store);

        var job = engine.Start("restore", async (ctx, ct) =>
        {
            ctx.BeginPhase("Dropping & recreating schema");
            ctx.BeginPhase("Importing into acme_local");
            switch (outcome)
            {
                case JobOutcome.Fail:
                    throw new InvalidOperationException("disk full");
                case JobOutcome.Cancel:
                    // Signal ourselves then observe it at a safe point.
                    engine.Cancel(ctx.JobId);
                    ct.ThrowIfCancellationRequested();
                    break;
            }

            await Task.CompletedTask;
        });

        await engine.WaitAsync(job.Id);

        // Nothing is persisted until the terminal transition; exactly one row on finish,
        // capturing both the start and finish of the job's lifecycle (store is insert-only).
        var rows = await store.GetJobsAsync();
        var row = Assert.Single(rows);
        Assert.Equal("restore", row.JobType);
        Assert.Equal(expected, row.Result);
        Assert.NotNull(row.StartedAt);
        Assert.NotNull(row.FinishedAt);
        Assert.Equal(new[] { "Dropping & recreating schema", "Importing into acme_local" }, row.Phases);
    }

    [Fact]
    public async Task Persistence_NoHistoryRow_WhileJobStillRunning()
    {
        var store = new FakeMetadataStore();
        var engine = new JobEngine(store);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var job = engine.Start("download", async (_, _) => await gate.Task);

        // Running, not terminal → nothing written yet.
        Assert.Equal(0, store.AddedCount);

        gate.SetResult();
        await engine.WaitAsync(job.Id);
        Assert.Equal(1, store.AddedCount);
    }

    [Fact]
    public async Task Persistence_WritesLogFileAndRecordsPath_WhenLogDirectoryConfigured()
    {
        var store = new FakeMetadataStore();
        var logDir = Path.Combine(Path.GetTempPath(), $"mxt-joblog-{Guid.NewGuid():N}");
        var engine = new JobEngine(store, logDirectory: logDir);

        try
        {
            var job = engine.Start("download", async (ctx, ct) =>
            {
                ctx.LogInfo("line one");
                ctx.LogError("line two");
                await Task.CompletedTask;
            });

            await engine.WaitAsync(job.Id);

            var row = Assert.Single(await store.GetJobsAsync());
            Assert.NotNull(row.LogPath);
            Assert.True(File.Exists(row.LogPath));
            var contents = await File.ReadAllTextAsync(row.LogPath!);
            Assert.Contains("line one", contents);
            Assert.Contains("line two", contents);
        }
        finally
        {
            if (Directory.Exists(logDir))
            {
                try { Directory.Delete(logDir, recursive: true); } catch (IOException) { /* temp */ }
            }
        }
    }

    [Fact]
    public async Task Persistence_StoreFailure_DoesNotChangeOutcomeOrCrash()
    {
        var store = new FakeMetadataStore { ThrowOnAdd = true };
        var engine = new JobEngine(store);

        var job = engine.Start("download", (_, _) => Task.CompletedTask);
        await engine.WaitAsync(job.Id);

        // Persistence blew up, but the job still completed cleanly in memory.
        Assert.Equal(JobState.Succeeded, job.State);
    }

    public enum JobOutcome { Succeed, Fail, Cancel }

    /// <summary>
    /// In-memory <see cref="IMetadataStore"/> for the job-engine tests. Only <c>AddJobAsync</c>/
    /// <c>GetJobsAsync</c> are exercised; the rest throw so an accidental dependency is loud.
    /// </summary>
    private sealed class FakeMetadataStore : IMetadataStore
    {
        private readonly List<JobHistoryEntry> _jobs = [];
        private long _nextId;

        public bool ThrowOnAdd { get; init; }
        public int AddedCount { get { lock (_jobs) { return _jobs.Count; } } }

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<long> AddJobAsync(JobHistoryEntry entry, CancellationToken cancellationToken = default)
        {
            if (ThrowOnAdd)
            {
                throw new InvalidOperationException("store unavailable");
            }

            lock (_jobs)
            {
                entry.Id = ++_nextId;
                // Snapshot so later mutation of the live job cannot alter the stored row.
                _jobs.Add(new JobHistoryEntry
                {
                    Id = entry.Id,
                    JobType = entry.JobType,
                    Phases = entry.Phases.ToArray(),
                    Result = entry.Result,
                    LogPath = entry.LogPath,
                    StartedAt = entry.StartedAt,
                    FinishedAt = entry.FinishedAt,
                });
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

        // ── Unused by the job engine ──
        public Task<long> AddRestoredDatabaseAsync(RestoredDatabase record, CancellationToken cancellationToken = default) => throw new NotSupportedException();
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
