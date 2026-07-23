using Mendix_Tools.Models;

namespace Mendix_Tools.Services;

/// <summary>
/// MT-14 seam between the Backups screen and its data source. One mock implementation
/// (<see cref="MockBackupService"/>) is registered today; MT-20's real Backups API v2
/// client drops in behind the SAME interface with no page changes — exactly the pattern
/// MT-10 used for <see cref="IEnvironmentService"/>.
///
/// The single call mirrors the live Backups-v2 shape (MT-01 spike):
///   <c>GET /api/v2/apps/{ProjectId}/environments/{EnvironmentId}/snapshots</c>
///     → <see cref="BackupListResult"/> (<c>{ total, snapshots[] }</c>, newest first).
///
/// ProjectId is the app's GUID (the Backups API v2 key — NOT the AppId subdomain string),
/// EnvironmentId is the environment's id; both come from <see cref="IEnvironmentService"/>.
/// This is the same per-environment snapshots call MT-10/MT-20's "last backup" cell makes
/// (newest <c>created_at</c>), so the real implementation can serve both from one client.
/// </summary>
public interface IBackupService
{
    /// <summary>
    /// Lists snapshots for one environment (one page, newest first). Implementations surface
    /// transport/auth failures as thrown exceptions so the page can render its failed-list
    /// state ("Credential rejected — check Settings › Credentials" etc.); an environment with
    /// no backups returns <see cref="BackupListResult.Empty"/>, never an error.
    /// </summary>
    /// <param name="projectId">The app's <c>ProjectId</c> GUID (the Backups API v2 key).</param>
    /// <param name="environmentId">The environment's <c>EnvironmentId</c>.</param>
    /// <param name="ct">Cancellation for reloads/navigation-away.</param>
    Task<BackupListResult> GetSnapshotsAsync(string projectId, string environmentId, CancellationToken ct = default);

    /// <summary>
    /// MT-15 — requests a fresh snapshot for one environment (Backups v2 <c>POST snapshots</c>) and
    /// returns it in its initial <c>queued</c>/<c>running</c> state; the caller polls
    /// <see cref="GetSnapshotsAsync"/> until it reaches Available/Failed. This is the one-shot
    /// service-level create (the Backups screen drives creation through a job via
    /// <c>BackupJobs</c>; MT-17/X5 "backup-before-deploy" compose this method directly).
    ///
    /// Same THROW-on-failure contract as <see cref="GetSnapshotsAsync"/> so callers map by
    /// exception type: <see cref="UnauthorizedAccessException"/> (401/403 → "Credentials invalid —
    /// check Settings › Credentials"), <see cref="HttpRequestException"/> (network/429),
    /// <see cref="InvalidOperationException"/> (invalid/empty response). Never leaks a secret.
    /// </summary>
    /// <param name="comment">Optional snapshot comment; blank/null sends an empty-comment create.</param>
    Task<Snapshot> CreateBackupAsync(string projectId, string environmentId, string? comment = null, CancellationToken ct = default);

    /// <summary>
    /// MT-16 — the composable, headless one-shot download: request a <c>database_only</c> archive
    /// for a completed snapshot, wait for its 8-hour download URL, stream it into
    /// <paramref name="destinationDirectory"/> (created if missing), verify local integrity, and
    /// return the file path + actual size. Re-requests a fresh archive once if the link expires.
    /// The Backups screen drives download through a job (<c>BackupJobs.StartDownload</c>) for
    /// progress/cancel; this method is the seam MT-17's restore flow composes directly.
    ///
    /// Same THROW-on-failure contract as <see cref="CreateBackupAsync"/> (map by exception TYPE):
    /// <see cref="UnauthorizedAccessException"/> (401/403), <see cref="HttpRequestException"/>
    /// (network/429), <see cref="InvalidOperationException"/> (server-side archive failure, integrity
    /// failure, invalid/empty response). On integrity failure the partial file is deleted. Never
    /// leaks a secret and never logs the (signed) download URL.
    /// </summary>
    /// <param name="destinationDirectory">Directory the archive is written to (MT-11 data directory).</param>
    /// <param name="verifyIntegrity">Run the structural integrity test (MT-12 preference); size is always checked.</param>
    Task<BackupDownload> DownloadArchiveAsync(
        string projectId, string environmentId, string snapshotId, string destinationDirectory,
        bool verifyIntegrity = true, CancellationToken ct = default);
}
