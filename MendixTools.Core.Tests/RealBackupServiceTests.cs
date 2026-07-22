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
        public string? LastProjectId { get; private set; }
        public string? LastEnvironmentId { get; private set; }

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
    }
}
