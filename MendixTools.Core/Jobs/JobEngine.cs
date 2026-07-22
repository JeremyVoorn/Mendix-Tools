using System.Collections.Concurrent;
using MendixTools.Core.Metadata;
using MendixTools.Core.Models;

namespace MendixTools.Core.Jobs;

/// <summary>
/// Default <see cref="IJobEngine"/>. Each job runs on its own worker task with a dedicated
/// <see cref="CancellationTokenSource"/>; the work delegate's exceptions are captured and
/// mapped to a terminal state, so a failed job never surfaces as an unobserved task exception.
/// When the job reaches a terminal state its lifecycle is persisted to <c>job_history</c>
/// (MT-08). Optionally, the retained log is written to a file whose path is recorded on the
/// history row (MT-09 AC: "the log is retained").
/// </summary>
public sealed class JobEngine : IJobEngine
{
    private readonly IMetadataStore _store;
    private readonly TimeProvider _clock;
    private readonly string? _logDirectory;
    private readonly ConcurrentDictionary<Guid, Registration> _registry = new();
    private long _sequence;

    /// <summary>Creates the engine.</summary>
    /// <param name="store">Metadata store the engine persists terminal job history to.</param>
    /// <param name="clock">Time source for timestamps; defaults to <see cref="TimeProvider.System"/>.</param>
    /// <param name="logDirectory">Optional directory to write each finished job's log file to;
    /// when null, logs stay in memory on the <see cref="Job"/> and <c>log_path</c> is left null.</param>
    public JobEngine(IMetadataStore store, TimeProvider? clock = null, string? logDirectory = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? TimeProvider.System;
        _logDirectory = string.IsNullOrWhiteSpace(logDirectory) ? null : logDirectory;
    }

    public event EventHandler<Job>? JobStarted;

    public IReadOnlyList<Job> Jobs =>
        _registry.Values.OrderBy(r => r.Order).Select(r => r.Job).ToArray();

    public Job? Get(Guid id) => _registry.TryGetValue(id, out var reg) ? reg.Job : null;

    public Job Start(string kind, JobWork work)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            throw new ArgumentException("Job kind is required.", nameof(kind));
        }

        ArgumentNullException.ThrowIfNull(work);

        var job = new Job(Guid.NewGuid(), kind, _clock);
        var cts = new CancellationTokenSource();
        var registration = new Registration(job, cts, Interlocked.Increment(ref _sequence));
        _registry[job.Id] = registration;

        // RunAsync runs synchronously up to its first await, so the job is transitioned to
        // Running before Start returns. Assigning Completion after lets WaitAsync await it.
        registration.Completion = RunAsync(registration, work);

        try { JobStarted?.Invoke(this, job); }
        catch { /* subscriber fault must not affect the caller */ }

        return job;
    }

    public bool Cancel(Guid id)
    {
        if (!_registry.TryGetValue(id, out var registration))
        {
            return false;
        }

        if (registration.Job.IsTerminal)
        {
            return false;
        }

        try
        {
            registration.Cts.Cancel();
            return true;
        }
        catch (ObjectDisposedException)
        {
            // The job finished between the terminal check and the cancel — treat as no-op.
            return false;
        }
    }

    public Task WaitAsync(Guid id) =>
        _registry.TryGetValue(id, out var registration)
            ? registration.Completion ?? Task.CompletedTask
            : Task.CompletedTask;

    private async Task RunAsync(Registration registration, JobWork work)
    {
        var job = registration.Job;
        var token = registration.Cts.Token;

        job.MarkRunning();

        try
        {
            var context = new JobContext(job, token);
            await work(context, token).ConfigureAwait(false);
            job.MarkSucceeded();
        }
        catch (OperationCanceledException) when (registration.Cts.IsCancellationRequested)
        {
            // Cooperative cancellation observed at a safe point.
            job.MarkCancelled("Cancelled.");
        }
        catch (Exception ex)
        {
            // Any other fault ends the job as Failed with an actionable message — never a crash.
            job.MarkFailed(ex.Message);
        }
        finally
        {
            await PersistAsync(job).ConfigureAwait(false);
            registration.Cts.Dispose();
        }
    }

    private async Task PersistAsync(Job job)
    {
        try
        {
            string? logPath = _logDirectory is not null ? WriteLogFile(job) : null;

            var entry = new JobHistoryEntry
            {
                JobType = job.Kind,
                Phases = job.Phases,
                Result = ToResult(job.State),
                LogPath = logPath,
                StartedAt = job.StartedAt,
                FinishedAt = job.FinishedAt,
            };

            await _store.AddJobAsync(entry).ConfigureAwait(false);
        }
        catch
        {
            // Persistence/log-file failures must never crash the engine or change the job's
            // outcome; the in-memory Job remains the source of truth for the current session.
        }
    }

    private string WriteLogFile(Job job)
    {
        Directory.CreateDirectory(_logDirectory!);
        var path = Path.Combine(_logDirectory!, $"{job.Id:N}.log");
        var lines = job.Log.Select(l =>
            $"{l.Timestamp:O} [{l.Level,-7}] {l.Message}");
        File.WriteAllLines(path, lines);
        return path;
    }

    private static JobResult ToResult(JobState state) => state switch
    {
        JobState.Succeeded => JobResult.Succeeded,
        JobState.Failed => JobResult.Failed,
        JobState.Cancelled => JobResult.Cancelled,
        _ => throw new InvalidOperationException($"Job persisted in non-terminal state {state}."),
    };

    private sealed class Registration(Job job, CancellationTokenSource cts, long order)
    {
        public Job Job { get; } = job;
        public CancellationTokenSource Cts { get; } = cts;
        public long Order { get; } = order;
        public Task? Completion { get; set; }
    }

    private sealed class JobContext(Job job, CancellationToken cancellationToken) : IJobContext
    {
        private readonly Job _job = job;

        public Guid JobId => _job.Id;
        public CancellationToken CancellationToken { get; } = cancellationToken;
        public void BeginPhase(string phaseName) => _job.BeginPhase(phaseName);
        public void ReportProgress(double percent) => _job.SetProgress(percent);
        public void ReportIndeterminate() => _job.SetIndeterminate();
        public void Log(JobLogLevel level, string message) => _job.AppendLog(level, message);
    }
}
