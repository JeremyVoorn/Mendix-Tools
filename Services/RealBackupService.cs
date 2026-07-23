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

    public async Task<BackupDownload> DownloadArchiveAsync(
        string projectId, string environmentId, string snapshotId, string destinationDirectory,
        bool verifyIntegrity = true, CancellationToken ct = default)
    {
        Directory.CreateDirectory(destinationDirectory);
        var finalPath = Path.Combine(destinationDirectory, $"snapshot-{Sanitize(snapshotId)}.backup");
        var partialPath = finalPath + ".part";
        Delete(partialPath);

        try
        {
            var url = await AcquireArchiveUrlAsync(projectId, environmentId, snapshotId, ct).ConfigureAwait(false);

            long? contentLength;
            var reRequested = false;
            while (true)
            {
                using var download = await _api.OpenArchiveDownloadAsync(url, ct).ConfigureAwait(false);

                if (download.LinkExpired)
                {
                    if (reRequested)
                    {
                        throw new InvalidOperationException("The archive download link expired and could not be refreshed.");
                    }

                    reRequested = true;
                    url = await AcquireArchiveUrlAsync(projectId, environmentId, snapshotId, ct).ConfigureAwait(false);
                    continue;
                }

                if (!download.IsSuccess)
                {
                    throw Translate(download.Outcome, "downloading the archive");
                }

                contentLength = download.ContentLength;
                await using var file = new FileStream(partialPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                await download.Content!.CopyToAsync(file, ct).ConfigureAwait(false);
                await file.FlushAsync(ct).ConfigureAwait(false);
                break;
            }

            var actual = new FileInfo(partialPath).Length;
            if (contentLength is { } expected && expected != actual)
            {
                Delete(partialPath);
                throw new InvalidOperationException("The downloaded archive failed its integrity check (size mismatch).");
            }

            if (verifyIntegrity)
            {
                var integrity = MendixTools.Core.Integrity.ArchiveIntegrity.VerifyFile(partialPath, expectedContentLength: null, ct);
                if (!integrity.IsValid)
                {
                    Delete(partialPath);
                    throw new InvalidOperationException("The downloaded archive failed its integrity check.");
                }
            }

            File.Move(partialPath, finalPath, overwrite: true);
            return new BackupDownload(finalPath, new FileInfo(finalPath).Length);
        }
        catch
        {
            Delete(partialPath);
            throw;
        }
    }

    /// <summary>Requests a database-only archive and polls to its 8-hour download URL.</summary>
    private async Task<string> AcquireArchiveUrlAsync(string projectId, string environmentId, string snapshotId, CancellationToken ct)
    {
        var create = await _api.CreateArchiveAsync(projectId, environmentId, snapshotId, "database_only", ct).ConfigureAwait(false);
        if (!create.IsSuccess || create.Value?.ArchiveId is not { Length: > 0 } archiveId)
        {
            throw Translate(create.Outcome, "requesting the archive");
        }

        if (IsCompleted(create.Value.State) && !string.IsNullOrWhiteSpace(create.Value.Url))
        {
            return create.Value.Url!;
        }

        for (var poll = 0; poll < 600; poll++)
        {
            ct.ThrowIfCancellationRequested();
            var get = await _api.GetArchiveAsync(projectId, environmentId, snapshotId, archiveId, ct).ConfigureAwait(false);
            if (!get.IsSuccess)
            {
                throw Translate(get.Outcome, "checking the archive state");
            }

            var state = get.Value?.State?.Trim().ToLowerInvariant();
            if (state == "completed")
            {
                if (!string.IsNullOrWhiteSpace(get.Value!.Url))
                {
                    return get.Value.Url!;
                }

                throw new InvalidOperationException("The archive completed without a download link.");
            }

            if (state == "failed")
            {
                var reason = string.IsNullOrWhiteSpace(get.Value!.StatusMessage) ? "no reason given" : get.Value.StatusMessage;
                throw new InvalidOperationException($"The archive failed: {reason}.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), ct).ConfigureAwait(false);
        }

        throw new InvalidOperationException("Timed out waiting for the archive to be prepared.");
    }

    private static bool IsCompleted(string? state) =>
        string.Equals(state?.Trim(), "completed", StringComparison.OrdinalIgnoreCase);

    private static string Sanitize(string value)
    {
        var safe = new string((value ?? string.Empty)
            .Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray());
        return string.IsNullOrEmpty(safe) ? "archive" : safe;
    }

    private static void Delete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // best-effort
        }
    }

    /// <summary>Maps a failing client outcome to the exception TYPE the page/job contract expects.</summary>
    private static Exception Translate(MendixApiOutcome outcome, string context) => outcome switch
    {
        MendixApiOutcome.Unauthorized or MendixApiOutcome.Forbidden or MendixApiOutcome.NoCredentials =>
            new UnauthorizedAccessException("The Mendix credential was rejected."),
        MendixApiOutcome.NetworkError =>
            new HttpRequestException("Could not reach the Mendix Backups API."),
        MendixApiOutcome.RateLimited =>
            new HttpRequestException("The Mendix Backups API is rate limiting; try again shortly."),
        _ => new InvalidOperationException($"The Mendix API returned an unexpected response while {context}."),
    };

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
