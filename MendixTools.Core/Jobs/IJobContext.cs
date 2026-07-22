namespace MendixTools.Core.Jobs;

/// <summary>
/// The handle a job's work delegate uses to report progress and stream log lines back to its
/// <see cref="Job"/>, and to observe cancellation. Handed to the delegate by the engine while
/// the job is Running; calls made after the job leaves Running are ignored (the engine only
/// invokes work during the Running state, so this is defensive).
/// </summary>
public interface IJobContext
{
    /// <summary>Id of the job being run.</summary>
    Guid JobId { get; }

    /// <summary>Cancellation token for this run; cooperative — check it at safe points
    /// (e.g. <c>ct.ThrowIfCancellationRequested()</c>) to end the job as
    /// <see cref="JobState.Cancelled"/>.</summary>
    CancellationToken CancellationToken { get; }

    /// <summary>Starts a new named phase, appending it to <see cref="Job.Phases"/> and
    /// resetting progress to 0. Phases are ordered, e.g. "Downloading backup" →
    /// "Dropping &amp; recreating schema" → "Importing into acme_local".</summary>
    void BeginPhase(string phaseName);

    /// <summary>Reports progress within the current phase as a percentage (0..100, clamped).</summary>
    void ReportProgress(double percent);

    /// <summary>Marks the current phase as having no measurable progress (spinner, not a bar).</summary>
    void ReportIndeterminate();

    /// <summary>Appends a log line at the given level.</summary>
    void Log(JobLogLevel level, string message);

    /// <summary>Appends an informational log line.</summary>
    void LogInfo(string message) => Log(JobLogLevel.Info, message);

    /// <summary>Appends a warning log line.</summary>
    void LogWarning(string message) => Log(JobLogLevel.Warning, message);

    /// <summary>Appends an error log line.</summary>
    void LogError(string message) => Log(JobLogLevel.Error, message);
}

/// <summary>
/// The actual work of a job: an async delegate given a <see cref="IJobContext"/> reporter and a
/// <see cref="CancellationToken"/>. Keeping the engine generic over this delegate is how the
/// backup/restore/deploy stories (MT-16/17/18) plug in without touching the engine. Throwing
/// ends the job as <see cref="JobState.Failed"/>; throwing <see cref="OperationCanceledException"/>
/// after the token is cancelled ends it as <see cref="JobState.Cancelled"/>.
/// </summary>
public delegate Task JobWork(IJobContext context, CancellationToken cancellationToken);
