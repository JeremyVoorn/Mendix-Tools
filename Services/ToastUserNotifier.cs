using Mendix_Tools.Components.UI;

namespace Mendix_Tools.Services;

/// <summary>
/// MT-15 — the app adapter that forwards <see cref="IUserNotifier"/> to the shell-level
/// <see cref="ToastService"/> (which drives the single <c>ToastStack</c> now rooted in
/// MainLayout). This file lives in the MAUI app project only — it is NOT linked into the test
/// project, which uses a fake <see cref="IUserNotifier"/> instead.
/// </summary>
public sealed class ToastUserNotifier : IUserNotifier
{
    private readonly ToastService _toasts;

    public ToastUserNotifier(ToastService toasts) => _toasts = toasts;

    public void Success(string title, string? message = null) => _toasts.Success(title, message);

    public void Error(string title, string? message = null) => _toasts.Error(title, message);
}
