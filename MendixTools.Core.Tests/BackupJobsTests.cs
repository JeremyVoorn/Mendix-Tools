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

    // ── MT-15 hardening — default comment + resolve-without-id ────────────────────────────────

    [Fact]
    public async Task CreateBackup_NoComment_SendsDefaultProvenanceComment()
    {
        var api = new FakeApi
        {
            CreateSnapshotResult = Ok(new SnapshotRaw { SnapshotId = "s1", State = "queued" }),
            GetSnapshotsResponder = _ => Ok(new SnapshotsResponseRaw
            {
                Total = 1,
                Snapshots = [new SnapshotRaw { SnapshotId = "s1", State = "completed" }],
            }),
        };

        var (jobs, engine, _, _) = NewJobs(api);
        var job = jobs.StartCreateBackup("proj", "env-1", "Prod");
        await engine.WaitAsync(job.Id);

        Assert.Equal(JobState.Succeeded, job.State);
        Assert.Equal("Created from Mendix Tools", api.LastCreateComment);
    }

    [Fact]
    public async Task CreateBackup_ResponseWithoutSnapshotId_ResolvesViaListMatch_Succeeds()
    {
        var now = DateTimeOffset.UtcNow;
        var api = new FakeApi
        {
            // Live shape is unverified — the POST may return 2xx/empty with NO snapshot_id.
            CreateSnapshotResult = Ok(new SnapshotRaw { SnapshotId = null, State = "queued" }),
        };
        // Both the resolve step and the terminal poll read the list; the new snapshot is matched
        // by our default provenance comment + a recent created_at.
        api.GetSnapshotsResponder = call => Ok(new SnapshotsResponseRaw
        {
            Total = 1,
            Snapshots =
            [
                new SnapshotRaw
                {
                    SnapshotId = "resolved-1",
                    Comment = "Created from Mendix Tools",
                    CreatedAt = now,
                    State = call == 0 ? "running" : "completed",
                },
            ],
        });

        var (jobs, engine, _, notifier) = NewJobs(api);
        var job = jobs.StartCreateBackup("proj", "env-1", "Prod");
        await engine.WaitAsync(job.Id);

        Assert.Equal(JobState.Succeeded, job.State);
        Assert.Contains(job.Log, l => l.Message.Contains("resolved-1"));
        Assert.Single(notifier.Successes);
    }

    // ── MT-16 download job ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Download_HappyPath_DeterminateProgress_RecordsSize_FiresToast()
    {
        var payload = Gzip("db-only archive"u8.ToArray());
        var api = new FakeApi
        {
            CreateArchiveResponder = _ => Ok(new ArchiveRaw { ArchiveId = "arc-1", State = "queued" }),
            GetArchiveResponder = _ => Ok(new ArchiveRaw { ArchiveId = "arc-1", State = "completed", Url = "https://blob.example/arc-1?sig=abc" }),
            DownloadResponder = _ => SuccessDownload(payload, payload.Length),
        };

        using var dir = new TempDir();
        var (jobs, engine, store, notifier) = NewJobs(api);
        var job = jobs.StartDownload("proj", "env-1", "snap-1", "Acme · Production", Options(dir.Path));
        await engine.WaitAsync(job.Id);

        Assert.Equal(JobState.Succeeded, job.State);
        Assert.Equal(["Requesting archive", "Waiting for archive", "Downloading", "Verifying", "Done"], job.Phases);
        Assert.Equal(100d, job.Progress);

        // Size recorded to the store (the ONLY size source).
        Assert.True(store.RecordedSizes.TryGetValue("snap-1", out var size));
        Assert.Equal(payload.Length, size);

        var finalPath = Path.Combine(dir.Path, "snapshot-snap-1.backup");
        Assert.True(File.Exists(finalPath));
        Assert.False(File.Exists(finalPath + ".part")); // partial moved into place

        var toast = Assert.Single(notifier.Successes);
        Assert.Contains(finalPath, toast.Title);
        Assert.Contains("downloaded", toast.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(notifier.Errors);
    }

    [Fact]
    public async Task Download_ContentLengthMismatch_FailsIntegrity_DeletesPartial()
    {
        var payload = Gzip("short"u8.ToArray());
        var api = new FakeApi
        {
            CreateArchiveResponder = _ => Ok(new ArchiveRaw { ArchiveId = "arc-1", State = "completed", Url = "https://blob.example/arc-1?sig=abc" }),
            // Advertise MORE bytes than the stream carries → truncated download.
            DownloadResponder = _ => SuccessDownload(payload, payload.Length + 500),
        };

        using var dir = new TempDir();
        var (jobs, engine, store, notifier) = NewJobs(api);
        var job = jobs.StartDownload("proj", "env-1", "snap-1", "Prod", Options(dir.Path));
        await engine.WaitAsync(job.Id);

        Assert.Equal(JobState.Failed, job.State);
        Assert.Contains("integrity check", job.Message);
        Assert.False(File.Exists(Path.Combine(dir.Path, "snapshot-snap-1.backup")));
        Assert.False(File.Exists(Path.Combine(dir.Path, "snapshot-snap-1.backup.part")));
        Assert.False(store.RecordedSizes.ContainsKey("snap-1")); // nothing recorded on failure
        var error = Assert.Single(notifier.Errors);
        Assert.Equal("Download failed", error.Title);
    }

    [Fact]
    public async Task Download_CorruptArchive_FailsStructuralIntegrity_DeletesPartial()
    {
        var corrupt = new byte[1024]; // random-ish bytes, no known magic; no Content-Length
        new Random(7).NextBytes(corrupt);
        var api = new FakeApi
        {
            CreateArchiveResponder = _ => Ok(new ArchiveRaw { ArchiveId = "arc-1", State = "completed", Url = "https://blob.example/arc-1?sig=abc" }),
            DownloadResponder = _ => SuccessDownload(corrupt, contentLength: null),
        };

        using var dir = new TempDir();
        var (jobs, engine, _, notifier) = NewJobs(api);
        var job = jobs.StartDownload("proj", "env-1", "snap-1", "Prod", Options(dir.Path));
        await engine.WaitAsync(job.Id);

        Assert.Equal(JobState.Failed, job.State);
        Assert.Contains("integrity check", job.Message);
        Assert.False(File.Exists(Path.Combine(dir.Path, "snapshot-snap-1.backup.part")));
        Assert.Single(notifier.Errors);
    }

    [Fact]
    public async Task Download_ExpiredLink_ReRequestsArchiveOnce_ThenSucceeds()
    {
        var payload = Gzip("second try"u8.ToArray());
        var api = new FakeApi
        {
            CreateArchiveResponder = _ => Ok(new ArchiveRaw { ArchiveId = "arc-1", State = "completed", Url = "https://blob.example/arc-1?sig=fresh" }),
            // First open: expired link (8h window). Second open (after re-request): success.
            DownloadResponder = call => call == 0 ? MendixArchiveDownload.Expired() : SuccessDownload(payload, payload.Length),
        };

        using var dir = new TempDir();
        var (jobs, engine, _, notifier) = NewJobs(api);
        var job = jobs.StartDownload("proj", "env-1", "snap-1", "Prod", Options(dir.Path));
        await engine.WaitAsync(job.Id);

        Assert.Equal(JobState.Succeeded, job.State);
        Assert.Equal(2, api.CreateArchiveCalls);        // archive re-requested exactly once
        Assert.Equal(2, api.DownloadCalls);
        Assert.Contains(job.Log, l => l.Message.Contains("expired", StringComparison.OrdinalIgnoreCase));
        Assert.Single(notifier.Successes);
    }

    [Fact]
    public async Task Download_RateLimitedOnDownload_RetriesWithinJob_ThenSucceeds()
    {
        var payload = Gzip("after 429"u8.ToArray());
        var api = new FakeApi
        {
            CreateArchiveResponder = _ => Ok(new ArchiveRaw { ArchiveId = "arc-1", State = "completed", Url = "https://blob.example/arc-1?sig=abc" }),
            DownloadResponder = call => call == 0
                ? MendixArchiveDownload.Fail(MendixApiOutcome.RateLimited, "rate limited", TimeSpan.Zero)
                : SuccessDownload(payload, payload.Length),
        };

        using var dir = new TempDir();
        var (jobs, engine, _, _) = NewJobs(api);
        var job = jobs.StartDownload("proj", "env-1", "snap-1", "Prod", Options(dir.Path));
        await engine.WaitAsync(job.Id);

        Assert.Equal(JobState.Succeeded, job.State);
        Assert.Contains(job.Log, l => l.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Download_ServerSideArchiveFailure_FailsWithApiReason()
    {
        var api = new FakeApi
        {
            CreateArchiveResponder = _ => Ok(new ArchiveRaw { ArchiveId = "arc-1", State = "queued" }),
            GetArchiveResponder = _ => Ok(new ArchiveRaw { ArchiveId = "arc-1", State = "failed", StatusMessage = "archive worker crashed" }),
        };

        using var dir = new TempDir();
        var (jobs, engine, _, notifier) = NewJobs(api);
        var job = jobs.StartDownload("proj", "env-1", "snap-1", "Prod", Options(dir.Path));
        await engine.WaitAsync(job.Id);

        Assert.Equal(JobState.Failed, job.State);
        Assert.Contains("archive worker crashed", job.Message);
        Assert.Single(notifier.Errors);
    }

    [Fact]
    public async Task Download_Unauthorized_FailsWithCredentialsMessage()
    {
        var api = new FakeApi
        {
            CreateArchiveResponder = _ => Fail<ArchiveRaw>(MendixApiOutcome.Unauthorized),
        };

        using var dir = new TempDir();
        var (jobs, engine, _, _) = NewJobs(api);
        var job = jobs.StartDownload("proj", "env-1", "snap-1", "Prod", Options(dir.Path));
        await engine.WaitAsync(job.Id);

        Assert.Equal(JobState.Failed, job.State);
        Assert.Equal("Credentials invalid — check Settings › Credentials.", job.Message);
    }

    [Fact]
    public async Task Download_CancelledMidDownload_EndsCancelled_RemovesPartial_NoToast()
    {
        var blocking = new BlockingStream();
        var api = new FakeApi
        {
            CreateArchiveResponder = _ => Ok(new ArchiveRaw { ArchiveId = "arc-1", State = "completed", Url = "https://blob.example/arc-1?sig=abc" }),
            DownloadResponder = _ => MendixArchiveDownload.Success(blocking, contentLength: null, new NoopDisposable()),
        };

        using var dir = new TempDir();
        var (jobs, engine, _, notifier) = NewJobs(api, pollInterval: TimeSpan.FromMilliseconds(10));
        var job = jobs.StartDownload("proj", "env-1", "snap-1", "Prod", Options(dir.Path));

        await blocking.Started;              // streaming has begun and is blocked
        Assert.True(jobs.Cancel(job.Id));
        await engine.WaitAsync(job.Id);

        Assert.Equal(JobState.Cancelled, job.State);
        Assert.False(File.Exists(Path.Combine(dir.Path, "snapshot-snap-1.backup.part")));
        Assert.False(File.Exists(Path.Combine(dir.Path, "snapshot-snap-1.backup")));
        Assert.Empty(notifier.Successes);
        Assert.Empty(notifier.Errors);       // cancellation is user-initiated → no toast
    }

    // ── Helpers, fakes & fixtures ────────────────────────────────────────────────────────────

    private static DownloadOptions Options(string dir) => new(dir, VerifyIntegrity: true, KeepFile: true);

    private static MendixArchiveDownload SuccessDownload(byte[] bytes, long? contentLength) =>
        MendixArchiveDownload.Success(new MemoryStream(bytes), contentLength, new NoopDisposable());

    private static byte[] Gzip(byte[] payload)
    {
        using var ms = new MemoryStream();
        using (var gz = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
        {
            gz.Write(payload);
        }

        return ms.ToArray();
    }

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose() { }
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"mxt-dl-{Guid.NewGuid():N}");

        public TempDir() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { /* best-effort */ }
        }
    }

    /// <summary>A read stream that signals when reading starts, then blocks until cancelled.</summary>
    private sealed class BlockingStream : Stream
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _first = true;

        public Task Started => _started.Task;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_first)
            {
                _first = false;
                _started.TrySetResult();
            }

            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static (BackupJobs jobs, JobEngine engine, FakeStore store, FakeNotifier notifier) NewJobs(
        FakeApi api, TimeProvider? clock = null, TimeSpan? pollInterval = null)
    {
        var store = new FakeStore();
        var engine = new JobEngine(store, clock);
        var notifier = new FakeNotifier();
        var jobs = new BackupJobs(engine, api, notifier, clock, pollInterval ?? TimeSpan.Zero, store);
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

        // MT-16 archive/download seam.
        public Func<int, MendixApiResult<ArchiveRaw>>? CreateArchiveResponder { get; set; }
        public Func<int, MendixApiResult<ArchiveRaw>>? GetArchiveResponder { get; set; }
        public Func<int, MendixArchiveDownload>? DownloadResponder { get; set; }

        public int GetSnapshotsCalls { get; private set; }
        public int CreateSnapshotCalls { get; private set; }
        public int CreateArchiveCalls { get; private set; }
        public int GetArchiveCalls { get; private set; }
        public int DownloadCalls { get; private set; }

        public string? LastCreateProjectId { get; private set; }
        public string? LastCreateEnvironmentId { get; private set; }
        public string? LastCreateComment { get; private set; }

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
            LastCreateComment = comment;
            return Task.FromResult(CreateSnapshotResult ?? throw new Xunit.Sdk.XunitException("CreateSnapshotResult not set."));
        }

        public Task<MendixApiResult<ArchiveRaw>> CreateArchiveAsync(string projectId, string environmentId, string snapshotId, string dataType = "database_only", CancellationToken ct = default)
            => Task.FromResult((CreateArchiveResponder ?? throw new Xunit.Sdk.XunitException("CreateArchiveResponder not set."))(CreateArchiveCalls++));

        public Task<MendixApiResult<ArchiveRaw>> GetArchiveAsync(string projectId, string environmentId, string snapshotId, string archiveId, CancellationToken ct = default)
            => Task.FromResult((GetArchiveResponder ?? throw new Xunit.Sdk.XunitException("GetArchiveResponder not set."))(GetArchiveCalls++));

        public Task<MendixArchiveDownload> OpenArchiveDownloadAsync(string url, CancellationToken ct = default)
            => Task.FromResult((DownloadResponder ?? throw new Xunit.Sdk.XunitException("DownloadResponder not set."))(DownloadCalls++));
    }

    /// <summary>In-memory store supporting the job-history + snapshot-size operations these tests use.</summary>
    private sealed class FakeStore : IMetadataStore
    {
        private readonly List<JobHistoryEntry> _jobs = [];
        private readonly Dictionary<string, long> _sizes = [];
        private long _nextId;

        public IReadOnlyDictionary<string, long> RecordedSizes => _sizes;

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

        // ── MT-16 snapshot sizes (the only size source) ──
        public Task RecordSnapshotSizeAsync(string snapshotId, long sizeBytes, DateTimeOffset recordedAt, CancellationToken cancellationToken = default)
        {
            lock (_sizes)
            {
                _sizes[snapshotId] = sizeBytes;
            }

            return Task.CompletedTask;
        }

        public Task<long?> GetSnapshotSizeAsync(string snapshotId, CancellationToken cancellationToken = default)
        {
            lock (_sizes)
            {
                return Task.FromResult(_sizes.TryGetValue(snapshotId, out var v) ? v : (long?)null);
            }
        }
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
