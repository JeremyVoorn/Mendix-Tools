using Mendix_Tools.Models;

namespace Mendix_Tools.Services;

/// <summary>
/// MT-14 mock <see cref="IBackupService"/>. Hardcoded snapshots mirroring the LIVE-VERIFIED
/// Backups API v2 shape (MT-01 spike §4): a realistic mix of completed nightly + pipeline
/// snapshots, a couple of manual ones with human comments, and a few FAILED rows carrying
/// real-style <c>status_message</c> text. Response is the paginated <c>{ total, snapshots[] }</c>
/// shape — a rich environment returns one page (12 rows) out of a larger <c>total</c>, proving
/// the page's "showing N of total" note without inventing a second page.
///
/// Per-environment behaviour is chosen so the Tester can see every state from the selector,
/// keyed by EnvironmentId (MOCK-ONLY routing — the real client has no such branching):
///   • <c>acme-test</c> and any sandbox env → <see cref="BackupListResult.Empty"/> (empty state);
///   • <c>kwik-accp</c> → throws a simulated timeout (failed-list state);
///   • everything else → the rich page (incl. failed snapshot ROWS).
///
/// This is throwaway data for the mock-first slice: NOT wired to any real API, carries no
/// secrets, makes no HTTP call. MT-20 replaces this class with the real Backups-v2 client.
/// </summary>
public sealed class MockBackupService : IBackupService
{
    // The real environment had 139 snapshots, 11 of them failed (MT-01 §4). We return the
    // newest page and report the full total so the page shows "Showing 12 of 139".
    private const int RichTotal = 139;

    public async Task<BackupListResult> GetSnapshotsAsync(
        string projectId, string environmentId, CancellationToken ct = default)
    {
        // Simulate the per-env Backups-v2 round-trip so the loading state is observable.
        await Task.Delay(450, ct).ConfigureAwait(false);

        // Empty environments (fresh env / any sandbox) — proves the calm empty state.
        if (string.Equals(environmentId, "acme-test", StringComparison.OrdinalIgnoreCase)
            || environmentId.Contains("sandbox", StringComparison.OrdinalIgnoreCase))
        {
            return BackupListResult.Empty;
        }

        // A deliberately-failing list call — proves the failed-list state (what happened +
        // what to do next). The page maps thrown exceptions to that state.
        if (string.Equals(environmentId, "kwik-accp", StringComparison.OrdinalIgnoreCase))
        {
            throw new TimeoutException("The Backups API did not respond in time.");
        }

        return new BackupListResult(RichTotal, BuildRichPage());
    }

