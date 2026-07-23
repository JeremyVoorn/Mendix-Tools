using MendixTools.Core.Integrity;
using MendixTools.Core.Jobs;
using MendixTools.Core.Metadata;
using MendixTools.Core.Models;

namespace Mendix_Tools.Services;

/// <summary>
/// MT-17 — the Restore screen's job orchestrator (the flagship "cloud backup → local Postgres"
/// engine). Turns a confirmed restore request into one <see cref="IJobEngine"/> job that runs
/// cancellably, reports phase + progress into the active-job card, survives navigation, and
/// persists a terminal row to <c>job_history</c>. UI-agnostic (vision principle 7): it depends on
/// the job engine, the <see cref="IRestoreRunner"/> exec seam, a toast seam and the metadata store
/// only — no MAUI/Blazor types — so the orchestration is unit-tested in MendixTools.Core.Tests with
/// a FAKE runner and NO real database is ever touched.
///
/// Phases (per BackupsScreen.jsx, clean-restore only — "Merge into existing" is CUT for v1, D5):
///   Preparing → Terminating connections → Dropping &amp; recreating {db} → Importing into {db}
///   → Verifying → Done.
///
/// SAFETY — the destructive drop/recreate is gated two ways that reinforce each other:
///   1. UI (MT-19): a Tier-2 typed-identifier guard before the job is ever started.
///   2. Engine (this class): <see cref="StartRestore"/> takes an explicit <c>confirmed</c> flag and
///      converts it into a <see cref="RestoreConfirmation"/> token. If unconfirmed the job fails
///      IMMEDIATELY with "restore not confirmed" and calls NO runner step — nothing is touched. The
///      destructive runner methods take a non-null token, so with nullable reference types on an
///      unconfirmed drop is a compile-time WARNING, not a hard error; the actual guarantee is the
///      runtime gate here — <see cref="RestoreConfirmation.ForConfirmed"/> returns null unless
///      <c>confirmed</c>, and the job then fails before any runner step runs.
///
/// Provenance is recorded on SUCCESS with <see cref="RestoreStatus.Succeeded"/>. If the drop has
/// BEGUN and the restore then fails or is cancelled, a provenance row is written with
/// <see cref="RestoreStatus.Failed"/> and an honest ERROR log line records that the target is left
/// dropped / half-imported and is NOT usable — the failed attempt is captured (MT-17 AC-5), so no
/// silent half-restore is ever presented as good. A failure BEFORE the drop (tool-not-found,
/// unreachable server, bad credentials, not-confirmed, missing archive) leaves the database
/// untouched and writes NO row.
/// </summary>
public sealed class RestoreJobs
{
    private readonly IJobEngine _jobs;
    private readonly IRestoreRunner _runner;
    private readonly IUserNotifier _notifier;
    private readonly IMetadataStore _store;
    private readonly TimeProvider _clock;

