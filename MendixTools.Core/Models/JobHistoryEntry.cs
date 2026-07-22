namespace MendixTools.Core.Models;

/// <summary>
/// A persisted record of a background job the engine ran (MT-08 provides the store; MT-09's
/// job engine writes terminal state + log path here). The live job lives in memory (MT-09
/// scope: no persistence-across-restart of running jobs); this table is the durable
/// after-the-fact history.
/// </summary>
public sealed class JobHistoryEntry
{
    /// <summary>Local store primary key (0 until inserted).</summary>
    public long Id { get; set; }

    /// <summary>Job kind, e.g. <c>restore</c>, <c>download</c>, <c>deploy</c>, <c>backup</c>.</summary>
    public required string JobType { get; set; }

    /// <summary>Ordered phase names the job ran, e.g. <c>["Downloading backup","Importing"]</c>.
    /// Stored as JSON; convenience accessors serialize to/from this string.</summary>
    public IReadOnlyList<string> Phases { get; set; } = [];

    /// <summary>Terminal result.</summary>
    public JobResult Result { get; set; }

    /// <summary>Path to the retained log file for this job (MT-09 keeps the log on failure).</summary>
    public string? LogPath { get; set; }

    /// <summary>When the job started.</summary>
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>When the job reached a terminal state.</summary>
    public DateTimeOffset? FinishedAt { get; set; }
}

/// <summary>Terminal job states (mirrors MT-09's engine states).</summary>
public enum JobResult
{
    Succeeded = 0,
    Failed = 1,
    Cancelled = 2,
}