    public async Task<Snapshot> CreateBackupAsync(
        string projectId, string environmentId, string? comment = null, CancellationToken ct = default)
    {
        // Simulate the Backups-v2 POST round-trip; returns a fresh snapshot in the initial
        // queued state (the create job then polls it to Completed). MOCK-ONLY routing lets the
        // Tester exercise the failure toast: the "kwik-accp" env simulates a rejected credential.
        await Task.Delay(300, ct).ConfigureAwait(false);

        if (string.Equals(environmentId, "kwik-accp", StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("The Mendix credential was rejected.");
        }

        var now = DateTimeOffset.Now;
        return new Snapshot
        {
            SnapshotId = $"snap-new-{now:HHmmss}",
            Comment = string.IsNullOrWhiteSpace(comment) ? "Backup created from Mendix Tools" : comment!,
            State = SnapshotState.Queued,
            StatusMessage = null,
            ModelVersion = null,
            CreatedAt = now,
            FinishedAt = null,
            UpdatedAt = now,
            ExpiresAt = now.AddMonths(3),
        };
    }

    public async Task<BackupDownload> DownloadArchiveAsync(
        string projectId, string environmentId, string snapshotId, string destinationDirectory,
        bool verifyIntegrity = true, CancellationToken ct = default)
    {
        // Simulate archive-request + prepare + a small streamed body. MOCK-ONLY: no HTTP, no secret.
        await Task.Delay(300, ct).ConfigureAwait(false);

        if (string.Equals(environmentId, "kwik-accp", StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("The Mendix credential was rejected.");
        }

        Directory.CreateDirectory(destinationDirectory);
        var path = Path.Combine(destinationDirectory, $"snapshot-{snapshotId}.backup");

        // Write a small but structurally VALID gzip so the integrity check passes in offline dev.
        await using (var file = File.Create(path))
        await using (var gzip = new System.IO.Compression.GZipStream(file, System.IO.Compression.CompressionLevel.Fastest))
        {
            var payload = System.Text.Encoding.UTF8.GetBytes($"mock database_only archive for {snapshotId}\n");
            await gzip.WriteAsync(payload, ct).ConfigureAwait(false);
        }

        return new BackupDownload(path, new FileInfo(path).Length);
    }

    // One page of 12 snapshots, newest first — the mix the real data showed. `now` is the
    // anchor so Created/Expires render stable relative dates across runs.
    private static IReadOnlyList<Snapshot> BuildRichPage()
    {
        var now = DateTimeOffset.Now;
        DateTimeOffset created(int daysAgo, int hour, int minute)
            => new DateTimeOffset(now.Year, now.Month, now.Day, hour, minute, 0, now.Offset).AddDays(-daysAgo);

        var rows = new List<Snapshot>();

        void completed(int daysAgo, int hour, int minute, string comment, string modelVersion)
        {
            var c = created(daysAgo, hour, minute);
            rows.Add(new Snapshot
            {
                SnapshotId = $"snap-{rows.Count + 1:D3}",
                Comment = comment,
                State = SnapshotState.Completed,
                StatusMessage = null,
                ModelVersion = modelVersion,
                CreatedAt = c,
                FinishedAt = c.AddMinutes(7),
                UpdatedAt = c.AddMinutes(7),
                // Retention varies in real data (~1–12 months) — mostly ~3 months here,
                // with one older manual snapshot kept ~12 months.
                ExpiresAt = c.AddMonths(3),
            });
        }

        void failed(int daysAgo, int hour, int minute, string comment, string statusMessage)
        {
            var c = created(daysAgo, hour, minute);
            rows.Add(new Snapshot
            {
                SnapshotId = $"snap-{rows.Count + 1:D3}",
                Comment = comment,
                State = SnapshotState.Failed,
                StatusMessage = statusMessage,
                ModelVersion = null, // absent on failed snapshots (live run §4)
                CreatedAt = c,
                FinishedAt = c.AddMinutes(2),
                UpdatedAt = c.AddMinutes(2),
                ExpiresAt = c.AddMonths(3),
            });
        }

        // Newest first. Two nightly-automatic streams broken up by pipeline + manual +
        // failed rows, matching the live comment strings.
        completed(0, 3, 12, "Automatically created nightly snapshot", "1.12.4.5521");
        failed(0, 14, 8, "Backup created by Mendix pipeline", "could not connect to server: Connection refused");
        completed(1, 3, 12, "Automatically created nightly snapshot", "1.12.4.5521");
        completed(1, 11, 30, "Backup created by Mendix pipeline", "1.12.4.5521");
        completed(2, 3, 12, "Automatically created nightly snapshot", "1.12.3.5498");
        rows.Add(ManualSnapshot(created(2, 16, 44), 6, "Before v10.12 model upgrade", "1.12.3.5498", monthsRetained: 12));
        completed(3, 3, 12, "Automatically created nightly snapshot", "1.12.3.5498");
        failed(3, 11, 30, "Backup created by Mendix pipeline", "relation \"account\" does not exist");
        completed(4, 3, 12, "Automatically created nightly snapshot", "1.12.2.5471");
        rows.Add(ManualSnapshot(created(4, 9, 5), 10, "Pre-migration checkpoint", "1.12.2.5471", monthsRetained: 6));
        completed(5, 3, 12, "Automatically created nightly snapshot", "1.12.2.5471");
        failed(6, 3, 12, "Automatically created nightly snapshot", "backup timed out after 3600s");

        return rows;
    }

    private static Snapshot ManualSnapshot(
        DateTimeOffset created, int idIndex, string comment, string modelVersion, int monthsRetained)
        => new()
        {
            SnapshotId = $"snap-{idIndex:D3}",
            Comment = comment,
            State = SnapshotState.Completed,
            StatusMessage = null,
            ModelVersion = modelVersion,
            CreatedAt = created,
            FinishedAt = created.AddMinutes(9),
            UpdatedAt = created.AddMinutes(9),
            ExpiresAt = created.AddMonths(monthsRetained),
        };
}
