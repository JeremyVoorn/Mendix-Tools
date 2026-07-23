using System.Text.Json.Serialization;

namespace Mendix_Tools.Services;

// MT-20 — contracts for the shared Mendix Platform API client. This file is intentionally
// MAUI/Blazor-free so it can be <Compile>-linked into the pure net10.0 test project (the
// same pattern MT-08 uses for ThemeService) and reused unchanged by MT-14 (backups list),
// MT-15 (create snapshot) and MT-16 (archive download).
//
// Auth is the API-key generation confirmed by the MT-01 live run (D1): headers
// `Mendix-Username` + `Mendix-ApiKey` on every request, read from the OS vault AT CALL TIME
// (see IMendixCredentialProvider) so a credential change in Settings takes effect without a
// restart. No secret is ever captured in a constructor, logged, or written to an exception.

/// <summary>
/// One Mendix Platform credential pair (D1): the platform login <paramref name="Username"/>
/// and the API key. Never logged, never serialised, never persisted outside the OS vault.
/// </summary>
public sealed record MendixCredential(string Username, string ApiKey);

/// <summary>
/// Supplies the current Mendix credential, read fresh on each call so a Settings change is
/// picked up immediately. Returns <c>null</c> when the user has not connected an account
/// (first run / after Remove) — the client turns that into a <see cref="MendixApiOutcome.NoCredentials"/>
/// result rather than throwing (MT-13 AC: cloud screens must not error when no key is stored).
/// </summary>
public interface IMendixCredentialProvider
{
    Task<MendixCredential?> GetCredentialAsync(CancellationToken ct = default);
}

/// <summary>
/// Outcome of a Mendix API call. Every failure mode is typed so callers surface an
/// actionable message and never crash on the FIRST real call (the app does no credential
/// pre-validation — MT-13 accepted deviation).
/// </summary>
public enum MendixApiOutcome
{
    /// <summary>2xx with a well-formed body.</summary>
    Success,

    /// <summary>No credential stored — the caller should show the "connect your account" state.</summary>
    NoCredentials,

    /// <summary>HTTP 401 — credential rejected (bad username/key).</summary>
    Unauthorized,

    /// <summary>HTTP 403 — authenticated but lacks API Rights on the target.</summary>
    Forbidden,

    /// <summary>HTTP 429 — rate limited; <see cref="MendixApiResult{T}.RetryAfter"/> carries the hint when present.</summary>
    RateLimited,

    /// <summary>Transport failure or timeout — offline / unreachable.</summary>
    NetworkError,

    /// <summary>2xx (or other) with a body that could not be parsed, or an unexpected status.</summary>
    InvalidResponse,
}

/// <summary>
/// Result of a typed GET. On success <see cref="Value"/> is set; on failure <see cref="Error"/>
/// carries a user-safe message (never a secret, never a raw stack trace).
/// </summary>
public sealed class MendixApiResult<T>
{
    public required MendixApiOutcome Outcome { get; init; }
    public T? Value { get; init; }
    public string? Error { get; init; }

    /// <summary>Set for <see cref="MendixApiOutcome.RateLimited"/> when the API sent a Retry-After header.</summary>
    public TimeSpan? RetryAfter { get; init; }

    public bool IsSuccess => Outcome == MendixApiOutcome.Success;

    public static MendixApiResult<T> Ok(T value) =>
        new() { Outcome = MendixApiOutcome.Success, Value = value };

    public static MendixApiResult<T> Fail(MendixApiOutcome outcome, string error, TimeSpan? retryAfter = null) =>
        new() { Outcome = outcome, Error = error, RetryAfter = retryAfter };
}

/// <summary>
/// The shared, low-level Mendix Platform API client. Typed helpers returning strongly-typed RAW
/// DTOs that mirror the live JSON exactly (MT-01 spike). Higher layers
/// (<see cref="RealEnvironmentService"/>, <see cref="RealBackupService"/>) map these to app models.
/// </summary>
public interface IMendixApiClient
{
    /// <summary>Deploy API v1 <c>GET /api/1/apps</c> — every app the credential can see (mixes sandboxes with licensed apps).</summary>
    Task<MendixApiResult<IReadOnlyList<MendixAppRaw>>> GetAppsAsync(CancellationToken ct = default);

    /// <summary>Deploy API v1 <c>GET /api/1/apps/{appId}/environments</c> — the app's environments.</summary>
    Task<MendixApiResult<IReadOnlyList<MendixEnvironmentRaw>>> GetEnvironmentsAsync(string appId, CancellationToken ct = default);

