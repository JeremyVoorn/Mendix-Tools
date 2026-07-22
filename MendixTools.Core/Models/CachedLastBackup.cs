namespace MendixTools.Core.Models;

/// <summary>
/// Cached "newest backup timestamp" per environment (MT-08 / N4; consumed by MT-10's lazy
/// fill and MT-20's cached path). The env payload carries no backup info — the only source
/// is one Backups-v2 snapshots call per environment (newest <c>created_at</c>). Caching it
/// here lets cards render the last-backup cell offline and avoids re-hitting the N+1 call
/// on every refresh. <see cref="LastBackupAt"/> is null when the environment has no
/// backups (sandboxes render "—").
/// </summary>
public sealed class CachedLastBackup
{
    /// <summary>Deploy-v1 <c>EnvironmentId</c> — the cache key.</summary>
    public required string EnvironmentId { get; set; }

    /// <summary>Newest snapshot <c>created_at</c>, or null when the environment has no backups.</summary>
    public DateTimeOffset? LastBackupAt { get; set; }

    /// <summary>When this value was fetched from the API (for staleness display).</summary>
    public DateTimeOffset FetchedAt { get; set; }
}
