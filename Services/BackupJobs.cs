using MendixTools.Core.Integrity;
using MendixTools.Core.Jobs;
using MendixTools.Core.Metadata;

namespace Mendix_Tools.Services;

/// <summary>
/// MT-15 — the Backups screen's job orchestrator. Turns a "Create backup" click into an
/// <see cref="IJobEngine"/> job so it runs cancellably, reports phase + progress into the
/// active-job card, survives navigation, and persists a terminal row to job_history.
/// UI-agnostic (uses the typed <see cref="IMendixApiClient"/>, a toast seam and the job engine
/// only — no MAUI/Blazor types), so the orchestration is unit-tested in MendixTools.Core.Tests
/// with fakes and NO live Mendix API call is ever made.
///
/// create-backup state machine:
///   POST snapshot → poll GetSnapshots until the new snapshot reaches completed/failed.
///
/// The client NEVER throws (typed <see cref="MendixApiResult{T}"/>); this orchestrator converts a
/// failing outcome into a thrown, user-safe message so the job ends Failed with that message —
/// which the failure toast then states verbatim. No secret ever appears in a message or log.
///
/// MT-16 (download) extends this class next with the archive request→poll→stream→verify flow.
/// </summary>
public sealed class BackupJobs
{
    // Safety cap so a stuck server-side snapshot/archive can't spin forever. With the default 5s poll
    // this bounds polling to ~50 min; tests inject a zero interval so they resolve fast.
    private const int MaxPolls = 600;
    private const int MaxRateLimitRetries = 6;

    // MT-15 hardening — the provenance comment stamped on a snapshot created from this app when the
    // caller supplies none. Also the marker the create job matches on when the create POST response
    // carries no snapshot_id (unverified live shape — may be 202/empty).
    private const string DefaultCreateComment = "Created from Mendix Tools";

    // Clock skew tolerance when matching a just-created snapshot by created_at (MT-15 hardening).
    private static readonly TimeSpan CreatedAtTolerance = TimeSpan.FromMinutes(2);

    // Streamed copy buffer for the download (never buffer the whole archive in memory).
    private const int DownloadBufferSize = 81920;

    private readonly IJobEngine _jobs;
    private readonly IMendixApiClient _api;
    private readonly IUserNotifier _notifier;
    private readonly IMetadataStore? _store;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _pollInterval;

    public BackupJobs(
        IJobEngine jobs,
        IMendixApiClient api,
        IUserNotifier notifier,
        TimeProvider? clock = null,
        TimeSpan? pollInterval = null,
        IMetadataStore? store = null)
    {
        _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _notifier = notifier ?? throw new ArgumentNullException(nameof(notifier));
        _store = store; // optional: MT-16 records the downloaded size here (the only size source).
        _clock = clock ?? TimeProvider.System;
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(5);
    }

    /// <summary>Requests cooperative cancellation of a running job (stops polling → the job ends
    /// Cancelled). Passthrough to the engine so the page needs only this seam.</summary>
    public bool Cancel(Guid jobId) => _jobs.Cancel(jobId);

    // ── MT-15 — create backup ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Starts a create-backup job for one environment: POST a snapshot, then poll until it is
    /// Available or Failed. On success a "Backup created for {env}." toast fires and the caller
    /// refreshes the list; on failure a danger toast states the cause. The action only ever
    /// targets the <paramref name="projectId"/>/<paramref name="environmentId"/> passed in — the
    /// one the page has selected on screen.
    /// </summary>
    public Job StartCreateBackup(string projectId, string environmentId, string environmentLabel, string? comment = null)
    {
        // MT-15 hardening: always stamp a provenance comment (never send {}); this doubles as the
        // marker we match on if the create POST response carries no snapshot_id.
        var effectiveComment = string.IsNullOrWhiteSpace(comment) ? DefaultCreateComment : comment!;

        var job = _jobs.Start("create-backup", async (ctx, ct) =>
        {
            ctx.BeginPhase("Requesting backup");
            ctx.ReportIndeterminate();
            ctx.LogInfo($"Requesting a snapshot for {environmentLabel}.");
            var requestedAt = _clock.GetUtcNow();

            var create = await WithRateLimitRetry(ctx,
                token => _api.CreateSnapshotAsync(projectId, environmentId, effectiveComment, token), ct)
                .ConfigureAwait(false);

            if (!create.IsSuccess)
            {
                throw Fail(create.Outcome, "requesting the snapshot");
            }

            // MT-15 hardening: the live create response shape is unverified — it may be 202/empty
            // with NO snapshot_id. Use the id when the body carries one; otherwise identify the new
            // snapshot by matching our provenance comment among snapshots created at/after the request.
            var snapshotId = create.Value?.SnapshotId;
            if (string.IsNullOrEmpty(snapshotId))
            {
                ctx.LogInfo("Create accepted without a snapshot id — identifying the new snapshot from the list.");
                snapshotId = await ResolveNewSnapshotIdAsync(ctx, projectId, environmentId, effectiveComment, requestedAt, ct)
                    .ConfigureAwait(false);
            }

            ctx.LogInfo($"Snapshot {snapshotId} accepted (state: {create.Value?.State ?? "queued"}).");

            ctx.BeginPhase("Waiting for Mendix to finish");
            ctx.ReportIndeterminate();
            await PollSnapshotToTerminalAsync(ctx, projectId, environmentId, snapshotId, ct).ConfigureAwait(false);

            ctx.BeginPhase("Done");
            ctx.ReportProgress(100);
            ctx.LogInfo($"Snapshot {snapshotId} is available.");
        });

        AttachTerminalToast(job, () => new ToastText($"Backup created for {environmentLabel}.", null));
        return job;
    }

