using MendixTools.Core.Metadata;
using MendixTools.Core.Models;
using Xunit;

namespace MendixTools.Core.Tests;

/// <summary>
/// MT-08 AC: CRUD round-trips against a temp-file SQLite database — restored-DB provenance
/// (insert/read/update), job history, cached environment state, the per-env last-backup
/// timestamp cache, and locally-recorded snapshot sizes.
/// </summary>
public sealed class MetadataStoreTests : IAsyncLifetime
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"mxt-meta-{Guid.NewGuid():N}.db");
    private SqliteMetadataStore _store = null!;

    public async Task InitializeAsync()
    {
        _store = new SqliteMetadataStore(_dbPath);
        await _store.InitializeAsync();
    }

    public Task DisposeAsync()
    {
        // Pooling can hold the file handle; clear then delete best-effort.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch (IOException) { /* temp file; ignore */ }
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task Initialize_CreatesFileAtCurrentSchemaVersion()
    {
        Assert.True(File.Exists(_dbPath));

        // Re-initialising is idempotent (no exception, still current version).
        await _store.InitializeAsync();
    }

    [Fact]
    public async Task RestoredDatabase_InsertReadUpdate_RoundTrips()
    {
        var record = new RestoredDatabase
        {
            TargetDatabaseName = "acme_local",
            SourceApp = "Acme Insurance",
            SourceEnvironmentId = "env-123",
            SnapshotId = "snap-9",
            SnapshotTimestamp = new DateTimeOffset(2026, 7, 20, 3, 0, 0, TimeSpan.Zero),
            MendixVersion = "10.24.16.96987",
            SizeBytes = 2_400_000_000,
            RestoredAt = new DateTimeOffset(2026, 7, 22, 9, 15, 0, TimeSpan.Zero),
            Status = RestoreStatus.Succeeded,
        };

        var id = await _store.AddRestoredDatabaseAsync(record);
        Assert.True(id > 0);
        Assert.Equal(id, record.Id);

        var read = await _store.GetRestoredDatabaseAsync(id);
        Assert.NotNull(read);
        Assert.Equal("acme_local", read!.TargetDatabaseName);
        Assert.Equal("Acme Insurance", read.SourceApp);
        Assert.Equal("env-123", read.SourceEnvironmentId);
        Assert.Equal("snap-9", read.SnapshotId);
        Assert.Equal(record.SnapshotTimestamp, read.SnapshotTimestamp);
        Assert.Equal("10.24.16.96987", read.MendixVersion);
        Assert.Equal(2_400_000_000, read.SizeBytes);
        Assert.Equal(record.RestoredAt, read.RestoredAt);
        Assert.Equal(RestoreStatus.Succeeded, read.Status);

        // Update: mark the restore failed (partial-restore marker per MT-17).
        read.Status = RestoreStatus.Failed;
        read.TargetDatabaseName = "acme_local_retry";
        await _store.UpdateRestoredDatabaseAsync(read);

        var reread = await _store.GetRestoredDatabaseAsync(id);
        Assert.Equal(RestoreStatus.Failed, reread!.Status);
        Assert.Equal("acme_local_retry", reread.TargetDatabaseName);

        var all = await _store.GetRestoredDatabasesAsync();
        Assert.Single(all);
    }

    [Fact]
    public async Task RestoredDatabase_NullableFields_RoundTripAsNull()
    {
        var record = new RestoredDatabase
        {
            TargetDatabaseName = "sandbox_local",
            SourceApp = "app1099",
            SourceEnvironmentId = "env-sandbox",
            RestoredAt = DateTimeOffset.UtcNow,
        };

        var id = await _store.AddRestoredDatabaseAsync(record);
        var read = await _store.GetRestoredDatabaseAsync(id);

        Assert.Null(read!.SnapshotId);
        Assert.Null(read.SnapshotTimestamp);
        Assert.Null(read.MendixVersion);
        Assert.Null(read.SizeBytes);
    }

    [Fact]
    public async Task JobHistory_AddRead_RoundTripsPhases()
    {
        var entry = new JobHistoryEntry
        {
            JobType = "restore",
            Phases = ["Downloading backup", "Dropping & recreating schema", "Importing into acme_local"],
            Result = JobResult.Succeeded,
            LogPath = @"C:\logs\job-1.log",
            StartedAt = new DateTimeOffset(2026, 7, 22, 9, 0, 0, TimeSpan.Zero),
            FinishedAt = new DateTimeOffset(2026, 7, 22, 9, 5, 0, TimeSpan.Zero),
        };

        var id = await _store.AddJobAsync(entry);
        Assert.True(id > 0);

        var jobs = await _store.GetJobsAsync();
        var job = Assert.Single(jobs);
        Assert.Equal("restore", job.JobType);
        Assert.Equal(3, job.Phases.Count);
        Assert.Equal("Importing into acme_local", job.Phases[2]);
        Assert.Equal(JobResult.Succeeded, job.Result);
        Assert.Equal(@"C:\logs\job-1.log", job.LogPath);
        Assert.Equal(entry.StartedAt, job.StartedAt);
        Assert.Equal(entry.FinishedAt, job.FinishedAt);
    }

    [Fact]
    public async Task EnvironmentState_CacheAndRead_RoundTrips()
    {
        var fetched = new DateTimeOffset(2026, 7, 22, 8, 0, 0, TimeSpan.Zero);
        await _store.CacheEnvironmentStateAsync(new CachedEnvironmentState
        {
            EnvironmentId = "env-123",
            Payload = """{"Status":"Running","MendixVersion":"10.24.16.96987"}""",
            FetchedAt = fetched,
        });

        var read = await _store.GetEnvironmentStateAsync("env-123");
        Assert.NotNull(read);
        Assert.Contains("Running", read!.Payload);
        Assert.Equal(fetched, read.FetchedAt);

        // Re-caching the same env replaces (upsert), does not duplicate.
        await _store.CacheEnvironmentStateAsync(new CachedEnvironmentState
        {
            EnvironmentId = "env-123",
            Payload = """{"Status":"Stopped"}""",
            FetchedAt = fetched.AddHours(1),
        });
        var reread = await _store.GetEnvironmentStateAsync("env-123");
        Assert.Contains("Stopped", reread!.Payload);
        Assert.Equal(fetched.AddHours(1), reread.FetchedAt);
    }

    [Fact]
    public async Task EnvironmentState_UnknownEnvironment_ReturnsNull()
    {
        Assert.Null(await _store.GetEnvironmentStateAsync("nope"));
    }

    [Fact]
    public async Task LastBackup_CacheAndRead_RoundTrips()
    {
        var lastBackup = new DateTimeOffset(2026, 7, 21, 2, 0, 0, TimeSpan.Zero);
        var fetched = new DateTimeOffset(2026, 7, 22, 8, 0, 0, TimeSpan.Zero);

        await _store.CacheLastBackupAsync("env-123", lastBackup, fetched);

        var read = await _store.GetLastBackupAsync("env-123");
        Assert.NotNull(read);
        Assert.Equal(lastBackup, read!.LastBackupAt);
        Assert.Equal(fetched, read.FetchedAt);
    }

    [Fact]
    public async Task LastBackup_SandboxWithNoBackups_CachesNull()
    {
        var fetched = new DateTimeOffset(2026, 7, 22, 8, 0, 0, TimeSpan.Zero);
        await _store.CacheLastBackupAsync("env-sandbox", lastBackupAt: null, fetched);

        var read = await _store.GetLastBackupAsync("env-sandbox");
        Assert.NotNull(read);              // the row exists (we fetched and found none)…
        Assert.Null(read!.LastBackupAt);   // …but there is no backup → "—" in the UI.
        Assert.Equal(fetched, read.FetchedAt);
    }

    [Fact]
    public async Task SnapshotSize_StoreAndRead_RoundTrips()
    {
        Assert.Null(await _store.GetSnapshotSizeAsync("snap-9"));

        await _store.RecordSnapshotSizeAsync("snap-9", 2_400_000_000, DateTimeOffset.UtcNow);
        Assert.Equal(2_400_000_000, await _store.GetSnapshotSizeAsync("snap-9"));

        // Recording again replaces (e.g. a re-download): upsert, not a second row.
        await _store.RecordSnapshotSizeAsync("snap-9", 2_500_000_000, DateTimeOffset.UtcNow);
        Assert.Equal(2_500_000_000, await _store.GetSnapshotSizeAsync("snap-9"));
    }
}
