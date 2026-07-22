namespace MendixTools.Core.Jobs;

/// <summary>
/// A long-running background operation (MT-09 / N5) — the shared backbone for the three
/// flagship flows (backup download, restore-to-local-Postgres, build&amp;deploy). The live job
/// lives in memory in a singleton <see cref="IJobEngine"/> so it survives Blazor navigation;
/// a Blazor component subscribes to <see cref="StateChanged"/>/<see cref="ProgressChanged"/>/
/// <see cref="LineAppended"/> and calls <c>InvokeAsync(StateHasChanged)</c> to re-render.
/// UI-agnostic (vision principle 7): no MAUI/Blazor types cross this boundary.
///
/// State machine (scope-guarded — no scheduling/queue/retry/persist-across-restart):
///     Queued ─▶ Running ─▶ Succeeded | Failed | Cancelled  (terminal)
/// Progress is a 0..100 percentage within the current phase, or indeterminate.
/// </summary>
public sealed class Job
{
    private readonly object _gate = new();
    private readonly TimeProvider _clock;
    private readonly List<string> _phases = [];
    private readonly List<JobLogLine> _log = [];
    private readonly List<JobState> _stateHistory;

    private JobState _state = JobState.Queued;
    private string? _currentPhase;
    private double? _progress;
    private bool _indeterminate;
    private string? _message;
    private DateTimeOffset? _startedAt;
    private DateTimeOffset? _finishedAt;

    internal Job(Guid id, string kind, TimeProvider clock)
    {
        Id = id;
        Kind = kind;
        _clock = clock;
        _stateHistory = [JobState.Queued];
    }

    /// <summary>Stable identity of this job (assigned by the engine).</summary>
    public Guid Id { get; }

    /// <summary>What the job does, e.g. <c>download</c>, <c>restore</c>, <c>deploy</c>.
    /// Persisted verbatim to <c>job_history.job_type</c>.</summary>
    public string Kind { get; }

    /// <summary>Current lifecycle state (thread-safe read).</summary>
    public JobState State { get { lock (_gate) { return _state; } } }

    /// <summary>True once the job has reached a terminal state.</summary>
    public bool IsTerminal { get { lock (_gate) { return IsTerminalState(_state); } } }

    /// <summary>Name of the phase currently running, or null before the first phase begins.</summary>
    public string? CurrentPhase { get { lock (_gate) { return _currentPhase; } } }

    /// <summary>Ordered names of the phases begun so far (feeds <c>job_history.phases</c>).</summary>
    public IReadOnlyList<string> Phases { get { lock (_gate) { return _phases.ToArray(); } } }

    /// <summary>Progress in the current phase, 0..100, or null when the phase is indeterminate.</summary>
    public double? Progress { get { lock (_gate) { return _progress; } } }

    /// <summary>True when the current phase reports no measurable progress (spinner, not a bar).</summary>
    public bool IsIndeterminate { get { lock (_gate) { return _indeterminate; } } }

    /// <summary>Immutable snapshot of the streamed log lines (feeds the LogViewer). Retained
    /// after the job finishes so a failed job's log stays inspectable (MT-09 AC).</summary>
    public IReadOnlyList<JobLogLine> Log { get { lock (_gate) { return _log.ToArray(); } } }

    /// <summary>Human-readable status/error message. On failure this states what happened
    /// (vision principle 3); null while the job is running cleanly.</summary>
    public string? Message { get { lock (_gate) { return _message; } } }

    /// <summary>When the job transitioned to <see cref="JobState.Running"/>.</summary>
    public DateTimeOffset? StartedAt { get { lock (_gate) { return _startedAt; } } }

    /// <summary>When the job reached a terminal state.</summary>
    public DateTimeOffset? FinishedAt { get { lock (_gate) { return _finishedAt; } } }

    /// <summary>Ordered record of the states this job entered (starts with
    /// <see cref="JobState.Queued"/>). A deterministic audit trail for the UI and tests.</summary>
    public IReadOnlyList<JobState> StateHistory { get { lock (_gate) { return _stateHistory.ToArray(); } } }

    /// <summary>Raised on every state transition. Handlers run on the job's worker thread;
    /// a UI subscriber marshals with <c>InvokeAsync(StateHasChanged)</c>.</summary>
    public event EventHandler<JobStateChangedEventArgs>? StateChanged;

    /// <summary>Raised when progress, the indeterminate flag, or the current phase changes.</summary>
    public event EventHandler? ProgressChanged;

    /// <summary>Raised for each appended log line.</summary>
    public event EventHandler<JobLogLine>? LineAppended;

    // ── Engine-driven state transitions (only valid orderings are accepted) ────────────

    internal void MarkRunning()
    {
        Transition(JobState.Running, message: null, requireFrom: JobState.Queued, stampStart: true);
    }

    internal void MarkSucceeded()
    {
        Transition(JobState.Succeeded, message: null, requireFrom: JobState.Running, stampFinish: true);
    }

