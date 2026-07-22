using Mendix_Tools.Models;

namespace Mendix_Tools.Services;

/// <summary>
/// MT-14 — the real <see cref="IBackupService"/> that drops into the mock's seam behind the
/// SAME interface, so <c>Components/Pages/Backups.razor</c> needs no structural change. Backed
/// by the shared <see cref="IMendixApiClient"/> (Backups API v2), reusing the exact client
/// MT-20's Environments dashboard already uses.
///
/// Contract translation (the page + its <c>MapError</c> expect the service to THROW on
/// failure and return data on success). The client never throws and returns a typed
/// <see cref="MendixApiOutcome"/>; this service maps those outcomes to the exception/result
/// shapes the page understands:
///   • Success        → a populated <see cref="BackupListResult"/> (raw snapshots mapped to <see cref="Snapshot"/>).
///   • Unauthorized/Forbidden → <see cref="UnauthorizedAccessException"/> → MapError →
///     "Credential rejected — check Settings › Credentials." (the first real cloud call
///     surfaces a bad/rejected key here — MT-13 accepted deviation, MT-14 AC).
///   • NetworkError (transport OR timeout) → <see cref="HttpRequestException"/> → MapError →
///     "Couldn't reach the Mendix API…".
///   • RateLimited    → <see cref="HttpRequestException"/> too — a 429 on a one-shot list is
///     transient; Refresh retries. (MT-16 does in-job backoff for the high-volume download
///     flow; the list does not need it.)
///   • InvalidResponse → a generic <see cref="InvalidOperationException"/> → MapError's
///     default → "Something went wrong loading this environment…".
///   • NoCredentials  → a clean empty result (see the note on <see cref="GetSnapshotsAsync"/>).
///
/// No secret ever appears in a thrown message: MapError keys off the exception TYPE, and the
/// messages passed here carry no credential text.
/// </summary>
public sealed class RealBackupService : IBackupService
{
    private readonly IMendixApiClient _api;

    public RealBackupService(IMendixApiClient api) => _api = api;

    public async Task<BackupListResult> GetSnapshotsAsync(
        string projectId, string environmentId, CancellationToken ct = default)
    {
        // TODO(MT-20b): the response carries `total`; when it exceeds this page, add
        // load-more/pagination (offset/limit) driven by total. This slice fetches page 1 only.
        var result = await _api.GetSnapshotsAsync(projectId, environmentId, limit: null, ct)
            .ConfigureAwait(false);

        switch (result.Outcome)
        {
            case MendixApiOutcome.Success:
                var raw = result.Value;
                if (raw is null)
                {
                    // Success with an unreadable/empty envelope — treat as a generic failure.
                    throw new InvalidOperationException("Couldn't read the backup list from the Mendix API.");
                }

                var snapshots = (raw.Snapshots ?? [])
                    .Select(MapSnapshot)
                    .ToList();
                return new BackupListResult(raw.Total, snapshots);

            case MendixApiOutcome.NoCredentials:
                // Best UX: a clean empty result, NOT a scary "credential rejected". This path is
                // effectively unreachable from the page — the environment Select is populated
                // from IEnvironmentService, which returns NoCredentials with zero apps, so no
                // environment is selectable and no snapshot call is made. Returning empty keeps
                // the defensive path calm rather than surfacing a misleading error.
                return BackupListResult.Empty;

            case MendixApiOutcome.Unauthorized:
            case MendixApiOutcome.Forbidden:
                // MapError maps this type to "Credential rejected — check Settings › Credentials."
                throw new UnauthorizedAccessException("The Mendix credential was rejected.");

            case MendixApiOutcome.NetworkError:
                // Covers transport failures AND timeouts (the client folds both into NetworkError).
                throw new HttpRequestException("Could not reach the Mendix Backups API.");

            case MendixApiOutcome.RateLimited:
                throw new HttpRequestException("The Mendix Backups API is rate limiting; try again shortly.");

            case MendixApiOutcome.InvalidResponse:
            default:
                throw new InvalidOperationException("Couldn't read the backup list from the Mendix API.");
        }
    }

    public async Task<Snapshot> CreateBackupAsync(
        string projectId, string environmentId, string? comment = null, CancellationToken ct = default)
    {
        var result = await _api.CreateSnapshotAsync(projectId, environmentId, comment, ct).ConfigureAwait(false);

        switch (result.Outcome)
        {
            case MendixApiOutcome.Success:
                if (result.Value is null)
                {
                    throw new InvalidOperationException("Couldn't read the created snapshot from the Mendix API.");
                }

                return MapSnapshot(result.Value);

            case MendixApiOutcome.Unauthorized:
            case MendixApiOutcome.Forbidden:
                throw new UnauthorizedAccessException("The Mendix credential was rejected.");

            case MendixApiOutcome.NetworkError:
                throw new HttpRequestException("Could not reach the Mendix Backups API.");

            case MendixApiOutcome.RateLimited:
                throw new HttpRequestException("The Mendix Backups API is rate limiting; try again shortly.");

            case MendixApiOutcome.NoCredentials:
                // Unreachable from the wired screen (no environment is selectable without a
                // credential); treat defensively as a credential problem rather than a crash.
                throw new UnauthorizedAccessException("No Mendix credential is configured.");

            case MendixApiOutcome.InvalidResponse:
            default:
                throw new InvalidOperationException("Couldn't create a snapshot via the Mendix API.");
        }
    }

    /// <summary>
    /// Maps a raw Backups v2 snapshot to the app model. All fields line up 1:1 with the
    /// live-verified shape (MT-01 §4); Type is derived from the comment via
    /// <see cref="Snapshot.DeriveType"/> (no API type field), state string → enum, and
    /// <c>status_message</c> is normalised to null when blank (the API sends "" on success).
    /// </summary>
    private static Snapshot MapSnapshot(SnapshotRaw raw) => new()
    {
        SnapshotId = raw.SnapshotId ?? string.Empty,
        Comment = raw.Comment ?? string.Empty,
        State = MapState(raw.State),
        StatusMessage = string.IsNullOrWhiteSpace(raw.StatusMessage) ? null : raw.StatusMessage,
        ModelVersion = raw.ModelVersion, // absent on failed snapshots (already null)
        CreatedAt = raw.CreatedAt ?? default,
        FinishedAt = raw.FinishedAt,
        UpdatedAt = raw.UpdatedAt,
        ExpiresAt = raw.ExpiresAt,
    };

    /// <summary>
    /// Maps the Backups v2 <c>state</c> string to the enum. Documented machine:
    /// <c>queued → running → completed | failed</c>. Anything unexpected falls back to
    /// <see cref="SnapshotState.Queued"/> — a conservative default that never fabricates a
    /// restorable "Completed" and never marks a healthy row "Failed".
    /// </summary>
    private static SnapshotState MapState(string? state) => state?.Trim().ToLowerInvariant() switch
    {
        "completed" => SnapshotState.Completed,
        "failed" => SnapshotState.Failed,
        "running" => SnapshotState.Running,
        "queued" => SnapshotState.Queued,
        _ => SnapshotState.Queued,
    };
}
