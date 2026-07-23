namespace Mendix_Tools.Models;

// MT-14 — Backups list DTOs.
//
// Shaped to the LIVE-VERIFIED Backups API v2 snapshot payload (MT-01 spike,
// 2026-07-22, docs/spikes/MT-01-auth-model.md §4) so the wired MT-20 / MT-14 real
// Backups-v2 client reuses this exact shape without touching the page:
//
//   GET /api/v2/apps/{ProjectId}/environments/{EnvironmentId}/snapshots
//     -> { total, snapshots: [ { snapshot_id, model_version (ABSENT on failed),
//          comment, expires_at, state, status_message, created_at, finished_at,
//          updated_at } ] }
//
// CUT per the live run — never modelled, never faked: there is NO `size` field on a
// snapshot (size is knowable only at archive/download time via Content-Length, recorded
// locally by MT-16). Do not add a Size field back.
//
// There is also NO `type` (Automatic/Manual) field — Type is DERIVED from `comment`
// (see SnapshotType + Snapshot.Type below), exactly as the spike found `comment` reliably
// distinguishes snapshot origin.

/// <summary>
/// Backups API v2 snapshot state. Documented state machine:
/// <c>queued → running → completed | failed</c>. The live run observed
/// <c>completed</c> and <c>failed</c>; <c>queued</c>/<c>running</c> are kept for the
/// in-progress rows MT-15's create flow will produce.
/// </summary>
public enum SnapshotState
{
    Queued,
    Running,
    Completed,
    Failed,
}

/// <summary>
/// Snapshot origin, DERIVED from <c>comment</c> because the API carries no type field
/// (MT-01 live run §4). The two automatic phrasings are stable Mendix-generated strings;
/// anything else is a human-entered comment → Manual.
/// </summary>
public enum SnapshotType
{
    /// <summary>Nightly platform snapshot — comment "Automatically created nightly snapshot".</summary>
    Automatic,

    /// <summary>Snapshot taken by a CI/CD run — comment "Backup created by Mendix pipeline".</summary>
    Pipeline,

    /// <summary>Any other (human) comment — the comment text is shown alongside the badge.</summary>
    Manual,
}

/// <summary>
/// One Backups API v2 snapshot. <see cref="ModelVersion"/> is nullable because it is
/// ABSENT on failed snapshots (live run §4). <see cref="StatusMessage"/> carries the API's
/// failure reason for <c>failed</c> rows. <see cref="ExpiresAt"/> is displayed rather than
/// assumed (retention varies from ~1 to ~12 months across real data).
/// </summary>
public sealed record Snapshot
{
    /// <summary>Backups v2 <c>snapshot_id</c> — stable row identity for selection.</summary>
    public required string SnapshotId { get; init; }

    /// <summary>Backups v2 <c>comment</c>. Drives the derived <see cref="Type"/>; may be empty.</summary>
    public string Comment { get; init; } = string.Empty;

    /// <summary>Backups v2 <c>state</c>.</summary>
    public required SnapshotState State { get; init; }

    /// <summary>Backups v2 <c>status_message</c> — the failure reason on <c>failed</c> rows; null otherwise.</summary>
    public string? StatusMessage { get; init; }

    /// <summary>Backups v2 <c>model_version</c> (e.g. <c>1.8.82.e3c1a393</c>). Null on failed snapshots.</summary>
    public string? ModelVersion { get; init; }

    /// <summary>Backups v2 <c>created_at</c> — shown mono in the Created column.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Backups v2 <c>finished_at</c> — null while queued/running.</summary>
    public DateTimeOffset? FinishedAt { get; init; }

    /// <summary>Backups v2 <c>updated_at</c>.</summary>
    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>Backups v2 <c>expires_at</c> — shown mono in the Expires column; null if never set.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>
    /// Type DERIVED from <see cref="Comment"/> (there is no API type field): the stable
    /// nightly phrase → Automatic, the pipeline phrase → Pipeline, anything else → Manual.
    /// Matched case-insensitively and by prefix/contains so minor phrasing drift still maps.
    /// </summary>
    public SnapshotType Type => DeriveType(Comment);

    /// <summary>True only for <c>completed</c> snapshots — the only rows that get (later-wired)
    /// Restore/Download actions. Failed/queued/running rows have no actions (MT-14 AC).</summary>
    public bool HasActions => State == SnapshotState.Completed;

    /// <summary>
    /// Derives <see cref="SnapshotType"/> from a snapshot comment. Public + static so the
    /// real MT-20 client and unit tests can reuse the exact same heuristic.
    /// </summary>
    public static SnapshotType DeriveType(string? comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
        {
            return SnapshotType.Manual;
        }

        if (comment.StartsWith("Automatically created", StringComparison.OrdinalIgnoreCase))
        {
            return SnapshotType.Automatic;
        }

        if (comment.Contains("pipeline", StringComparison.OrdinalIgnoreCase))
        {
            return SnapshotType.Pipeline;
        }

        return SnapshotType.Manual;
    }
}

/// <summary>
/// MT-16 — the result of a completed archive download: where the file landed and its actual
/// size in bytes (the only size source — the snapshots API exposes none). Feeds MT-17's restore
/// (the file to <c>pg_restore</c>) and provenance recording.
/// </summary>
/// <param name="FilePath">Absolute path to the downloaded, integrity-verified archive.</param>
/// <param name="SizeBytes">The archive's actual size on disk in bytes.</param>
public sealed record BackupDownload(string FilePath, long SizeBytes);

/// <summary>
/// One page of the paginated Backups API v2 snapshots response (<c>{ total, snapshots[] }</c>).
/// <see cref="Total"/> is the server-side total across all pages; <see cref="Snapshots"/> is
/// the fetched page. The page shows <see cref="Total"/> so nothing is silently truncated
/// (MT-14 AC: pagination/load-more driven by <c>total</c>).
/// </summary>
public sealed record BackupListResult(int Total, IReadOnlyList<Snapshot> Snapshots)
{
    /// <summary>An empty page — used for environments with no backups (sandboxes, fresh envs).</summary>
    public static readonly BackupListResult Empty = new(0, []);
}