    /// <summary>
    /// Backups API v2 <c>GET /api/v2/apps/{projectId}/environments/{environmentId}/snapshots</c>.
    /// Keyed by <c>ProjectId</c> (the GUID), not AppId. <paramref name="limit"/> caps the page
    /// (newest first) — the newest-backup path asks for 1; MT-14's list asks for a page + uses <c>total</c>.
    /// </summary>
    Task<MendixApiResult<SnapshotsResponseRaw>> GetSnapshotsAsync(string projectId, string environmentId, int? limit = null, CancellationToken ct = default);

    /// <summary>
    /// MT-15 — Backups API v2 <c>POST /api/v2/apps/{projectId}/environments/{environmentId}/snapshots</c>.
    /// Body is <c>{ "comment": "..." }</c> (or <c>{}</c> when <paramref name="comment"/> is null/blank).
    /// Returns the newly-created snapshot in its initial <c>queued</c>/<c>running</c> state; the
    /// create job then polls <see cref="GetSnapshotsAsync"/> until it reaches <c>completed</c>/<c>failed</c>.
    /// Reused by MT-17/X5 (backup-before-restore/deploy).
    /// </summary>
    Task<MendixApiResult<SnapshotRaw>> CreateSnapshotAsync(string projectId, string environmentId, string? comment = null, CancellationToken ct = default);

    /// <summary>
    /// MT-16 — Backups API v2 <c>POST .../snapshots/{snapshotId}/archives?data_type={dataType}</c>.
    /// Requests an archive for a completed snapshot (default <c>database_only</c> per D4). Returns the
    /// archive in its initial <c>queued</c>/<c>running</c> state (its <c>url</c> is populated only once
    /// the archive reaches <c>completed</c>); the download job then polls <see cref="GetArchiveAsync"/>
    /// until the URL is ready. The download link is valid for 8 hours after completion (MT-01).
    /// </summary>
    Task<MendixApiResult<ArchiveRaw>> CreateArchiveAsync(string projectId, string environmentId, string snapshotId, string dataType = "database_only", CancellationToken ct = default);

    /// <summary>
    /// MT-16 — Backups API v2 <c>GET .../snapshots/{snapshotId}/archives/{archiveId}</c>. Polls one
    /// archive's state; when <c>completed</c> the <see cref="ArchiveRaw.Url"/> carries the (8-hour)
    /// download link. <c>failed</c> surfaces <see cref="ArchiveRaw.StatusMessage"/>.
    /// </summary>
    Task<MendixApiResult<ArchiveRaw>> GetArchiveAsync(string projectId, string environmentId, string snapshotId, string archiveId, CancellationToken ct = default);

    /// <summary>
    /// MT-16 — opens the archive download as a STREAM (never buffered whole in memory). The
    /// <paramref name="url"/> is the pre-signed link from a completed archive: it carries its own
    /// auth in the query string, so NO Mendix credential/header is attached and the URL is never
    /// logged (its signature is sensitive). The caller streams <see cref="MendixArchiveDownload.Content"/>
    /// to disk and disposes the result. An expired link (8-hour window → 401/403/410) is reported as
    /// <see cref="MendixArchiveDownload.LinkExpired"/> so the job re-requests a fresh archive once;
    /// 429 is reported as <see cref="MendixApiOutcome.RateLimited"/> with the Retry-After hint.
    /// Never throws for an HTTP/transport failure.
    /// </summary>
    Task<MendixArchiveDownload> OpenArchiveDownloadAsync(string url, CancellationToken ct = default);
}

/// <summary>
/// Backups API v2 archive object (snake_case). <see cref="Url"/> is populated only when
/// <see cref="State"/> reaches <c>completed</c>; the state machine is
/// <c>queued → running → completed | failed</c>. There is NO checksum/hash field (MT-01 §A2) —
/// MT-16's LOCAL integrity check (size + gzip/zip structural test) is the only mechanism.
/// </summary>
public sealed class ArchiveRaw
{
    [JsonPropertyName("archive_id")] public string? ArchiveId { get; set; }
    [JsonPropertyName("state")] public string? State { get; set; }
    [JsonPropertyName("data_type")] public string? DataType { get; set; }
    [JsonPropertyName("snapshot_id")] public string? SnapshotId { get; set; }
    [JsonPropertyName("status_message")] public string? StatusMessage { get; set; }
    [JsonPropertyName("url")] public string? Url { get; set; }
    [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; set; }
    [JsonPropertyName("updated_at")] public DateTimeOffset? UpdatedAt { get; set; }
    [JsonPropertyName("finished_at")] public DateTimeOffset? FinishedAt { get; set; }
}

/// <summary>
/// MT-16 — the outcome of opening an archive download. On success it owns an open response +
/// content <see cref="Content"/> stream (and <see cref="ContentLength"/> when the server sent it):
/// the caller streams the body to disk and then DISPOSES this to release the socket. Failure modes
/// mirror <see cref="MendixApiOutcome"/>; <see cref="LinkExpired"/> distinguishes the recoverable
/// 8-hour-expiry case (re-request the archive) from a hard failure.
/// </summary>
public sealed class MendixArchiveDownload : IDisposable
{
    private IDisposable? _response;

