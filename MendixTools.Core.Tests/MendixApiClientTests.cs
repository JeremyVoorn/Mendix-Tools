using System.Net;
using System.Text;
using Mendix_Tools.Models;
using Mendix_Tools.Services;
using Xunit;

namespace MendixTools.Core.Tests;

/// <summary>
/// MT-20 — tests for the shared <see cref="MendixApiClient"/> and <see cref="RealEnvironmentService"/>.
///
/// SECURITY: every test uses a fake <see cref="HttpMessageHandler"/> returning canned JSON
/// (shapes from docs/spikes/MT-01-auth-model.md). No live Mendix Platform API call is ever
/// made, and no real credential appears anywhere.
/// </summary>
public sealed class MendixApiClientTests
{
    // Canned JSON — property names/casing exactly as the live payloads (MT-01 spike). ────────

    private const string AppsJson = """
        [
          { "AppId": "vanschiemagazijn", "Name": "Van Schie Magazijn", "ProjectId": "b1f7c0e2-3a44-4d21-9f10-0a1b2c3d4e5f", "Url": "https://sprintr.home.mendix.com/link/project/vanschiemagazijn" },
          { "AppId": "app1099", "Name": "Weekend Prototype", "ProjectId": "e4c0f3b5-6d77-4054-8c43-3d4e5f607182", "Url": "https://sprintr.home.mendix.com/link/project/app1099" }
        ]
        """;

    // Licensed node — full field set incl. version/runtime (MT-01 §3).
    private const string LicensedEnvsJson = """
        [
          {
            "EnvironmentId": "env-prod-001", "Url": "vanschiemagazijn.mendixcloud.com",
            "Mode": "Production", "Status": "Running",
            "ModelVersion": "1.8.82.e3c1a393", "MendixVersion": "10.24.16.96987",
            "Production": true, "Instances": 2, "MemoryPerInstance": 2048, "TotalMemory": 4096,
            "RuntimeLayer": "mxruntime"
          },
          {
            "EnvironmentId": "env-acc-001", "Url": "vanschiemagazijn-accp.mendixcloud.com",
            "Mode": "Acceptance", "Status": "Stopped",
            "ModelVersion": "1.8.80.aaaa", "MendixVersion": "10.24.16.96987",
            "Production": false, "Instances": 1, "MemoryPerInstance": 1024, "TotalMemory": 1024,
            "RuntimeLayer": "mxruntime"
          }
        ]
        """;

    // Sandbox — leaner payload, NO ModelVersion/MendixVersion/RuntimeLayer (MT-01 §3).
    private const string SandboxEnvsJson = """
        [
          {
            "EnvironmentId": "env-sandbox-001", "Url": "app1099.mxapps.io",
            "Mode": "Sandbox", "Status": "Running",
            "Production": false, "Instances": 1, "MemoryPerInstance": 512, "TotalMemory": 512
          }
        ]
        """;

    // Snapshots envelope { total, snapshots[] }; intentionally NOT in created_at order to
    // prove the newest-picker takes the max, not the first (MT-01 §4).
    private const string SnapshotsJson = """
        {
          "total": 3,
          "snapshots": [
            { "snapshot_id": "s-2", "model_version": "1.8.81", "comment": "Automatically created nightly snapshot", "expires_at": "2026-08-20T00:00:00Z", "state": "completed", "status_message": "", "created_at": "2026-07-19T02:00:00Z", "finished_at": "2026-07-19T02:05:00Z", "updated_at": "2026-07-19T02:05:00Z" },
            { "snapshot_id": "s-3", "model_version": "1.8.82", "comment": "Manual backup before release", "expires_at": "2026-09-20T00:00:00Z", "state": "completed", "status_message": "", "created_at": "2026-07-21T09:30:00Z", "finished_at": "2026-07-21T09:34:00Z", "updated_at": "2026-07-21T09:34:00Z" },
            { "snapshot_id": "s-1", "comment": "Backup created by Mendix pipeline", "expires_at": "2026-08-01T00:00:00Z", "state": "failed", "status_message": "database connection refused", "created_at": "2026-07-18T02:00:00Z", "finished_at": "2026-07-18T02:01:00Z", "updated_at": "2026-07-18T02:01:00Z" }
          ]
        }
        """;

