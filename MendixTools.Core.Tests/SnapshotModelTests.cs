using Mendix_Tools.Models;
using Xunit;

namespace MendixTools.Core.Tests;

/// <summary>
/// MT-14 — tests for the pure Snapshot logic: the comment-derived <see cref="SnapshotType"/>
/// heuristic (there is no API type field — MT-01 §4) and the completed-only action rule.
/// These are the non-UI pieces the wired Backups-v2 client (RealBackupService) will reuse
/// verbatim, so they are worth pinning independently of the mock/page.
/// </summary>
public sealed class SnapshotModelTests
{
    [Theory]
    // Automatic — the stable nightly phrasing, case-insensitive + drift-tolerant (prefix match).
    [InlineData("Automatically created nightly snapshot", SnapshotType.Automatic)]
    [InlineData("automatically created nightly snapshot", SnapshotType.Automatic)]
    [InlineData("Automatically created snapshot (nightly, retained 30d)", SnapshotType.Automatic)]
    // Pipeline — the CI phrasing, matched anywhere in the comment, case-insensitive.
    [InlineData("Backup created by Mendix pipeline", SnapshotType.Pipeline)]
    [InlineData("backup created by mendix PIPELINE", SnapshotType.Pipeline)]
    // Manual — any human comment.
    [InlineData("Before v10.12 model upgrade", SnapshotType.Manual)]
    [InlineData("Pre-migration checkpoint", SnapshotType.Manual)]
    public void DeriveType_MapsCommentToType(string comment, SnapshotType expected)
        => Assert.Equal(expected, Snapshot.DeriveType(comment));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void DeriveType_BlankComment_IsManual(string? comment)
        => Assert.Equal(SnapshotType.Manual, Snapshot.DeriveType(comment));

    [Fact]
    public void Type_Property_UsesDeriveType()
    {
        var snap = NewSnapshot(SnapshotState.Completed, "Automatically created nightly snapshot");
        Assert.Equal(SnapshotType.Automatic, snap.Type);
    }

    [Theory]
    [InlineData(SnapshotState.Completed, true)]  // only completed rows expose Restore/Download
    [InlineData(SnapshotState.Failed, false)]    // failed rows carry status_message, no actions
    [InlineData(SnapshotState.Queued, false)]
    [InlineData(SnapshotState.Running, false)]
    public void HasActions_TrueOnlyForCompleted(SnapshotState state, bool expected)
        => Assert.Equal(expected, NewSnapshot(state, "x").HasActions);

    [Fact]
    public void BackupListResult_Empty_IsZeroTotalNoRows()
    {
        Assert.Equal(0, BackupListResult.Empty.Total);
        Assert.Empty(BackupListResult.Empty.Snapshots);
    }

    private static Snapshot NewSnapshot(SnapshotState state, string comment) => new()
    {
        SnapshotId = "snap-001",
        Comment = comment,
        State = state,
        CreatedAt = DateTimeOffset.UtcNow,
    };
}
