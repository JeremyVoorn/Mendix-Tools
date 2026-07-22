using MendixTools.Core.Jobs;

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
    // Safety cap so a stuck server-side snapshot can't spin forever. With the default 5s poll this
    // bounds create polling to ~50 min; tests inject a zero interval so they resolve fast.
    private const int MaxPolls = 600;
    private const int MaxRateLimitRetries = 6;

    private readonly IJobEngine _jobs;
    private readonly IMendixApiClient _api;
    private readonly IUserNotifier _notifier;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _pollInterval;

    public BackupJobs(
        IJobEngine jobs,
        IMendixApiClient api,
        IUserNotifier notifier,
        TimeProvider? clock = null,
        TimeSpan? pollInterval = null)
    {
        _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _notifier = notifier ?? throw new ArgumentNullException(nameof(notifier));
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
        var job = _jobs.Start("create-backup", async (ctx, ct) =>
        {
            ctx.BeginPhase("Requesting backup");
            ctx.ReportIndeterminate();
            ctx.LogInfo($"Requesting a snapshot for {environmentLabel}.");

            var create = await WithRateLimitRetry(ctx,
                token => _api.CreateSnapshotAsync(projectId, environmentId, comment, token), ct)
                .ConfigureAwait(false);

            if (!create.IsSuccess || create.Value?.SnapshotId is not { Length: > 0 } snapshotId)
            {
                throw Fail(create.Outcome, "requesting the snapshot");
            }

            ctx.LogInfo($"Snapshot {snapshotId} accepted (state: {create.Value.State ?? "queued"}).");

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

    private void AttachTerminalToast(Job job, Func<ToastText> success)
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
                    _notifier.Error("Backup failed", job.Message);
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
}