    public required MendixApiOutcome Outcome { get; init; }
    public string? Error { get; init; }
    public TimeSpan? RetryAfter { get; init; }

    /// <summary>Content-Length when the server advertised it; null → progress is indeterminate and
    /// the size check falls back to the structural test alone.</summary>
    public long? ContentLength { get; init; }

    /// <summary>The archive body stream (caller streams to disk; disposed with this result).</summary>
    public Stream? Content { get; init; }

    /// <summary>True when the pre-signed link is expired/rejected (401/403/410) — the caller
    /// re-requests a fresh archive once (8-hour expiry, MT-16 AC).</summary>
    public bool LinkExpired { get; init; }

    public bool IsSuccess => Outcome == MendixApiOutcome.Success;

    internal static MendixArchiveDownload Success(Stream content, long? contentLength, IDisposable response) =>
        new() { Outcome = MendixApiOutcome.Success, Content = content, ContentLength = contentLength, _response = response };

    internal static MendixArchiveDownload Expired() =>
        new() { Outcome = MendixApiOutcome.Forbidden, LinkExpired = true, Error = "The archive download link has expired." };

    internal static MendixArchiveDownload Fail(MendixApiOutcome outcome, string error, TimeSpan? retryAfter = null) =>
        new() { Outcome = outcome, Error = error, RetryAfter = retryAfter };

    public void Dispose()
    {
        Content?.Dispose();
        _response?.Dispose();
    }
}

// ── RAW DTOs — property names/casing match the live payloads captured in the MT-01 spike ──

/// <summary>Deploy v1 apps object: <c>AppId</c> / <c>Name</c> / <c>ProjectId</c> / <c>Url</c> (PascalCase).</summary>
public sealed class MendixAppRaw
{
    [JsonPropertyName("AppId")] public string? AppId { get; set; }
    [JsonPropertyName("Name")] public string? Name { get; set; }
    [JsonPropertyName("ProjectId")] public string? ProjectId { get; set; }
    [JsonPropertyName("Url")] public string? Url { get; set; }
}

/// <summary>
/// Deploy v1 environment object (PascalCase). Version/runtime fields are nullable because the
/// sandbox payload omits <c>ModelVersion</c>/<c>MendixVersion</c>/<c>RuntimeLayer</c> entirely.
/// </summary>
public sealed class MendixEnvironmentRaw
{
    [JsonPropertyName("EnvironmentId")] public string? EnvironmentId { get; set; }
    [JsonPropertyName("Url")] public string? Url { get; set; }
    [JsonPropertyName("Mode")] public string? Mode { get; set; }
    [JsonPropertyName("Status")] public string? Status { get; set; }
    [JsonPropertyName("ModelVersion")] public string? ModelVersion { get; set; }
    [JsonPropertyName("MendixVersion")] public string? MendixVersion { get; set; }
    [JsonPropertyName("Production")] public bool Production { get; set; }
    [JsonPropertyName("Instances")] public int Instances { get; set; }
    [JsonPropertyName("MemoryPerInstance")] public int MemoryPerInstance { get; set; }
    [JsonPropertyName("TotalMemory")] public int TotalMemory { get; set; }
    [JsonPropertyName("RuntimeLayer")] public string? RuntimeLayer { get; set; }
}

/// <summary>Backups v2 snapshots response envelope: <c>{ total, snapshots[] }</c> (snake_case).</summary>
public sealed class SnapshotsResponseRaw
{
    [JsonPropertyName("total")] public int Total { get; set; }
    [JsonPropertyName("snapshots")] public List<SnapshotRaw>? Snapshots { get; set; }
}

/// <summary>
/// Backups v2 snapshot (snake_case). <c>model_version</c> is absent on failed snapshots
/// (nullable). There is NO size and NO type field — confirmed absent in the MT-01 live run;
/// MT-14 derives Type from <c>comment</c> and cuts the Size column.
/// </summary>
public sealed class SnapshotRaw
{
    [JsonPropertyName("snapshot_id")] public string? SnapshotId { get; set; }
    [JsonPropertyName("model_version")] public string? ModelVersion { get; set; }
    [JsonPropertyName("comment")] public string? Comment { get; set; }
    [JsonPropertyName("expires_at")] public DateTimeOffset? ExpiresAt { get; set; }
    [JsonPropertyName("state")] public string? State { get; set; }
    [JsonPropertyName("status_message")] public string? StatusMessage { get; set; }
    [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; set; }
    [JsonPropertyName("finished_at")] public DateTimeOffset? FinishedAt { get; set; }
    [JsonPropertyName("updated_at")] public DateTimeOffset? UpdatedAt { get; set; }
}
