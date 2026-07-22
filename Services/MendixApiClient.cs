using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Mendix_Tools.Services;

/// <summary>
/// MT-20 — the shared, low-level Mendix Platform API client (Deploy API v1 + Backups API v2).
/// Owned by MT-20; reused unchanged by MT-14 (backups list) and MT-16 (archive download).
///
/// Design notes:
///   • MAUI/Blazor-free (uses only <see cref="HttpClient"/> + <see cref="IMendixCredentialProvider"/>),
///     so it compiles into the pure net10.0 test project via source-link.
///   • The <see cref="HttpClient"/> is supplied by <c>IHttpClientFactory</c> (typed client).
///     It carries NO base address and NO default headers: every request builds an absolute URL
///     and attaches per-request auth headers, so nothing is shared or captured across calls.
///   • Auth headers are read from <see cref="IMendixCredentialProvider"/> AT CALL TIME.
///   • Every failure is mapped to a typed <see cref="MendixApiResult{T}"/>; the client never
///     throws for an HTTP/parse/transport failure and never puts a secret in a message or log.
/// </summary>
public sealed class MendixApiClient : IMendixApiClient
{
    /// <summary>Deploy API v1 base URL (apps, environments) — MT-01 spike.</summary>
    public const string DeployV1BaseUrl = "https://deploy.mendix.com/api/1/";

    /// <summary>
    /// Backups API v2 base URL (snapshots list/create, and — for MT-16 — archive create/poll/
    /// download). Archive download links expire 8 hours after completion (MT-01 confirmed);
    /// MT-16 re-requests a fresh link on expiry. Not exercised here, but the base URL and the
    /// 429/Retry-After + network handling below are already the shape MT-16 needs.
    /// </summary>
    public const string BackupsV2BaseUrl = "https://deploy.mendix.com/api/v2/";

    private const string UsernameHeader = "Mendix-Username";
    private const string ApiKeyHeader = "Mendix-ApiKey";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly IMendixCredentialProvider _credentials;

    public MendixApiClient(HttpClient http, IMendixCredentialProvider credentials)
    {
        _http = http;
        _credentials = credentials;
    }

    public Task<MendixApiResult<IReadOnlyList<MendixAppRaw>>> GetAppsAsync(CancellationToken ct = default)
    {
        var url = new Uri(new Uri(DeployV1BaseUrl), "apps");
        return GetAsync<IReadOnlyList<MendixAppRaw>>(url, ct);
    }

    public Task<MendixApiResult<IReadOnlyList<MendixEnvironmentRaw>>> GetEnvironmentsAsync(string appId, CancellationToken ct = default)
    {
        var url = new Uri(new Uri(DeployV1BaseUrl), $"apps/{Uri.EscapeDataString(appId)}/environments");
        return GetAsync<IReadOnlyList<MendixEnvironmentRaw>>(url, ct);
    }

    public Task<MendixApiResult<SnapshotsResponseRaw>> GetSnapshotsAsync(string projectId, string environmentId, int? limit = null, CancellationToken ct = default)
    {
        var path = $"apps/{Uri.EscapeDataString(projectId)}/environments/{Uri.EscapeDataString(environmentId)}/snapshots";
        if (limit is { } l)
        {
            path += $"?limit={l}";
        }

        var url = new Uri(new Uri(BackupsV2BaseUrl), path);
        return GetAsync<SnapshotsResponseRaw>(url, ct);
    }

    private async Task<MendixApiResult<T>> GetAsync<T>(Uri url, CancellationToken ct)
    {
        var credential = await _credentials.GetCredentialAsync(ct).ConfigureAwait(false);
        if (credential is null)
        {
            return MendixApiResult<T>.Fail(
                MendixApiOutcome.NoCredentials,
                "No Mendix credential is configured. Connect your Mendix account in Settings › Credentials.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        // Per-request auth (never on DefaultRequestHeaders) so credentials are not shared across
        // the pooled HttpClient and a credential change takes effect on the next call.
        request.Headers.Add(UsernameHeader, credential.Username);
        request.Headers.Add(ApiKeyHeader, credential.ApiKey);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // genuine caller cancellation (e.g. dashboard refresh) — let it propagate.
        }
        catch (OperationCanceledException)
        {
            return MendixApiResult<T>.Fail(MendixApiOutcome.NetworkError, "The Mendix API request timed out.");
        }
        catch (HttpRequestException)
        {
            // No secret in the message — the URL carries no credential and we log nothing.
            return MendixApiResult<T>.Fail(MendixApiOutcome.NetworkError, "Could not reach the Mendix API. Check your connection.");
        }

        using (response)
        {
            switch (response.StatusCode)
            {
                case HttpStatusCode.Unauthorized: // 401
                    return MendixApiResult<T>.Fail(
                        MendixApiOutcome.Unauthorized,
                        "Credential rejected — check your username and API key in Settings › Credentials.");

                case HttpStatusCode.Forbidden: // 403
                    return MendixApiResult<T>.Fail(
                        MendixApiOutcome.Forbidden,
                        "No API Rights — ask a Technical Contact for this app to grant access.");

                case HttpStatusCode.TooManyRequests: // 429
                    return MendixApiResult<T>.Fail(
                        MendixApiOutcome.RateLimited,
                        "Mendix API rate limit reached — retrying shortly.",
                        ReadRetryAfter(response));
            }

            if (!response.IsSuccessStatusCode)
            {
                return MendixApiResult<T>.Fail(
                    MendixApiOutcome.InvalidResponse,
                    $"The Mendix API returned an unexpected status ({(int)response.StatusCode}).");
            }

            try
            {
                var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct).ConfigureAwait(false);
                if (value is null)
                {
                    return MendixApiResult<T>.Fail(MendixApiOutcome.InvalidResponse, "The Mendix API returned an empty response.");
                }

                return MendixApiResult<T>.Ok(value);
            }
            catch (JsonException)
            {
                return MendixApiResult<T>.Fail(MendixApiOutcome.InvalidResponse, "The Mendix API returned an unreadable response.");
            }
            catch (NotSupportedException)
            {
                return MendixApiResult<T>.Fail(MendixApiOutcome.InvalidResponse, "The Mendix API returned an unexpected content type.");
            }
        }
    }

    /// <summary>
    /// Reads the Retry-After hint from a 429 response. Honours both the delta-seconds form
    /// (<c>Retry-After: 120</c>) and the HTTP-date form. Returns <c>null</c> when absent.
    /// </summary>
    private static TimeSpan? ReadRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter is null)
        {
            return null;
        }

        if (retryAfter.Delta is { } delta)
        {
            return delta;
        }

        if (retryAfter.Date is { } date)
        {
            var span = date - DateTimeOffset.UtcNow;
            return span > TimeSpan.Zero ? span : TimeSpan.Zero;
        }

        return null;
    }
}
