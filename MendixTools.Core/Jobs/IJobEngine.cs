namespace MendixTools.Core.Jobs;

/// <summary>
/// The shared, UI-agnostic background-job engine (MT-09 / N5). Registered as a singleton so
/// jobs outlive any Blazor component and survive navigation; the UI reads live <see cref="Job"/>
/// state and subscribes to a job's events to re-render. Generic over the work: start a job from
/// a <see cref="JobWork"/> delegate and the engine runs it async, cancellably, and persists its
/// terminal state to <c>job_history</c> (MT-08). Scope-guarded per MT-09: no scheduling, queue,
/// retry, or persistence of running jobs across restart.
/// </summary>
public interface IJobEngine
{
    /// <summary>
    /// Creates a job of the given <paramref name="kind"/>, starts it immediately, and returns
    /// the live <see cref="Job"/>. The job begins in <see cref="JobState.Queued"/> and is
    /// transitioned to <see cref="JobState.Running"/> synchronously before this returns.
    /// </summary>
    /// <param name="kind">Job kind (persisted to <c>job_history.job_type</c>), e.g. <c>download</c>.</param>
    /// <param name="work">The async work to run.</param>
    Job Start(string kind, JobWork work);

    /// <summary>All jobs the engine knows about — running and terminal — in start order.
    /// Completed/failed/cancelled jobs remain queryable (no eviction in v1).</summary>
    IReadOnlyList<Job> Jobs { get; }

    /// <summary>Looks up a job by id, or null if unknown.</summary>
    Job? Get(Guid id);

    /// <summary>Requests cooperative cancellation of a running job. Returns true if a running
    /// job was signalled; false if the id is unknown or the job is already terminal.</summary>
    bool Cancel(Guid id);

    /// <summary>Awaits the completion of a job's run (including terminal persistence). Completes
    /// immediately for unknown/finished jobs. Never throws — a failed job ends as
    /// <see cref="JobState.Failed"/>, not as a faulted task.</summary>
    Task WaitAsync(Guid id);

    /// <summary>Raised when a new job has been started (for a UI job list to react to).</summary>
    event EventHandler<Job>? JobStarted;
}
