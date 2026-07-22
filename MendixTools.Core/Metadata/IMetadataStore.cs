using MendixTools.Core.Models;

namespace MendixTools.Core.Metadata;

/// <summary>
/// The app's local memory (MT-08 / N4) — the thing Sprintr does not give a consultant:
/// restored-DB provenance, background-job history, and cached cloud state for the
/// stale/offline path. UI-agnostic (vision principle 7): no MAUI/Blazor types cross this
/// seam, so the same store backs a future CLI. Register once as a singleton; call
/// <see cref="InitializeAsync"/> at startup.
/// </summary>
public interface IMetadataStore
{
    /// <summary>Creates the database file if needed and runs pending migrations. Idempotent.</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    // ── Restored-database provenance ──────────────────────────────────────────────

    /// <summary>Inserts a provenance record; returns the assigned id (also set on <paramref name="record"/>).</summary>
    Task<long> AddRestoredDatabaseAsync(RestoredDatabase record, CancellationToken cancellationToken = default);

    /// <summary>Reads one provenance record by id, or null if not found.</summary>
    Task<RestoredDatabase?> GetRestoredDatabaseAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>All provenance records, newest restore first.</summary>
    Task<IReadOnlyList<RestoredDatabase>> GetRestoredDatabasesAsync(CancellationToken cancellationToken = default);

    /// <summary>Updates an existing provenance record (matched by <see cref="RestoredDatabase.Id"/>).</summary>
    Task UpdateRestoredDatabaseAsync(RestoredDatabase record, CancellationToken cancellationToken = default);

    // ── Job history ───────────────────────────────────────────────────────────────

    /// <summary>Inserts a job-history record; returns the assigned id.</summary>
    Task<long> AddJobAsync(JobHistoryEntry entry, CancellationToken cancellationToken = default);

    /// <summary>All job-history records, newest first.</summary>
    Task<IReadOnlyList<JobHistoryEntry>> GetJobsAsync(CancellationToken cancellationToken = default);

    // ── Cached environment state (stale/offline) ──────────────────────────────────

    /// <summary>Inserts or replaces the cached payload for an environment.</summary>
    Task CacheEnvironmentStateAsync(CachedEnvironmentState state, CancellationToken cancellationToken = default);

    /// <summary>Reads the cached payload for an environment, or null if none.</summary>
    Task<CachedEnvironmentState?> GetEnvironmentStateAsync(string environmentId, CancellationToken cancellationToken = default);

    // ── Cached last-backup timestamp per environment ──────────────────────────────

    /// <summary>Inserts or replaces the cached newest-backup timestamp for an environment
    /// (<paramref name="lastBackupAt"/> null = no backups, e.g. a sandbox).</summary>
    Task CacheLastBackupAsync(string environmentId, DateTimeOffset? lastBackupAt, DateTimeOffset fetchedAt, CancellationToken cancellationToken = default);

    /// <summary>Reads the cached last-backup entry for an environment, or null if never cached.</summary>
    Task<CachedLastBackup?> GetLastBackupAsync(string environmentId, CancellationToken cancellationToken = default);

    // ── Snapshot sizes (the API exposes none) ─────────────────────────────────────

    /// <summary>Records (insert or replace) the locally-observed archive size for a snapshot.</summary>
    Task RecordSnapshotSizeAsync(string snapshotId, long sizeBytes, DateTimeOffset recordedAt, CancellationToken cancellationToken = default);

    /// <summary>Reads the recorded size for a snapshot in bytes, or null if unknown.</summary>
    Task<long?> GetSnapshotSizeAsync(string snapshotId, CancellationToken cancellationToken = default);
}
