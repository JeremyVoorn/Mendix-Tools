namespace MendixTools.Core.Models;

/// <summary>
/// Provenance of a local database that this app restored from a cloud snapshot (MT-08 /
/// N4; feeds MT-17's restore flow and the future Local Databases screen X1). Answers
/// "where did <c>acme_local</c> come from?" — something Sprintr cannot.
/// </summary>
public sealed class RestoredDatabase
{
    /// <summary>Local store primary key (0 until inserted; set by <see cref="Metadata.IMetadataStore"/>).</summary>
    public long Id { get; set; }

    /// <summary>Target local Postgres database name the snapshot was imported into, e.g. <c>acme_local</c>.</summary>
    public required string TargetDatabaseName { get; set; }

    /// <summary>Source app — the Mendix app name or <c>AppId</c> the snapshot came from.</summary>
    public required string SourceApp { get; set; }

    /// <summary>Source environment id (Deploy-v1 <c>EnvironmentId</c>) the snapshot came from.</summary>
    public required string SourceEnvironmentId { get; set; }

    /// <summary>Source snapshot id (Backups-v2 <c>snapshot_id</c>), when known.</summary>
    public string? SnapshotId { get; set; }

    /// <summary>The snapshot's own timestamp (Backups-v2 <c>created_at</c>).</summary>
    public DateTimeOffset? SnapshotTimestamp { get; set; }

    /// <summary>Mendix version of the source (nullable — sandboxes report none, per MT-01).</summary>
    public string? MendixVersion { get; set; }

    /// <summary>Archive/restored size in bytes — the API exposes no size, so this is the
    /// locally-recorded truth (see also <see cref="Metadata.IMetadataStore.RecordSnapshotSizeAsync"/>).</summary>
    public long? SizeBytes { get; set; }

    /// <summary>When the restore completed locally.</summary>
    public DateTimeOffset RestoredAt { get; set; }

    /// <summary>Outcome of the restore. A partially-restored DB is marked <see cref="RestoreStatus.Failed"/>
    /// so no silent half-restore is ever presented as good (MT-17 AC).</summary>
    public RestoreStatus Status { get; set; } = RestoreStatus.Succeeded;
}

/// <summary>Terminal outcome recorded against a <see cref="RestoredDatabase"/>.</summary>
public enum RestoreStatus
{
    Succeeded = 0,
    Failed = 1,
}