    /// <summary>
    /// MT-15 hardening — resolves the id of a just-created snapshot when the create POST returned no
    /// <c>snapshot_id</c>: polls the list for the newest snapshot whose comment matches the one we
    /// sent and whose <c>created_at</c> is at/after the request time (with a small skew tolerance).
    /// </summary>
    private async Task<string> ResolveNewSnapshotIdAsync(
        IJobContext ctx, string projectId, string environmentId, string comment, DateTimeOffset requestedAt, CancellationToken ct)
    {
        for (var poll = 0; poll < MaxPolls; poll++)
        {
            ct.ThrowIfCancellationRequested();

            var list = await WithRateLimitRetry(ctx,
                token => _api.GetSnapshotsAsync(projectId, environmentId, limit: null, token), ct)
                .ConfigureAwait(false);

            if (!list.IsSuccess)
            {
                throw Fail(list.Outcome, "identifying the new snapshot");
            }

            var match = list.Value?.Snapshots?
                .Where(s => !string.IsNullOrEmpty(s.SnapshotId)
                            && string.Equals(s.Comment, comment, StringComparison.Ordinal)
                            && (s.CreatedAt is null || s.CreatedAt >= requestedAt - CreatedAtTolerance))
                .OrderByDescending(s => s.CreatedAt ?? DateTimeOffset.MinValue)
                .FirstOrDefault();

            if (match?.SnapshotId is { Length: > 0 } id)
            {
                return id;
            }

            await DelayAsync(_pollInterval, ct).ConfigureAwait(false);
        }

        throw new TimeoutException(
            "Timed out waiting for the new snapshot to appear. Check the environment in Sprintr and try again.");
    }

    private async Task PollSnapshotToTerminalAsync(
        IJobContext ctx, string projectId, string environmentId, string snapshotId, CancellationToken ct)
    {
        for (var poll = 0; poll < MaxPolls; poll++)
        {
            ct.ThrowIfCancellationRequested();

            var list = await WithRateLimitRetry(ctx,
                token => _api.GetSnapshotsAsync(projectId, environmentId, limit: null, token), ct)
                .ConfigureAwait(false);

            if (!list.IsSuccess)
            {
                throw Fail(list.Outcome, "checking the snapshot state");
            }

            var snap = list.Value?.Snapshots?.FirstOrDefault(s =>
                string.Equals(s.SnapshotId, snapshotId, StringComparison.Ordinal));

            var state = snap?.State?.Trim().ToLowerInvariant();
            switch (state)
            {
                case "completed":
                    return;
                case "failed":
                    var reason = string.IsNullOrWhiteSpace(snap!.StatusMessage) ? "no reason given" : snap.StatusMessage;
                    throw new InvalidOperationException($"Mendix reported the snapshot failed: {reason}.");
            }

            await DelayAsync(_pollInterval, ct).ConfigureAwait(false);
        }

        throw new TimeoutException("Timed out waiting for the snapshot to complete. Check the environment in Sprintr and try again.");
    }