    // ── Client-level tests ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAppsAsync_ParsesLiveAppsShape_AndSendsAuthHeaders()
    {
        var handler = new FakeHandler(_ => Json(AppsJson));
        var client = NewClient(handler);

        var result = await client.GetAppsAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
        Assert.Equal("vanschiemagazijn", result.Value[0].AppId);
        Assert.Equal("b1f7c0e2-3a44-4d21-9f10-0a1b2c3d4e5f", result.Value[0].ProjectId);

        // Auth headers attached per request (not on the shared client).
        var request = Assert.Single(handler.Requests);
        Assert.Equal("test-user", request.Headers.GetValues("Mendix-Username").Single());
        Assert.Equal("test-key", request.Headers.GetValues("Mendix-ApiKey").Single());
        Assert.Equal("https://deploy.mendix.com/api/1/apps", request.RequestUri!.ToString());
    }

    [Fact]
    public async Task GetSnapshotsAsync_ParsesEnvelope_AndBuildsBackupsV2Url()
    {
        var handler = new FakeHandler(_ => Json(SnapshotsJson));
        var client = NewClient(handler);

        var result = await client.GetSnapshotsAsync("proj-guid", "env-1", limit: 1);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.Total);
        Assert.Equal(3, result.Value.Snapshots!.Count);
        Assert.Equal(
            "https://deploy.mendix.com/api/v2/apps/proj-guid/environments/env-1/snapshots?limit=1",
            Assert.Single(handler.Requests).RequestUri!.ToString());
    }

    [Fact]
    public async Task GetAppsAsync_NoCredential_ReturnsNoCredentials_WithoutHttpCall()
    {
        var handler = new FakeHandler(_ => throw new Xunit.Sdk.XunitException("HTTP must not be called without a credential"));
        var client = new MendixApiClient(new HttpClient(handler), new FakeCredentialProvider(null));

        var result = await client.GetAppsAsync();

        Assert.Equal(MendixApiOutcome.NoCredentials, result.Outcome);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetAppsAsync_401_MapsToUnauthorized_NoThrow()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var client = NewClient(handler);

        var result = await client.GetAppsAsync();

        Assert.Equal(MendixApiOutcome.Unauthorized, result.Outcome);
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task GetAppsAsync_403_MapsToForbidden_NoThrow()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var client = NewClient(handler);

        var result = await client.GetAppsAsync();

        Assert.Equal(MendixApiOutcome.Forbidden, result.Outcome);
    }

    [Fact]
    public async Task GetAppsAsync_429_MapsToRateLimited_AndParsesRetryAfterSeconds()
    {
        var handler = new FakeHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.Add("Retry-After", "120");
            return response;
        });
        var client = NewClient(handler);

        var result = await client.GetAppsAsync();

        Assert.Equal(MendixApiOutcome.RateLimited, result.Outcome);
        Assert.Equal(TimeSpan.FromSeconds(120), result.RetryAfter);
    }

    [Fact]
    public async Task GetAppsAsync_MalformedJson_MapsToInvalidResponse_NoThrow()
    {
        var handler = new FakeHandler(_ => Json("{ this is not json"));
        var client = NewClient(handler);

        var result = await client.GetAppsAsync();

        Assert.Equal(MendixApiOutcome.InvalidResponse, result.Outcome);
    }

    [Fact]
    public async Task GetAppsAsync_EmptyBody_MapsToInvalidResponse_NoThrow()
    {
        var handler = new FakeHandler(_ => Json(""));
        var client = NewClient(handler);

        var result = await client.GetAppsAsync();

        Assert.Equal(MendixApiOutcome.InvalidResponse, result.Outcome);
    }

    [Fact]
    public async Task GetAppsAsync_NetworkFailure_MapsToNetworkError_NoThrow()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("no route to host"));
        var client = NewClient(handler);

        var result = await client.GetAppsAsync();

        Assert.Equal(MendixApiOutcome.NetworkError, result.Outcome);
    }

    // ── MT-15 client tests: create snapshot ──────────────────────────────────────────────

    [Fact]
    public async Task CreateSnapshotAsync_PostsToSnapshots_WithCommentBody_ParsesQueuedSnapshot()
    {
        string? capturedBody = null;
        HttpMethod? capturedMethod = null;
        var handler = new FakeHandler(request =>
        {
            capturedMethod = request.Method;
            capturedBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return Json("""{ "snapshot_id": "s-new", "state": "queued", "comment": "before release" }""");
        });
        var client = NewClient(handler);

        var result = await client.CreateSnapshotAsync("proj", "env-1", "before release");

        Assert.True(result.IsSuccess);
        Assert.Equal("s-new", result.Value!.SnapshotId);
        Assert.Equal("queued", result.Value.State);
        Assert.Equal(HttpMethod.Post, capturedMethod);
        Assert.Contains("before release", capturedBody);
        Assert.Equal(
            "https://deploy.mendix.com/api/v2/apps/proj/environments/env-1/snapshots",
            Assert.Single(handler.Requests).RequestUri!.ToString());
    }

    [Fact]
    public async Task CreateSnapshotAsync_NoComment_SendsEmptyObject()
    {
        string? body = null;
        var handler = new FakeHandler(request =>
        {
            body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return Json("""{ "snapshot_id": "s-new", "state": "queued" }""");
        });

        await NewClient(handler).CreateSnapshotAsync("proj", "env-1");

        Assert.Equal("{}", body);
    }

    [Fact]
    public async Task CreateSnapshotAsync_401_MapsToUnauthorized_NoThrow()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var result = await NewClient(handler).CreateSnapshotAsync("proj", "env-1");
        Assert.Equal(MendixApiOutcome.Unauthorized, result.Outcome);
    }

    // ── MT-16 client tests: archive create / poll / streamed download ─────────────────────

    [Fact]
    public async Task CreateArchiveAsync_PostsDatabaseOnly_ToArchivesUrl_WithAuthHeaders()
    {
        HttpMethod? method = null;
        var handler = new FakeHandler(request =>
        {
            method = request.Method;
            return Json("""{ "archive_id": "arc-1", "state": "queued", "data_type": "database_only" }""");
        });
        var client = NewClient(handler);

        var result = await client.CreateArchiveAsync("proj", "env-1", "snap-1");

        Assert.True(result.IsSuccess);
        Assert.Equal("arc-1", result.Value!.ArchiveId);
        Assert.Equal(HttpMethod.Post, method);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(
            "https://deploy.mendix.com/api/v2/apps/proj/environments/env-1/snapshots/snap-1/archives?data_type=database_only",
            request.RequestUri!.ToString());
        Assert.Equal("test-user", request.Headers.GetValues("Mendix-Username").Single());
    }

    [Fact]
    public async Task GetArchiveAsync_ParsesCompletedArchive_WithUrl()
    {
        var handler = new FakeHandler(_ =>
            Json("""{ "archive_id": "arc-1", "state": "completed", "url": "https://blob.example/arc-1?sig=abc" }"""));
        var client = NewClient(handler);

        var result = await client.GetArchiveAsync("proj", "env-1", "snap-1", "arc-1");

        Assert.True(result.IsSuccess);
        Assert.Equal("completed", result.Value!.State);
        Assert.Equal("https://blob.example/arc-1?sig=abc", result.Value.Url);
        Assert.Equal(
            "https://deploy.mendix.com/api/v2/apps/proj/environments/env-1/snapshots/snap-1/archives/arc-1",
            Assert.Single(handler.Requests).RequestUri!.ToString());
    }

    [Fact]
    public async Task CreateArchiveAsync_429_MapsToRateLimited_WithRetryAfter()
    {
        var handler = new FakeHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.Add("Retry-After", "30");
            return response;
        });

        var result = await NewClient(handler).CreateArchiveAsync("proj", "env-1", "snap-1");

        Assert.Equal(MendixApiOutcome.RateLimited, result.Outcome);
        Assert.Equal(TimeSpan.FromSeconds(30), result.RetryAfter);
    }

    [Fact]
    public async Task OpenArchiveDownloadAsync_StreamsBody_WithContentLength_NoAuthHeaderOnPresignedUrl()
    {
        var payload = "database_only archive payload"u8.ToArray();
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(payload),
        });
        var client = NewClient(handler);

        using var download = await client.OpenArchiveDownloadAsync("https://blob.example/arc-1?sig=abc");

        Assert.True(download.IsSuccess);
        Assert.Equal(payload.Length, download.ContentLength);
        using var ms = new MemoryStream();
        await download.Content!.CopyToAsync(ms);
        Assert.Equal(payload, ms.ToArray());

        // The pre-signed URL carries its own auth — NO Mendix credential header is attached.
        var request = Assert.Single(handler.Requests);
        Assert.False(request.Headers.Contains("Mendix-Username"));
        Assert.False(request.Headers.Contains("Mendix-ApiKey"));
    }

    [Fact]
    public async Task OpenArchiveDownloadAsync_403_ReportsLinkExpired()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));

        using var download = await NewClient(handler).OpenArchiveDownloadAsync("https://blob.example/arc-1?sig=stale");

        Assert.True(download.LinkExpired);
        Assert.False(download.IsSuccess);
    }

    [Fact]
    public async Task OpenArchiveDownloadAsync_429_MapsToRateLimited()
    {
        var handler = new FakeHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.Add("Retry-After", "15");
            return response;
        });

        using var download = await NewClient(handler).OpenArchiveDownloadAsync("https://blob.example/arc-1?sig=abc");

        Assert.Equal(MendixApiOutcome.RateLimited, download.Outcome);
        Assert.Equal(TimeSpan.FromSeconds(15), download.RetryAfter);
    }

    [Fact]
    public async Task OpenArchiveDownloadAsync_NetworkFailure_MapsToNetworkError_NoThrow()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("no route to host"));

        using var download = await NewClient(handler).OpenArchiveDownloadAsync("https://blob.example/arc-1?sig=abc");

        Assert.Equal(MendixApiOutcome.NetworkError, download.Outcome);
    }

    [Fact]
    public async Task OpenArchiveDownloadAsync_BlankUrl_MapsToInvalidResponse_NoHttpCall()
    {
        var handler = new FakeHandler(_ => throw new Xunit.Sdk.XunitException("must not call HTTP for a blank URL"));

        using var download = await NewClient(handler).OpenArchiveDownloadAsync("");

        Assert.Equal(MendixApiOutcome.InvalidResponse, download.Outcome);
        Assert.Empty(handler.Requests);
    }

    // ── RealEnvironmentService-level tests (mapping) ──────────────────────────────────────

    [Fact]
    public async Task GetAppsAsync_MapsAppsAndEnvironments_LicensedFullFields()
    {
        var service = new RealEnvironmentService(RoutingClient());

        var result = await service.GetAppsAsync();
        Assert.Equal(EnvironmentsOutcome.Ok, result.Outcome);
        Assert.True(result.IsSuccess);
        var apps = result.Apps;

        var licensed = Assert.Single(apps, a => a.AppId == "vanschiemagazijn");
        Assert.Equal("Van Schie Magazijn", licensed.Name);
        Assert.False(licensed.IsSandbox);
        Assert.Equal(2, licensed.Environments.Count);

        var prod = licensed.Environments[0];
        Assert.Equal("env-prod-001", prod.EnvironmentId);
        Assert.Equal(EnvironmentStatus.Running, prod.Status);
        Assert.Equal("10.24.16.96987", prod.MendixVersion);
        Assert.Equal("1.8.82.e3c1a393", prod.ModelVersion);
        Assert.True(prod.Production);
        Assert.Equal(4096, prod.TotalMemory);

        Assert.Equal(EnvironmentStatus.Stopped, licensed.Environments[1].Status);
    }

    [Fact]
    public async Task GetAppsAsync_SandboxEnvironment_HasNullVersionFields()
    {
        var service = new RealEnvironmentService(RoutingClient());

        var apps = (await service.GetAppsAsync()).Apps;

        var sandboxApp = Assert.Single(apps, a => a.AppId == "app1099");
        Assert.True(sandboxApp.IsSandbox);
        var env = Assert.Single(sandboxApp.Environments);
        Assert.Equal("Sandbox", env.Mode);
        Assert.True(env.IsSandbox);
        Assert.Null(env.MendixVersion);
        Assert.Null(env.ModelVersion);
        Assert.Null(env.RuntimeLayer);
        Assert.Equal(EnvironmentStatus.Running, env.Status);
    }

    [Fact]
    public async Task GetNewestBackupAsync_PicksNewestCreatedAt_NotFirstInList()
    {
        var service = new RealEnvironmentService(RoutingClient());

        var newest = await service.GetNewestBackupAsync("proj-guid", "env-prod-001");

        // s-3 at 2026-07-21T09:30Z is the newest, though it is the SECOND element.
        Assert.Equal(new DateTimeOffset(2026, 7, 21, 9, 30, 0, TimeSpan.Zero), newest);
    }

    [Fact]
    public async Task GetNewestBackupAsync_BlankIdentifiers_ReturnsNull_NoHttpCall()
    {
        var handler = new FakeHandler(_ => throw new Xunit.Sdk.XunitException("must not call HTTP for a blank identifier"));
        var service = new RealEnvironmentService(NewClient(handler));

        Assert.Null(await service.GetNewestBackupAsync("", "env-1"));
        Assert.Null(await service.GetNewestBackupAsync("proj", ""));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetNewestBackupAsync_ApiFailure_ReturnsNull()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var service = new RealEnvironmentService(NewClient(handler));

        Assert.Null(await service.GetNewestBackupAsync("proj", "env-1"));
    }

    [Fact]
    public async Task GetAppsAsync_CredentialRejected_MapsToCredentialsRejected_NeverThrows()
    {
        // The FIRST real call surfaces a 401 gracefully as an actionable state, not a crash.
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var service = new RealEnvironmentService(NewClient(handler));

        var result = await service.GetAppsAsync();

        Assert.Equal(EnvironmentsOutcome.CredentialsRejected, result.Outcome);
        Assert.Empty(result.Apps);
    }

    [Fact]
    public async Task GetAppsAsync_Forbidden_MapsToCredentialsRejected()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var service = new RealEnvironmentService(NewClient(handler));

        var result = await service.GetAppsAsync();

        Assert.Equal(EnvironmentsOutcome.CredentialsRejected, result.Outcome);
        Assert.Empty(result.Apps);
    }

    [Fact]
    public async Task GetAppsAsync_NetworkFailure_MapsToOffline()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("no route to host"));
        var service = new RealEnvironmentService(NewClient(handler));

        var result = await service.GetAppsAsync();

        Assert.Equal(EnvironmentsOutcome.Offline, result.Outcome);
        Assert.Empty(result.Apps);
    }

    [Fact]
    public async Task GetAppsAsync_MalformedResponse_MapsToError()
    {
        var handler = new FakeHandler(_ => Json("{ not json"));
        var service = new RealEnvironmentService(NewClient(handler));

        var result = await service.GetAppsAsync();

        Assert.Equal(EnvironmentsOutcome.Error, result.Outcome);
        Assert.Empty(result.Apps);
    }

    [Fact]
    public async Task GetAppsAsync_RateLimited_MapsToError()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        var service = new RealEnvironmentService(NewClient(handler));

        var result = await service.GetAppsAsync();

        // 429 is not a connectivity fault → generic (retryable) Error, not Offline.
        Assert.Equal(EnvironmentsOutcome.Error, result.Outcome);
    }

    [Fact]
    public async Task GetAppsAsync_NoCredential_MapsToNoCredentials_NeverThrows()
    {
        var handler = new FakeHandler(_ => Json(AppsJson));
        var service = new RealEnvironmentService(
            new MendixApiClient(new HttpClient(handler), new FakeCredentialProvider(null)));

        var result = await service.GetAppsAsync();

        Assert.Equal(EnvironmentsOutcome.NoCredentials, result.Outcome);
        Assert.Empty(result.Apps);
        Assert.Empty(handler.Requests); // never hits the network without a credential.
    }

    // ── Fakes & helpers ───────────────────────────────────────────────────────────────────

    private static MendixApiClient NewClient(FakeHandler handler) =>
        new(new HttpClient(handler), new FakeCredentialProvider(new MendixCredential("test-user", "test-key")));

    /// <summary>A client whose handler routes by URL to the canned apps/environments/snapshots JSON.</summary>
    private static MendixApiClient RoutingClient()
    {
        var handler = new FakeHandler(request =>
        {
            var url = request.RequestUri!.ToString();
            if (url.EndsWith("snapshots") || url.Contains("snapshots?"))
            {
                return Json(SnapshotsJson);
            }

            if (url.Contains("/environments"))
            {
                return Json(url.Contains("app1099") ? SandboxEnvsJson : LicensedEnvsJson);
            }

            if (url.EndsWith("/apps"))
            {
                return Json(AppsJson);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        return NewClient(handler);
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private sealed class FakeCredentialProvider : IMendixCredentialProvider
    {
        private readonly MendixCredential? _credential;
        public FakeCredentialProvider(MendixCredential? credential) => _credential = credential;
        public Task<MendixCredential?> GetCredentialAsync(CancellationToken ct = default) => Task.FromResult(_credential);
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<HttpRequestMessage> Requests { get; } = new();

        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_responder(request));
        }
    }
}
