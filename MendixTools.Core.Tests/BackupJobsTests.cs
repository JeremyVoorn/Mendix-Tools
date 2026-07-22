using Mendix_Tools.Services;
using MendixTools.Core.Jobs;
using MendixTools.Core.Metadata;
using MendixTools.Core.Models;
using Xunit;

namespace MendixTools.Core.Tests;

/// <summary>
/// MT-15 — orchestration tests for <see cref="BackupJobs"/>: the create-backup job state machine
/// runs through a REAL <see cref="JobEngine"/> with a fake <see cref="IMendixApiClient"/> and a
/// fake notifier.
///
/// SECURITY: NO live Mendix Platform API call is ever made — every create/poll is a canned
/// in-memory response. No credential appears anywhere. This exercises the create+poll state
/// machine (POST snapshot → poll until completed/failed) with a mocked API client.
/// </summary>
public sealed class BackupJobsTests
{
    // ── MT-15 create backup ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateBackup_PollsToCompleted_Succeeds_PersistsHistory_FiresSuccessToast()
    {
        var api = new FakeApi
        {
            CreateSnapshotResult = Ok(new SnapshotRaw { SnapshotId = "s1", State = "queued" }),
        };
        // First poll: still running; second poll: completed.
        api.GetSnapshotsResponder = call => Ok(new SnapshotsResponseRaw
        {
            Total = 1,
            Snapshots = [new SnapshotRaw { SnapshotId = "s1", State = call == 0 ? "running" : "completed" }],
        });

        var (jobs, engine, store, notifier) = NewJobs(api);
        var job = jobs.StartCreateBackup("proj", "env-1", "Acme Insurance · Production");
        await engine.WaitAsync(job.Id);

        Assert.Equal(JobState.Succeeded, job.State);
        Assert.Contains("Requesting backup", job.Phases);
        Assert.Contains("Waiting for Mendix to finish", job.Phases);
        var row = Assert.Single(await store.GetJobsAsync());
        Assert.Equal("create-backup", row.JobType);
        Assert.Equal(JobResult.Succeeded, row.Result);
        var toast = Assert.Single(notifier.Successes);
        Assert.Contains("Acme Insurance · Production", toast.Title);
        Assert.Empty(notifier.Errors);
    }

    [Fact]
    public async Task CreateBackup_SnapshotFails_JobFailsWithApiReason_FiresErrorToast()
    {
        var api = new FakeApi
        {
            CreateSnapshotResult = Ok(new SnapshotRaw { SnapshotId = "s1", State = "queued" }),
            GetSnapshotsResponder = _ => Ok(new SnapshotsResponseRaw
            {
                Total = 1,
                Snapshots = [new SnapshotRaw { SnapshotId = "s1", State = "failed", StatusMessage = "disk full on node" }],
            }),
        };

        var (jobs, engine, _, notifier) = NewJobs(api);
        var job = jobs.StartCreateBackup("proj", "env-1", "Prod");
        await engine.WaitAsync(job.Id);

        Assert.Equal(JobState.Failed, job.State);
        Assert.Contains("disk full on node", job.Message);
        Assert.Single(notifier.Errors);
        Assert.Empty(notifier.Successes);
    }

    [Fact]
    public async Task CreateBackup_Unauthorized_FailsWithCredentialsMessage()
    {
        var api = new FakeApi { CreateSnapshotResult = Fail<SnapshotRaw>(MendixApiOutcome.Unauthorized) };

        var (jobs, engine, _, notifier) = NewJobs(api);
        var job = jobs.StartCreateBackup("proj", "env-1", "Prod");
        await engine.WaitAsync(job.Id);

        Assert.Equal(JobState.Failed, job.State);
        Assert.Equal("Credentials invalid — check Settings › Credentials.", job.Message);
        Assert.Single(notifier.Errors);
    }