    public RestoreJobs(
        IJobEngine jobs,
        IRestoreRunner runner,
        IUserNotifier notifier,
        IMetadataStore store,
        TimeProvider? clock = null)
    {
        _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _notifier = notifier ?? throw new ArgumentNullException(nameof(notifier));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>Requests cooperative cancellation of a running restore (passthrough to the engine).
    /// A cancel BEFORE the drop leaves the target untouched; a cancel mid-import leaves it dropped /
    /// half-imported — the job log states which, honestly.</summary>
    public bool Cancel(Guid jobId) => _jobs.Cancel(jobId);

    /// <summary>
    /// Starts a clean-restore job: import <paramref name="archivePath"/> into local Postgres
    /// database <paramref name="targetDbName"/>, dropping and recreating it first.
    ///
    /// The destructive drop/recreate runs ONLY when <paramref name="confirmed"/> is true. MT-18 wires
    /// the restore dialog to call this with <c>confirmed</c> set from the MT-19 Tier-2 guard's
    /// pass/fail; when the target database does not exist the guard is not shown and the UI still
    /// passes <c>confirmed: true</c> (creating a fresh DB is not destructive — D5).
    /// </summary>
    /// <param name="archivePath">The downloaded, integrity-checked archive (MT-16).</param>
    /// <param name="targetDbName">Local database to (drop and) create and import into.</param>
    /// <param name="sourceSnapshotId">Source Backups-v2 snapshot id, for provenance (may be null).</param>
    /// <param name="sourceEnvLabel">Human label of the source, e.g. "Acme Insurance · Production".</param>
    /// <param name="confirmed">The destructive-step authorisation. False → the job fails at once.</param>
    /// <param name="keepFile">MT-12 preference: keep the .backup after a successful restore (default
    /// true); when false the archive is deleted once the import succeeds.</param>
    /// <param name="provenance">Optional richer provenance (source app / env id / version / snapshot
    /// timestamp) MT-18 supplies; when null, <paramref name="sourceEnvLabel"/> is used as the source
    /// app label.</param>
    public Job StartRestore(
        string archivePath,
        string targetDbName,
        string? sourceSnapshotId,
        string sourceEnvLabel,
        bool confirmed,
        bool keepFile = true,
        RestoreProvenanceInfo? provenance = null)
    {
        var job = _jobs.Start("restore", async (ctx, ct) =>
        {
            // ── SAFETY GATE (first thing, before any phase or side effect) ──────────────────────
            // Turn the caller's flag into a capability token. No token → we refuse to run ANY step.
            var confirmation = RestoreConfirmation.ForConfirmed(confirmed, targetDbName);
            if (confirmation is null)
            {
                ctx.LogError("Restore not confirmed — no connections were terminated and no database was dropped or changed.");
                throw new InvalidOperationException(
                    "Restore not confirmed — the destructive step was not authorised, so nothing was changed.");
            }

            // ── Preparing (non-destructive) ─────────────────────────────────────────────────────
            ctx.BeginPhase("Preparing");
            ctx.ReportIndeterminate();

            if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
            {
                throw new RestoreRunnerException(
                    RestoreFailureKind.ArchiveMissing,
                    "The backup archive could not be found on disk — download it again, then retry the restore.");
            }

            var format = ArchiveIntegrity.DetectFileFormat(archivePath, ct);
            var method = RestorePlanner.DecideImportMethod(format);
            var plan = new RestorePlan(archivePath, targetDbName, format, method);
            ctx.LogInfo($"Archive format: {format}; import via {(method == RestoreImportMethod.PgRestore ? "pg_restore" : "psql")}.");

            // Locate the client tool and check the server BEFORE the point of no return, so an absent
            // tool / unreachable server fails while the target database is still untouched.
            await _runner.EnsureClientToolAvailableAsync(plan, ctx, ct).ConfigureAwait(false);
            await _runner.VerifyServerAsync(plan, ctx, ct).ConfigureAwait(false);

            var destructiveStarted = false;
            try
            {
                // ── Terminating connections (token required, but NOT yet data-destroying) ────────
                // Terminating open backends frees the DB for the drop; it does not itself drop or
                // alter data, so if it fails the target is still intact — destructiveStarted stays
                // false until the drop is actually attempted below.
                ctx.BeginPhase("Terminating connections");
                ctx.ReportIndeterminate();
                ctx.LogInfo($"Terminating open connections to {targetDbName}.");
                await _runner.TerminateConnectionsAsync(confirmation, plan, ctx, ct).ConfigureAwait(false);

                // ── Dropping & recreating (destructive, irreversible — token required) ───────────
                // The point of no return: from here a failure/cancellation leaves the DB unusable.
                ctx.BeginPhase($"Dropping & recreating {targetDbName}");
                ctx.ReportIndeterminate();
                ctx.LogInfo($"Dropping and recreating {targetDbName}. This cannot be undone.");
                destructiveStarted = true;
                await _runner.DropAndRecreateDatabaseAsync(confirmation, plan, ctx, ct).ConfigureAwait(false);

                // ── Importing ────────────────────────────────────────────────────────────────────
                ctx.BeginPhase($"Importing into {targetDbName}");
                ctx.ReportIndeterminate();
                await _runner.ImportAsync(confirmation, plan, ctx, ct).ConfigureAwait(false);

                // ── Verifying ────────────────────────────────────────────────────────────────────
                ctx.BeginPhase("Verifying");
                ctx.ReportIndeterminate();
                await _runner.VerifyRestoreAsync(plan, ctx, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                if (destructiveStarted)
                {
                    ctx.LogError(
                        $"Cancelled after the drop began — {targetDbName} is left dropped or partially imported and is NOT usable. Re-run the restore.");

                    // Capture the failed attempt: this DB is equally unusable as an import-failure.
                    // Use CancellationToken.None so the provenance write is not itself cancelled.
                    await RecordProvenanceAsync(
                        targetDbName, sourceSnapshotId, sourceEnvLabel, provenance,
                        SafeFileLength(archivePath), RestoreStatus.Failed, CancellationToken.None).ConfigureAwait(false);
                }
                else
                {
                    ctx.LogWarning("Cancelled before any change — no database was touched.");
                }

                throw;
            }
            catch (Exception ex)
            {
                if (destructiveStarted)
                {
                    ctx.LogError(
                        $"Restore failed after the drop began — {targetDbName} is left dropped or partially imported and is NOT usable. Re-run the restore.");

                    // Record the failed attempt so the partial DB is clearly marked failed in
                    // provenance (MT-17 AC-5) — no silent half-restore. Use CancellationToken.None
                    // so the write completes even if the failure rode in on a cancellation.
                    await RecordProvenanceAsync(
                        targetDbName, sourceSnapshotId, sourceEnvLabel, provenance,
                        SafeFileLength(archivePath), RestoreStatus.Failed, CancellationToken.None).ConfigureAwait(false);
                }

                // Known, already-user-safe runner failures pass through verbatim; anything else is
                // reduced to a generic message so a raw driver exception can never leak details.
                throw Classify(ex);
            }

            // ── Success: provenance + keep-file + Done ───────────────────────────────────────────
            var size = SafeFileLength(archivePath);
            await RecordProvenanceAsync(
                targetDbName, sourceSnapshotId, sourceEnvLabel, provenance, size, RestoreStatus.Succeeded, ct).ConfigureAwait(false);

            if (!keepFile)
            {
                SafeDelete(archivePath);
                ctx.LogInfo("Keep-file preference is off — the source archive was removed.");
            }

            ctx.BeginPhase("Done");
            ctx.ReportProgress(100);
            ctx.LogInfo($"Backup restored to {targetDbName}{FormatSizeSuffix(size)}.");
        });

        AttachTerminalToast(
            job,
            () => $"Backup restored to {targetDbName}{FormatSizeSuffix(SafeFileLength(archivePath))}.",
            failureTitle: "Restore failed");

        return job;
    }

    private async Task RecordProvenanceAsync(
        string targetDbName,
        string? sourceSnapshotId,
        string sourceEnvLabel,
        RestoreProvenanceInfo? provenance,
        long? size,
        RestoreStatus status,
        CancellationToken ct)
    {
        var record = new RestoredDatabase
        {
            TargetDatabaseName = targetDbName,
            SourceApp = provenance?.SourceApp ?? sourceEnvLabel,
            SourceEnvironmentId = provenance?.SourceEnvironmentId ?? sourceEnvLabel,
            SnapshotId = sourceSnapshotId,
            SnapshotTimestamp = provenance?.SnapshotTimestamp,
            MendixVersion = provenance?.MendixVersion,
            SizeBytes = size,
            RestoredAt = _clock.GetUtcNow(),
            Status = status,
        };

        await _store.AddRestoredDatabaseAsync(record, ct).ConfigureAwait(false);
    }

    /// <summary>Passes a user-safe runner failure through unchanged; neutralises anything else so a
    /// raw driver/exec exception (which could echo connection details) never reaches the UI.</summary>
    private static Exception Classify(Exception ex) => ex switch
    {
        RestoreRunnerException => ex,
        _ => new InvalidOperationException("The restore failed unexpectedly. Open the log for details."),
    };

    private static long? SafeFileLength(string path)
    {
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : null;
        }
        catch
        {
            return null;
        }
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

    private static string FormatSizeSuffix(long? size) => size is { } bytes ? $" ({FormatSize(bytes)})" : string.Empty;

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

    private void AttachTerminalToast(Job job, Func<string> successTitle, string failureTitle)
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
                    _notifier.Success(successTitle(), null);
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
}

/// <summary>
/// Optional richer provenance MT-18 passes to <see cref="RestoreJobs.StartRestore"/> so the
/// restored-database record answers "where did this come from?" precisely (the 5-arg core call
/// falls back to the environment label). All fields feed <see cref="RestoredDatabase"/>.
/// </summary>
/// <param name="SourceApp">Source Mendix app name or AppId.</param>
/// <param name="SourceEnvironmentId">Source Deploy-v1 EnvironmentId.</param>
/// <param name="MendixVersion">Source Mendix version (null on sandboxes — MT-01).</param>
/// <param name="SnapshotTimestamp">The snapshot's own created_at.</param>
public sealed record RestoreProvenanceInfo(
    string SourceApp,
    string SourceEnvironmentId,
    string? MendixVersion = null,
    DateTimeOffset? SnapshotTimestamp = null);