    internal void MarkFailed(string message)
    {
        Transition(JobState.Failed, message, requireFrom: JobState.Running, stampFinish: true);
    }

    internal void MarkCancelled(string? message)
    {
        Transition(JobState.Cancelled, message, requireFrom: JobState.Running, stampFinish: true);
    }

    private void Transition(JobState target, string? message, JobState requireFrom, bool stampStart = false, bool stampFinish = false)
    {
        JobState previous;
        lock (_gate)
        {
            if (_state != requireFrom)
            {
                throw new InvalidOperationException(
                    $"Illegal job transition {_state} → {target} (expected from {requireFrom}).");
            }

            previous = _state;
            _state = target;
            _stateHistory.Add(target);
            if (message is not null)
            {
                _message = message;
            }

            if (stampStart)
            {
                _startedAt = _clock.GetUtcNow();
            }

            if (stampFinish)
            {
                _finishedAt = _clock.GetUtcNow();
            }
        }

        RaiseStateChanged(previous, target);
    }

    // ── Progress / phase / log updates (accepted only while Running) ───────────────────

    internal void BeginPhase(string phaseName)
    {
        lock (_gate)
        {
            if (_state != JobState.Running)
            {
                return;
            }

            _phases.Add(phaseName);
            _currentPhase = phaseName;
            _progress = 0d;      // a new phase restarts the bar
            _indeterminate = false;
        }

        RaiseProgressChanged();
    }

    internal void SetProgress(double percent)
    {
        var clamped = Math.Clamp(percent, 0d, 100d);
        lock (_gate)
        {
            if (_state != JobState.Running)
            {
                return;
            }

            _progress = clamped;
            _indeterminate = false;
        }

        RaiseProgressChanged();
    }

    internal void SetIndeterminate()
    {
        lock (_gate)
        {
            if (_state != JobState.Running)
            {
                return;
            }

            _indeterminate = true;
            _progress = null;
        }

        RaiseProgressChanged();
    }

    internal void AppendLog(JobLogLevel level, string message)
    {
        JobLogLine line;
        lock (_gate)
        {
            if (_state != JobState.Running)
            {
                return;
            }

            line = new JobLogLine(_clock.GetUtcNow(), level, message);
            _log.Add(line);
        }

        RaiseLineAppended(line);
    }

    internal static bool IsTerminalState(JobState state) =>
        state is JobState.Succeeded or JobState.Failed or JobState.Cancelled;

    // A misbehaving subscriber must never destabilise the engine or change a job's outcome,
    // so every raise is isolated. Events are raised outside the lock and in call order (the
    // engine drives a single worker per job), so subscribers observe a consistent sequence.
    private void RaiseStateChanged(JobState previous, JobState current)
    {
        try { StateChanged?.Invoke(this, new JobStateChangedEventArgs(previous, current)); }
        catch { /* subscriber fault is swallowed by design */ }
    }

    private void RaiseProgressChanged()
    {
        try { ProgressChanged?.Invoke(this, EventArgs.Empty); }
        catch { /* subscriber fault is swallowed by design */ }
    }

    private void RaiseLineAppended(JobLogLine line)
    {
        try { LineAppended?.Invoke(this, line); }
        catch { /* subscriber fault is swallowed by design */ }
    }
}

/// <summary>Lifecycle states of a <see cref="Job"/>. Terminal = Succeeded/Failed/Cancelled.</summary>
public enum JobState
{
    /// <summary>Created but not yet running (the engine starts it immediately — no queue).</summary>
    Queued = 0,

    /// <summary>Actively executing its work delegate.</summary>
    Running = 1,

    /// <summary>Finished successfully.</summary>
    Succeeded = 2,

    /// <summary>The work threw; <see cref="Job.Message"/> states what happened.</summary>
    Failed = 3,

    /// <summary>Cooperatively cancelled at a safe point.</summary>
    Cancelled = 4,
}

/// <summary>Severity of a streamed <see cref="JobLogLine"/> (drives LogViewer styling).</summary>
public enum JobLogLevel
{
    Debug = 0,
    Info = 1,
    Warning = 2,
    Error = 3,
}

/// <summary>One line in a job's log stream.</summary>
/// <param name="Timestamp">When the line was emitted (UTC).</param>
/// <param name="Level">Severity.</param>
/// <param name="Message">The text (identifiers/numbers exact per vision principle 2).</param>
public sealed record JobLogLine(DateTimeOffset Timestamp, JobLogLevel Level, string Message);

/// <summary>Payload for <see cref="Job.StateChanged"/>.</summary>
public sealed class JobStateChangedEventArgs(JobState previous, JobState current) : EventArgs
{
    /// <summary>State before the transition.</summary>
    public JobState Previous { get; } = previous;

    /// <summary>State after the transition.</summary>
    public JobState Current { get; } = current;
}
