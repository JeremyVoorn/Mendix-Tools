using Mendix_Tools.Models;
using Mendix_Tools.Services;
using Xunit;

namespace MendixTools.Core.Tests;

/// <summary>
/// MT-14 — tests for <see cref="RealBackupService"/>: the translation of the shared client's
/// typed <see cref="MendixApiOutcome"/> into the exception/result contract the Backups page +
/// its <c>MapError</c> expect, and the raw→<see cref="Snapshot"/> mapping (state, comment-derived
/// type, status_message, model_version).
///
/// SECURITY: uses a FAKE <see cref="IMendixApiClient"/> only — no <see cref="System.Net.Http.HttpClient"/>,
/// no live Mendix Platform API call, no real credential anywhere.
/// </summary>
public sealed class RealBackupServiceTests
{
    // ── Outcome → exception/result translation ────────────────────────────────────────────

    [Fact]
    public async Task Unauthorized_ThrowsUnauthorizedAccess()
    {
        var service = new RealBackupService(FailWith(MendixApiOutcome.Unauthorized));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.GetSnapshotsAsync("proj", "env"));
    }

    [Fact]
    public async Task Forbidden_ThrowsUnauthorizedAccess()
    {
        var service = new RealBackupService(FailWith(MendixApiOutcome.Forbidden));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.GetSnapshotsAsync("proj", "env"));
    }

    [Fact]
    public async Task NetworkError_ThrowsHttpRequestException()
    {
        var service = new RealBackupService(FailWith(MendixApiOutcome.NetworkError));
        await Assert.ThrowsAsync<HttpRequestException>(
            () => service.GetSnapshotsAsync("proj", "env"));
    }

    [Fact]
    public async Task RateLimited_ThrowsHttpRequestException()
    {
        var service = new RealBackupService(FailWith(MendixApiOutcome.RateLimited));
        await Assert.ThrowsAsync<HttpRequestException>(
            () => service.GetSnapshotsAsync("proj", "env"));
    }

    [Fact]
    public async Task InvalidResponse_ThrowsInvalidOperation()
    {
        var service = new RealBackupService(FailWith(MendixApiOutcome.InvalidResponse));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GetSnapshotsAsync("proj", "env"));
    }

    [Fact]
    public async Task NoCredentials_ReturnsCleanEmptyResult_NeverThrows()
    {
        var service = new RealBackupService(FailWith(MendixApiOutcome.NoCredentials));

        var result = await service.GetSnapshotsAsync("proj", "env");

        Assert.Equal(0, result.Total);
        Assert.Empty(result.Snapshots);
    }

    [Fact]
    public async Task ThrownMessages_CarryNoSecret()
    {
        // Defence-in-depth: the credential-rejected message must never echo a key/username.
        var service = new RealBackupService(FailWith(MendixApiOutcome.Unauthorized));
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.GetSnapshotsAsync("proj", "env"));

        Assert.DoesNotContain("key", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── Success: raw → Snapshot mapping ───────────────────────────────────────────────────

    [Fact]
    public async Task Success_MapsEnvelope_TotalAndRowCount()
    {
        var service = new RealBackupService(SucceedWith(SampleResponse()));

        var result = await service.GetSnapshotsAsync("proj", "env");

        Assert.Equal(139, result.Total); // server-side total is preserved (not the page size)
        Assert.Equal(3, result.Snapshots.Count);
    }

    [Fact]
    public async Task Success_MapsCompletedNightly_ToAutomaticType_AndActions()
    {
        var service = new RealBackupService(SucceedWith(SampleResponse()));

        var nightly = (await service.GetSnapshotsAsync("proj", "env")).Snapshots
            .Single(s => s.SnapshotId == "s-nightly");

        Assert.Equal(SnapshotState.Completed, nightly.State);
        Assert.Equal(SnapshotType.Automatic, nightly.Type);
        Assert.True(nightly.HasActions);
        Assert.Null(nightly.StatusMessage);           // "" on success normalised to null
        Assert.Equal("1.12.4.5521", nightly.ModelVersion);
        Assert.Equal(new DateTimeOffset(2026, 7, 21, 2, 0, 0, TimeSpan.Zero), nightly.CreatedAt);
        Assert.Equal(new DateTimeOffset(2026, 10, 21, 0, 0, 0, TimeSpan.Zero), nightly.ExpiresAt);
    }

    [Fact]
    public async Task Success_MapsManualComment_ToManualType_KeepsComment()
    {
        var service = new RealBackupService(SucceedWith(SampleResponse()));

        var manual = (await service.GetSnapshotsAsync("proj", "env")).Snapshots
            .Single(s => s.SnapshotId == "s-manual");

        Assert.Equal(SnapshotType.Manual, manual.Type);
        Assert.Equal("Before v10.12 model upgrade", manual.Comment);
        Assert.True(manual.HasActions);
    }

    [Fact]
    public async Task Success_MapsFailedSnapshot_SurfacesStatusMessage_NoActions()
    {
        var service = new RealBackupService(SucceedWith(SampleResponse()));

        var failed = (await service.GetSnapshotsAsync("proj", "env")).Snapshots
            .Single(s => s.SnapshotId == "s-failed");

        Assert.Equal(SnapshotState.Failed, failed.State);
        Assert.Equal("database connection refused", failed.StatusMessage);
        Assert.Null(failed.ModelVersion);   // absent on failed snapshots (live run §4)
        Assert.False(failed.HasActions);    // failed rows have no Restore/Download
    }

    [Fact]
    public async Task Success_NullSnapshotsArray_ReturnsEmptyPageWithTotal()
    {
        var service = new RealBackupService(SucceedWith(new SnapshotsResponseRaw { Total = 0, Snapshots = null }));

        var result = await service.GetSnapshotsAsync("proj", "env");

        Assert.Equal(0, result.Total);
        Assert.Empty(result.Snapshots);
    }

    [Fact]
    public async Task Success_PassesIdentifiersThroughToClient()
    {
        var client = new FakeApiClient { Result = MendixApiResult<SnapshotsResponseRaw>.Ok(SampleResponse()) };
        var service = new RealBackupService(client);

        await service.GetSnapshotsAsync("the-project-guid", "the-env-id");

        Assert.Equal("the-project-guid", client.LastProjectId);
        Assert.Equal("the-env-id", client.LastEnvironmentId);
    }

    // ── MT-15 CreateBackupAsync — outcome translation + mapping ───────────────────────────

    [Fact]
    public async Task CreateBackup_Success_ReturnsMappedQueuedSnapshot()
    {
        var created = new SnapshotRaw
        {
            SnapshotId = "snap-new", Comment = "Backup created from Mendix Tools",
            State = "queued", CreatedAt = new DateTimeOffset(2026, 7, 22, 10, 0, 0, TimeSpan.Zero),
        };
        var client = new FakeApiClient
        {
            Result = MendixApiResult<SnapshotsResponseRaw>.Ok(SampleResponse()),
            CreateResult = MendixApiResult<SnapshotRaw>.Ok(created),
        };
        var service = new RealBackupService(client);

        var snapshot = await service.CreateBackupAsync("proj", "env", "hello");

        Assert.Equal("snap-new", snapshot.SnapshotId);
        Assert.Equal(SnapshotState.Queued, snapshot.State);
        Assert.Equal("proj", client.LastCreateProjectId);
        Assert.Equal("hello", client.LastCreateComment);
    }

    [Fact]
    public async Task CreateBackup_Unauthorized_ThrowsUnauthorizedAccess()
    {
        var client = new FakeApiClient
        {
            Result = MendixApiResult<SnapshotsResponseRaw>.Ok(SampleResponse()),
            CreateResult = MendixApiResult<SnapshotRaw>.Fail(MendixApiOutcome.Unauthorized, "err"),
        };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => new RealBackupService(client).CreateBackupAsync("proj", "env"));
    }

    [Fact]
    public async Task CreateBackup_RateLimited_ThrowsHttpRequestException()
    {
        var client = new FakeApiClient
        {
            Result = MendixApiResult<SnapshotsResponseRaw>.Ok(SampleResponse()),
            CreateResult = MendixApiResult<SnapshotRaw>.Fail(MendixApiOutcome.RateLimited, "err"),
        };
        await Assert.ThrowsAsync<HttpRequestException>(
            () => new RealBackupService(client).CreateBackupAsync("proj", "env"));
    }

    // ── MT-16 DownloadArchiveAsync — composable one-shot seam (for MT-17) ─────────────────

    [Fact]
    public async Task DownloadArchive_Success_WritesFile_ReturnsPathAndSize()
    {
        var payload = Gzip("db-only"u8.ToArray());
        var client = new FakeApiClient
        {
            Result = MendixApiResult<SnapshotsResponseRaw>.Ok(SampleResponse()),
            CreateArchiveResult = MendixApiResult<ArchiveRaw>.Ok(new ArchiveRaw { ArchiveId = "arc-1", State = "completed", Url = "https://blob.example/arc-1?sig=abc" }),
            DownloadFactory = () => MendixArchiveDownload.Success(new MemoryStream(payload), payload.Length, new Noop()),
        };
        var dir = Path.Combine(Path.GetTempPath(), $"mxt-rbs-{Guid.NewGuid():N}");

        try
        {
            var result = await new RealBackupService(client).DownloadArchiveAsync("proj", "env", "snap-1", dir);

            Assert.True(File.Exists(result.FilePath));
            Assert.Equal(payload.Length, result.SizeBytes);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public async Task DownloadArchive_Unauthorized_ThrowsUnauthorizedAccess()
    {
        var client = new FakeApiClient
        {
            Result = MendixApiResult<SnapshotsResponseRaw>.Ok(SampleResponse()),
            CreateArchiveResult = MendixApiResult<ArchiveRaw>.Fail(MendixApiOutcome.Unauthorized, "err"),
        };
        var dir = Path.Combine(Path.GetTempPath(), $"mxt-rbs-{Guid.NewGuid():N}");

        try
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => new RealBackupService(client).DownloadArchiveAsync("proj", "env", "snap-1", dir));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public async Task DownloadArchive_CorruptArchive_ThrowsInvalidOperation_DeletesPartial()
    {
        var corrupt = new byte[512];
        new Random(3).NextBytes(corrupt);
        var client = new FakeApiClient
        {
            Result = MendixApiResult<SnapshotsResponseRaw>.Ok(SampleResponse()),
            CreateArchiveResult = MendixApiResult<ArchiveRaw>.Ok(new ArchiveRaw { ArchiveId = "arc-1", State = "completed", Url = "https://blob.example/arc-1?sig=abc" }),
            DownloadFactory = () => MendixArchiveDownload.Success(new MemoryStream(corrupt), corrupt.Length, new Noop()),
        };
        var dir = Path.Combine(Path.GetTempPath(), $"mxt-rbs-{Guid.NewGuid():N}");

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => new RealBackupService(client).DownloadArchiveAsync("proj", "env", "snap-1", dir));
            Assert.False(File.Exists(Path.Combine(dir, "snapshot-snap-1.backup.part")));
            Assert.False(File.Exists(Path.Combine(dir, "snapshot-snap-1.backup")));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    private static byte[] Gzip(byte[] payload)
    {
        using var ms = new MemoryStream();
        using (var gz = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
        {
            gz.Write(payload);
        }

        return ms.ToArray();
    }

    private sealed class Noop : IDisposable
    {
        public void Dispose() { }
    }

    // ── Fakes & sample data ───────────────────────────────────────────────────────────────

    private static SnapshotsResponseRaw SampleResponse() => new()
    {
        Total = 139, // total exceeds the page → the real page 1 of a larger set
        Snapshots =
        [
            new SnapshotRaw
            {
                SnapshotId = "s-manual", ModelVersion = "1.12.4.5521",
                Comment = "Before v10.12 model upgrade", State = "completed", StatusMessage = "",
                CreatedAt = new DateTimeOffset(2026, 7, 21, 16, 44, 0, TimeSpan.Zero),
                ExpiresAt = new DateTimeOffset(2027, 7, 21, 0, 0, 0, TimeSpan.Zero),
            },
            new SnapshotRaw
            {
                SnapshotId = "s-nightly", ModelVersion = "1.12.4.5521",
                Comment = "Automatically created nightly snapshot", State = "completed", StatusMessage = "",
                CreatedAt = new DateTimeOffset(2026, 7, 21, 2, 0, 0, TimeSpan.Zero),
                ExpiresAt = new DateTimeOffset(2026, 10, 21, 0, 0, 0, TimeSpan.Zero),
            },
            new SnapshotRaw
            {
                SnapshotId = "s-failed", ModelVersion = null,
                Comment = "Backup created by Mendix pipeline", State = "failed",
                StatusMessage = "database connection refused",
                CreatedAt = new DateTimeOffset(2026, 7, 18, 2, 0, 0, TimeSpan.Zero),
                ExpiresAt = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            },
        ],
    };

    private static FakeApiClient FailWith(MendixApiOutcome outcome) =>
        new() { Result = MendixApiResult<SnapshotsResponseRaw>.Fail(outcome, "err") };

    private static FakeApiClient SucceedWith(SnapshotsResponseRaw response) =>
        new() { Result = MendixApiResult<SnapshotsResponseRaw>.Ok(response) };

    /// <summary>A fake client — the ONLY seam these tests touch. No HttpClient, no network.</summary>
    private sealed class FakeApiClient : IMendixApiClient
    {
        public required MendixApiResult<SnapshotsResponseRaw> Result { get; init; }
        public MendixApiResult<SnapshotRaw>? CreateResult { get; init; }
        public MendixApiResult<ArchiveRaw>? CreateArchiveResult { get; init; }
        public Func<MendixArchiveDownload>? DownloadFactory { get; init; }
        public string? LastProjectId { get; private set; }
        public string? LastEnvironmentId { get; private set; }
        public string? LastCreateProjectId { get; private set; }
        public string? LastCreateComment { get; private set; }

        public Task<MendixApiResult<IReadOnlyList<MendixAppRaw>>> GetAppsAsync(CancellationToken ct = default)
            => throw new Xunit.Sdk.XunitException("RealBackupService must not call GetAppsAsync.");

        public Task<MendixApiResult<IReadOnlyList<MendixEnvironmentRaw>>> GetEnvironmentsAsync(string appId, CancellationToken ct = default)
            => throw new Xunit.Sdk.XunitException("RealBackupService must not call GetEnvironmentsAsync.");

        public Task<MendixApiResult<SnapshotsResponseRaw>> GetSnapshotsAsync(string projectId, string environmentId, int? limit = null, CancellationToken ct = default)
        {
            LastProjectId = projectId;
            LastEnvironmentId = environmentId;
            return Task.FromResult(Result);
        }

        public Task<MendixApiResult<SnapshotRaw>> CreateSnapshotAsync(string projectId, string environmentId, string? comment = null, CancellationToken ct = default)
        {
            LastCreateProjectId = projectId;
            LastCreateComment = comment;
            return Task.FromResult(CreateResult ?? throw new Xunit.Sdk.XunitException("CreateResult not configured."));
        }

        public Task<MendixApiResult<ArchiveRaw>> CreateArchiveAsync(string projectId, string environmentId, string snapshotId, string dataType = "database_only", CancellationToken ct = default)
            => Task.FromResult(CreateArchiveResult ?? throw new Xunit.Sdk.XunitException("CreateArchiveResult not configured."));

        public Task<MendixApiResult<ArchiveRaw>> GetArchiveAsync(string projectId, string environmentId, string snapshotId, string archiveId, CancellationToken ct = default)
            => throw new Xunit.Sdk.XunitException("This test's archive completes on create — GetArchiveAsync should not be called.");

        public Task<MendixArchiveDownload> OpenArchiveDownloadAsync(string url, CancellationToken ct = default)
            => Task.FromResult((DownloadFactory ?? throw new Xunit.Sdk.XunitException("DownloadFactory not configured."))());
    }
}