    // ── MT-16 — download archive ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Starts a download job for one completed snapshot: request a <c>database_only</c> archive →
    /// wait for its (8-hour) download URL → stream it to <paramref name="options"/>.DataDirectory →
    /// verify local integrity → record the actual size. Progress is determinate from Content-Length
    /// when present, indeterminate otherwise. On any failure or cancellation the partial file is
    /// removed; on success a toast states the file path + human-readable size. The action only ever
    /// targets the snapshot/environment passed in.
    /// </summary>
    public Job StartDownload(
        string projectId, string environmentId, string snapshotId, string environmentLabel, DownloadOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        DownloadSummary? summary = null;

        var job = _jobs.Start("download", async (ctx, ct) =>
        {
            Directory.CreateDirectory(options.DataDirectory);
            var finalPath = Path.Combine(options.DataDirectory, BuildFileName(snapshotId));
            var partialPath = finalPath + ".part";
            SafeDelete(partialPath);

            try
            {
                var url = await AcquireArchiveUrlAsync(ctx, projectId, environmentId, snapshotId, ct).ConfigureAwait(false);

                long? contentLength;
                var reRequested = false;
                while (true)
                {
                    ctx.BeginPhase("Downloading");
                    ctx.ReportIndeterminate();

                    using var download = await OpenDownloadWithRateLimitAsync(ctx, url, ct).ConfigureAwait(false);

                    if (download.LinkExpired)
                    {
                        if (reRequested)
                        {
                            throw new InvalidOperationException(
                                "The archive download link expired and a fresh link could not be obtained. Start the download again.");
                        }

                        reRequested = true;
                        ctx.LogWarning("Archive download link expired (8-hour window) — requesting a fresh archive.");
                        url = await AcquireArchiveUrlAsync(ctx, projectId, environmentId, snapshotId, ct).ConfigureAwait(false);
                        continue;
                    }

                    if (!download.IsSuccess)
                    {
                        throw Fail(download.Outcome, "downloading the archive");
                    }

                    contentLength = download.ContentLength;
                    await StreamToFileAsync(ctx, download.Content!, contentLength, partialPath, ct).ConfigureAwait(false);
                    break;
                }

                // Verify (local integrity is the ONLY correctness mechanism — no API checksum, D4).
                ctx.BeginPhase("Verifying");
                ctx.ReportIndeterminate();
                VerifyOrThrow(ctx, partialPath, contentLength, options.VerifyIntegrity, ct);

                // Atomically move the verified file into place (overwrite a stale prior download).
                File.Move(partialPath, finalPath, overwrite: true);

                var size = new FileInfo(finalPath).Length;

                // Record the actual size — the snapshots API exposes none, so this local record is
                // the ONLY size source (also feeds MT-17 provenance).
                if (_store is not null)
                {
                    await _store.RecordSnapshotSizeAsync(snapshotId, size, _clock.GetUtcNow(), ct).ConfigureAwait(false);
                }

                ctx.LogInfo($"Recorded downloaded size {FormatSize(size)} for snapshot {snapshotId}.");
                ctx.LogInfo($"Archive saved to {finalPath}.");
                if (!options.KeepFile)
                {
                    // MT-16 keeps the downloaded artifact (it IS the deliverable); the keep-file
                    // preference governs deletion AFTER a restore consumes it (MT-17).
                    ctx.LogInfo("Keep-file preference is off — the file is retained now and removed by the restore flow (MT-17).");
                }

                ctx.BeginPhase("Done");
                ctx.ReportProgress(100);

                summary = new DownloadSummary(finalPath, size);
            }
            catch
            {
                // Failure OR cancellation → never leave a partial behind. The final file (if the
                // move already succeeded) is intentionally kept.
                SafeDelete(partialPath);
                throw;
            }
        });

        AttachTerminalToast(
            job,
            () =>
            {
                var s = summary!.Value;
                return new ToastText($"Backup downloaded to {s.Path} ({FormatSize(s.Size)}).", null);
            },
            failureTitle: "Download failed");

        return job;
    }

    /// <summary>Requests a database-only archive and returns its download URL, polling until ready.</summary>
    private async Task<string> AcquireArchiveUrlAsync(
        IJobContext ctx, string projectId, string environmentId, string snapshotId, CancellationToken ct)
    {
        ctx.BeginPhase("Requesting archive");
        ctx.ReportIndeterminate();
        ctx.LogInfo($"Requesting a database-only archive for snapshot {snapshotId}.");

        var create = await WithRateLimitRetry(ctx,
            token => _api.CreateArchiveAsync(projectId, environmentId, snapshotId, "database_only", token), ct)
            .ConfigureAwait(false);

        if (!create.IsSuccess || create.Value?.ArchiveId is not { Length: > 0 } archiveId)
        {
            throw Fail(create.Outcome, "requesting the archive");
        }

        ctx.LogInfo($"Archive {archiveId} requested (state: {create.Value.State ?? "queued"}).");

        // Some responses may already be complete with the URL populated.
        if (IsCompleted(create.Value.State) && !string.IsNullOrWhiteSpace(create.Value.Url))
        {
            return create.Value.Url!;
        }

        ctx.BeginPhase("Waiting for archive");
        ctx.ReportIndeterminate();
        return await PollArchiveToUrlAsync(ctx, projectId, environmentId, snapshotId, archiveId, ct).ConfigureAwait(false);
    }