    [Fact]
    public async Task CreateBackup_RateLimitedThenSucceeds_RetriesWithinJob()
    {
        var api = new FakeApi
        {
            CreateSnapshotResult = Ok(new SnapshotRaw { SnapshotId = "s1", State = "queued" }),
        };
        // First snapshots poll is 429 (Retry-After zero → no real wait), then completed.
        api.GetSnapshotsResponder = call => call == 0
            ? Fail<SnapshotsResponseRaw>(MendixApiOutcome.RateLimited, TimeSpan.Zero)
            : Ok(new SnapshotsResponseRaw { Total = 1, Snapshots = [new SnapshotRaw { SnapshotId = "s1", State = "completed" }] });

        var (jobs, engine, _, _) = NewJobs(api);
        var job = jobs.StartCreateBackup("proj", "env-1", "Prod");
        await engine.WaitAsync(job.Id);

        Assert.Equal(JobState.Succeeded, job.State);
        Assert.Contains(job.Log, l => l.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreateBackup_Cancelled_StopsPolling_NoToast()
    {
        var reachedPolling = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new FakeApi
        {
            CreateSnapshotResult = Ok(new SnapshotRaw { SnapshotId = "s1", State = "queued" }),
        };
        // Never completes — stays running so the job is still polling when we cancel.
        api.GetSnapshotsResponder = _ =>
        {
            reachedPolling.TrySetResult();
            return Ok(new SnapshotsResponseRaw
            {
                Total = 1,
                Snapshots = [new SnapshotRaw { SnapshotId = "s1", State = "running" }],
            });
        };

        // A small non-zero poll interval yields between polls so cancellation is observed cleanly.
        var (jobs, engine, _, notifier) = NewJobs(api, pollInterval: TimeSpan.FromMilliseconds(10));
        var job = jobs.StartCreateBackup("proj", "env-1", "Prod");

        await reachedPolling.Task;          // polling has started
        Assert.True(jobs.Cancel(job.Id));
        await engine.WaitAsync(job.Id);

        Assert.Equal(JobState.Cancelled, job.State);
        Assert.Empty(notifier.Successes);   // no success toast on cancel
        Assert.Empty(notifier.Errors);      // cancellation is user-initiated → no failure toast
    }

    // ── Helpers, fakes & fixtures ────────────────────────────────────────────────────────────

    private static (BackupJobs jobs, JobEngine engine, FakeStore store, FakeNotifier notifier) NewJobs(
        FakeApi api, TimeProvider? clock = null, TimeSpan? pollInterval = null)
    {
        var store = new FakeStore();
        var engine = new JobEngine(store, clock);
        var notifier = new FakeNotifier();
        var jobs = new BackupJobs(engine, api, notifier, clock, pollInterval ?? TimeSpan.Zero);
        return (jobs, engine, store, notifier);
    }

    private static MendixApiResult<T> Ok<T>(T value) => MendixApiResult<T>.Ok(value);
    private static MendixApiResult<T> Fail<T>(MendixApiOutcome outcome, TimeSpan? retryAfter = null) =>
        MendixApiResult<T>.Fail(outcome, "err", retryAfter);

    private sealed class FakeNotifier : IUserNotifier
    {
        public List<(string Title, string? Message)> Successes { get; } = [];
        public List<(string Title, string? Message)> Errors { get; } = [];
        public void Success(string title, string? message = null) => Successes.Add((title, message));
        public void Error(string title, string? message = null) => Errors.Add((title, message));
    }

    /// <summary>Fully-scriptable fake client — the ONLY seam these tests touch. No HttpClient.</summary>
    private sealed class FakeApi : IMendixApiClient
    {
        public MendixApiResult<SnapshotRaw>? CreateSnapshotResult { get; set; }
        public Func<int, MendixApiResult<SnapshotsResponseRaw>>? GetSnapshotsResponder { get; set; }

        public int GetSnapshotsCalls { get; private set; }
        public int CreateSnapshotCalls { get; private set; }

        public string? LastCreateProjectId { get; private set; }
        public string? LastCreateEnvironmentId { get; private set; }

        public Task<MendixApiResult<IReadOnlyList<MendixAppRaw>>> GetAppsAsync(CancellationToken ct = default)
            => throw new Xunit.Sdk.XunitException("BackupJobs must not call GetAppsAsync.");

        public Task<MendixApiResult<IReadOnlyList<MendixEnvironmentRaw>>> GetEnvironmentsAsync(string appId, CancellationToken ct = default)
            => throw new Xunit.Sdk.XunitException("BackupJobs must not call GetEnvironmentsAsync.");

        public Task<MendixApiResult<SnapshotsResponseRaw>> GetSnapshotsAsync(string projectId, string environmentId, int? limit = null, CancellationToken ct = default)
            => Task.FromResult(GetSnapshotsResponder!(GetSnapshotsCalls++));

        public Task<MendixApiResult<SnapshotRaw>> CreateSnapshotAsync(string projectId, string environmentId, string? comment = null, CancellationToken ct = default)
        {
            CreateSnapshotCalls++;
            LastCreateProjectId = projectId;
            LastCreateEnvironmentId = environmentId;
            return Task.FromResult(CreateSnapshotResult ?? throw new Xunit.Sdk.XunitException("CreateSnapshotResult not set."));
        }
    }

    /// <summary>In-memory store supporting the job-history operations these tests use.</summary>
    private sealed class FakeStore : IMetadataStore
    {
        private readonly List<JobHistoryEntry> _jobs = [];
        private long _nextId;

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<long> AddJobAsync(JobHistoryEntry entry, CancellationToken cancellationToken = default)
        {
            lock (_jobs)
            {
                entry.Id = ++_nextId;
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

        // ── Unused by BackupJobs (MT-15) ──
        public Task RecordSnapshotSizeAsync(string snapshotId, long sizeBytes, DateTimeOffset recordedAt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<long?> GetSnapshotSizeAsync(string snapshotId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<long> AddRestoredDatabaseAsync(RestoredDatabase record, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RestoredDatabase?> GetRestoredDatabaseAsync(long id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<RestoredDatabase>> GetRestoredDatabasesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateRestoredDatabaseAsync(RestoredDatabase record, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CacheEnvironmentStateAsync(CachedEnvironmentState state, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CachedEnvironmentState?> GetEnvironmentStateAsync(string environmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CacheLastBackupAsync(string environmentId, DateTimeOffset? lastBackupAt, DateTimeOffset fetchedAt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CachedLastBackup?> GetLastBackupAsync(string environmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
