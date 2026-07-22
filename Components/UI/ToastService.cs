// Mendix Tools — ToastService (MT-05). The programmatic entry point for transient
// result notifications (readme feedback group: "Toast + ToastStack — transient result
// notifications with a fixed stack container"). A single ToastStack subscribes to
// OnChange and renders the live list; callers just do ToastService.Show(...).
//
// Registered as a singleton in MauiProgram (one window, one stack). Auto-dismiss is
// scheduled here; the ToastStack marshals OnChange onto the UI thread with InvokeAsync.

namespace Mendix_Tools.Components.UI;

/// <summary>Holds the active toasts and raises <see cref="OnChange"/> when the set changes.</summary>
public sealed class ToastService
{
    private const int DefaultDurationMs = 5000;

    private readonly List<ToastMessage> _toasts = [];
    private readonly Lock _gate = new();

    /// <summary>Fired whenever a toast is added or removed. The ToastStack re-renders on this.</summary>
    public event Action? OnChange;

    /// <summary>Snapshot of the currently visible toasts, oldest first.</summary>
    public IReadOnlyList<ToastMessage> Toasts
    {
        get
        {
            lock (_gate)
            {
                return _toasts.ToArray();
            }
        }
    }

    /// <summary>
    /// Show a toast. <paramref name="durationMs"/> null uses the 5s default; 0 disables
    /// auto-dismiss (manual dismiss only). Returns the created message's id.
    /// </summary>
    public Guid Show(
        ToastTone tone,
        string? title,
        string? message = null,
        string? icon = null,
        int? durationMs = null)
    {
        var duration = durationMs ?? DefaultDurationMs;
        var toast = new ToastMessage(Guid.NewGuid(), tone, title, message, icon, duration);

        lock (_gate)
        {
            _toasts.Add(toast);
        }
        OnChange?.Invoke();

        if (duration > 0)
        {
            _ = AutoDismissAsync(toast.Id, duration);
        }

        return toast.Id;
    }

    /// <summary>Convenience for the common "backup created / restore done" success toast.</summary>
    public Guid Success(string? title, string? message = null, int? durationMs = null)
        => Show(ToastTone.Success, title, message, "check-circle-2", durationMs);

    /// <summary>Convenience for a failure toast ("what happened + what to do next").</summary>
    public Guid Error(string? title, string? message = null, int? durationMs = null)
        => Show(ToastTone.Danger, title, message, "alert-triangle", durationMs);

    /// <summary>Dismiss a toast by id (manual dismiss, or the end of auto-dismiss).</summary>
    public void Dismiss(Guid id)
    {
        bool removed;
        lock (_gate)
        {
            removed = _toasts.RemoveAll(t => t.Id == id) > 0;
        }
        if (removed)
        {
            OnChange?.Invoke();
        }
    }

    private async Task AutoDismissAsync(Guid id, int durationMs)
    {
        await Task.Delay(durationMs);
        Dismiss(id);
    }
}