    private async Task<string> PollArchiveToUrlAsync(
        IJobContext ctx, string projectId, string environmentId, string snapshotId, string archiveId, CancellationToken ct)
    {
        for (var poll = 0; poll < MaxPolls; poll++)
        {
            ct.ThrowIfCancellationRequested();

            var get = await WithRateLimitRetry(ctx,
                token => _api.GetArchiveAsync(projectId, environmentId, snapshotId, archiveId, token), ct)
                .ConfigureAwait(false);

            if (!get.IsSuccess)
            {
                throw Fail(get.Outcome, "checking the archive state");
            }

            var archive = get.Value;
            var state = archive?.State?.Trim().ToLowerInvariant();
            switch (state)
            {
                case "completed":
                    if (!string.IsNullOrWhiteSpace(archive!.Url))
                    {
                        return archive.Url!;
                    }

                    throw new InvalidOperationException("Mendix reported the archive completed but returned no download link.");
                case "failed":
                    var reason = string.IsNullOrWhiteSpace(archive!.StatusMessage) ? "no reason given" : archive.StatusMessage;
                    throw new InvalidOperationException($"Mendix reported the archive failed: {reason}.");
            }

            await DelayAsync(_pollInterval, ct).ConfigureAwait(false);
        }

        throw new TimeoutException("Timed out waiting for the archive to be prepared. Try the download again.");
    }

