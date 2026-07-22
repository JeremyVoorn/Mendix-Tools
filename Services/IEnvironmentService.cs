using Mendix_Tools.Models;

namespace Mendix_Tools.Services;

/// <summary>
/// MT-10 seam between the Environments dashboard and its data source. One mock
/// implementation (<see cref="MockEnvironmentService"/>) is registered today; MT-20 drops
/// in a real Deploy-v1 + Backups-v2 client behind the SAME interface with no page changes.
///
/// The interface is deliberately two calls, matching the live API shape (MT-01 spike):
///   (a) <see cref="GetAppsAsync"/>       — one <c>GET /api/1/apps</c> + one
///       <c>GET /api/1/apps/{AppId}/environments</c> per app; returns everything the card
///       renders synchronously (status, version, host, mode, production).
///   (b) <see cref="GetNewestBackupAsync"/> — the lazy, per-environment "last backup" cell
///       (vision N6). In MT-20 this maps to the single Backups-v2 snapshots call per env
///       (newest <c>created_at</c>) that MT-14 already makes — cards never block on it, so
///       it is a SEPARATE call the page fires per card and fills as each returns.
/// </summary>
public interface IEnvironmentService
{
    /// <summary>
    /// Lists every app the credential can see, each with its environments. Mirrors
    /// <c>GET /api/1/apps</c> (which mixes personal sandboxes with licensed apps) plus the
    /// per-app environments call. The dashboard groups sandboxes separately from this list.
    /// </summary>
    Task<IReadOnlyList<MendixApp>> GetAppsAsync(CancellationToken ct = default);

    /// <summary>
    /// Newest backup timestamp for one environment, or <c>null</c> when the environment has
    /// no backups (every sandbox — sandboxes are never backed up). Called once per card and
    /// awaited independently so the card renders immediately and its "Backup" cell fills
    /// when this returns. In MT-20 this is the newest <c>created_at</c> from the single
    /// Backups-v2 snapshots call per environment (cached via MT-08).
    /// </summary>
    /// <param name="projectId">The app's <c>ProjectId</c> GUID (the Backups API v2 key).</param>
    /// <param name="environmentId">The environment's <c>EnvironmentId</c>.</param>
    Task<DateTimeOffset?> GetNewestBackupAsync(string projectId, string environmentId, CancellationToken ct = default);
}
