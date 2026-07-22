namespace Mendix_Tools.Services;

/// <summary>
/// MT-15 — a tiny toast seam so the job orchestrator (<see cref="BackupJobs"/>) can fire
/// success/failure notifications WITHOUT depending on the Blazor <c>ToastService</c> (which pulls
/// in <c>Microsoft.AspNetCore.Components</c> and cannot be linked into the pure net10.0 test
/// project). The app wires <c>ToastUserNotifier</c> onto the shell-level ToastStack; tests use a
/// trivial fake. Toasts fire from the singleton orchestrator, so they surface regardless of which
/// screen is mounted when a long-running job finishes (the card survives navigation; the toast is
/// the completion beat). Reused by MT-16 (download) next.
/// </summary>
public interface IUserNotifier
{
    /// <summary>A neutral/positive result toast (voice rules: facts, no celebration).</summary>
    void Success(string title, string? message = null);

    /// <summary>A failure toast — "what happened + what to do next"; never a secret.</summary>
    void Error(string title, string? message = null);
}