    /// <summary>Opens the download, retrying on HTTP 429 (Retry-After honoured, bounded).</summary>
    private async Task<MendixArchiveDownload> OpenDownloadWithRateLimitAsync(IJobContext ctx, string url, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var download = await _api.OpenArchiveDownloadAsync(url, ct).ConfigureAwait(false);

            if (download.Outcome != MendixApiOutcome.RateLimited || attempt >= MaxRateLimitRetries)
            {
                return download;
            }

            var retryAfter = download.RetryAfter;
            download.Dispose();
            ctx.LogWarning("Mendix API rate limit — retrying.");
            await DelayAsync(retryAfter ?? _pollInterval, ct).ConfigureAwait(false);
        }
    }

    private async Task StreamToFileAsync(IJobContext ctx, Stream source, long? total, string partialPath, CancellationToken ct)
    {
        var hasTotal = total is > 0;
        if (hasTotal)
        {
            ctx.ReportProgress(0);
            ctx.LogInfo($"Downloading {FormatSize(total!.Value)}…");
        }
        else
        {
            ctx.ReportIndeterminate();
            ctx.LogInfo("Downloading (size not advertised — progress is indeterminate)…");
        }

        var buffer = new byte[DownloadBufferSize];
        long written = 0;
        var lastPercent = -1;

        await using var file = new FileStream(
            partialPath, FileMode.Create, FileAccess.Write, FileShare.None, DownloadBufferSize, useAsync: true);

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var n = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false);
            if (n == 0)
            {
                break;
            }

            await file.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
            written += n;

            if (hasTotal)
            {
                var percent = (int)(written * 100 / total!.Value);
                if (percent != lastPercent)
                {
                    ctx.ReportProgress(percent);
                    lastPercent = percent;
                }
            }
        }

        await file.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs the local integrity check. The Content-Length size match ALWAYS applies (truncation
    /// guard); the structural gzip/zip/pg_dump test runs when <paramref name="verifyIntegrity"/> is
    /// on (MT-12 preference). On failure the partial file is deleted and the job fails.
    /// </summary>
    private static void VerifyOrThrow(IJobContext ctx, string partialPath, long? contentLength, bool verifyIntegrity, CancellationToken ct)
    {
        var actual = new FileInfo(partialPath).Length;

        if (contentLength is { } expected && expected != actual)
        {
            ctx.LogError($"Size mismatch: expected {expected} bytes, got {actual}.");
            SafeDelete(partialPath);
            throw new InvalidOperationException(
                "The downloaded archive failed its integrity check (size mismatch). The partial file was removed — start the download again.");
        }

        if (!verifyIntegrity)
        {
            ctx.LogInfo("Structural integrity check skipped (disabled in Settings › Preferences); size verified.");
            return;
        }

        var result = ArchiveIntegrity.VerifyFile(partialPath, expectedContentLength: null, ct);
        if (!result.IsValid)
        {
            ctx.LogError($"Integrity check failed: {result.Detail}");
            SafeDelete(partialPath);
            throw new InvalidOperationException(
                "The downloaded archive failed its integrity check. The partial file was removed — start the download again.");
        }

        ctx.LogInfo($"Integrity check passed: {result.Detail}");
    }

    private static bool IsCompleted(string? state) =>
        string.Equals(state?.Trim(), "completed", StringComparison.OrdinalIgnoreCase);

    private static string BuildFileName(string snapshotId)
    {
        var safe = new string((snapshotId ?? string.Empty)
            .Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray());
        if (string.IsNullOrEmpty(safe))
        {
            safe = "archive";
        }

        return $"snapshot-{safe}.backup";
    }

    private static void SafeDelete(string path)
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
            // Best-effort cleanup — a locked/removed file must not mask the real job outcome.
        }
    }

    /// <summary>Formats a byte count as a short human-readable size ("2.4 GB").</summary>
    internal static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} B" : $"{size:0.0} {units[unit]}";
    }

    // ── Shared helpers ──────────────────────────────────────────────────────────────────────

    /// <summary>Retries a client call on HTTP 429, honouring Retry-After (bounded), logging each retry.</summary>
    private async Task<MendixApiResult<T>> WithRateLimitRetry<T>(
        IJobContext ctx, Func<CancellationToken, Task<MendixApiResult<T>>> call, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var result = await call(ct).ConfigureAwait(false);

            if (result.Outcome != MendixApiOutcome.RateLimited || attempt >= MaxRateLimitRetries)
            {
                return result;
            }

            ctx.LogWarning("Mendix API rate limit — retrying.");
            await DelayAsync(result.RetryAfter ?? _pollInterval, ct).ConfigureAwait(false);
        }
    }

    private Task DelayAsync(TimeSpan delay, CancellationToken ct) =>
        delay <= TimeSpan.Zero ? Task.CompletedTask : Task.Delay(delay, _clock, ct);

    /// <summary>Maps a failing client outcome to a thrown, user-safe message (becomes Job.Message
    /// and the failure-toast body). No secret ever appears here.</summary>
    private static Exception Fail(MendixApiOutcome outcome, string context) => outcome switch
    {
        MendixApiOutcome.Unauthorized or MendixApiOutcome.Forbidden =>
            new UnauthorizedAccessException("Credentials invalid — check Settings › Credentials."),
        MendixApiOutcome.NoCredentials =>
            new UnauthorizedAccessException("No Mendix credential is configured — add one in Settings › Credentials."),
        MendixApiOutcome.NetworkError =>
            new HttpRequestException("Couldn't reach the Mendix API. Check your connection, then try again."),
        MendixApiOutcome.RateLimited =>
            new HttpRequestException("Mendix API rate limit reached — try again shortly."),
        _ => new InvalidOperationException($"The Mendix API returned an unexpected response while {context}."),
    };

    private void AttachTerminalToast(Job job, Func<ToastText> success, string failureTitle = "Backup failed")
    {
        var fired = 0;
        void Fire()
        {
            if (Interlocked.Exchange(ref fired, 1) != 0)
            {
                return;
            }

            switch (job.State)
            {
                case JobState.Succeeded:
                    var text = success();
                    _notifier.Success(text.Title, text.Message);
                    break;
                case JobState.Failed:
                    _notifier.Error(failureTitle, job.Message);
                    break;
                // Cancelled is user-initiated — no toast.
            }
        }

        job.StateChanged += (_, e) =>
        {
            if (e.Current is JobState.Succeeded or JobState.Failed or JobState.Cancelled)
            {
                Fire();
            }
        };

        // Guard the race where an instant (fake) job reaches terminal before this subscription.
        if (job.IsTerminal)
        {
            Fire();
        }
    }

    private readonly record struct ToastText(string Title, string? Message);

    /// <summary>Captured after a successful download so the terminal toast can state path + size.</summary>
    private readonly record struct DownloadSummary(string Path, long Size);
}

/// <summary>
/// MT-16 — the inputs the download job needs from the app's settings, passed in by the caller
/// (Backups page reads them from <c>AppSettingsService</c>) so <see cref="BackupJobs"/> stays
/// MAUI-free and unit-testable with fakes.
/// </summary>
/// <param name="DataDirectory">Where the archive lands (created if missing) — MT-11 data directory.</param>
/// <param name="VerifyIntegrity">MT-12 "verify checksum after download" — runs the structural check.</param>
/// <param name="KeepFile">MT-12 "keep .backup file" — governs deletion after a later restore (MT-17).</param>
public sealed record DownloadOptions(string DataDirectory, bool VerifyIntegrity, bool KeepFile);
