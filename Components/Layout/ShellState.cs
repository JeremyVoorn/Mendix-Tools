using Microsoft.AspNetCore.Components;

namespace Mendix_Tools.Components.Layout;

/// <summary>
/// MT-07 — the per-page topbar contract. Each routable page declares its topbar
/// <see cref="Title"/>, optional mono <see cref="Subtitle"/>, and optional right-aligned
/// <see cref="Actions"/> by rendering a single <c>&lt;PageHeader ... /&gt;</c> at the top
/// of its markup; <see cref="MainLayout"/> subscribes to <see cref="OnChanged"/> and
/// renders the topbar from this state.
///
/// Registered as a singleton (one shell for the app's lifetime in the BlazorWebView).
/// <see cref="PageHeader"/> calls <see cref="SetHeader"/> once in its
/// <c>OnInitialized</c>; because each navigation creates a fresh page (and thus a fresh
/// PageHeader) instance, the header is set exactly once per navigation with no render
/// loop. A page that needs to update its topbar after a state change (e.g. a Refresh
/// spinner) can inject <see cref="ShellState"/> and call <see cref="SetHeader"/> again —
/// the Actions fragment closes over the page, so the re-render shows current values.
/// </summary>
public sealed class ShellState
{
    public string Title { get; private set; } = string.Empty;
    public string? Subtitle { get; private set; }
    public RenderFragment? Actions { get; private set; }

    /// <summary>Raised when the header changes so the shell re-renders its topbar.</summary>
    public event Action? OnChanged;

    public void SetHeader(string title, string? subtitle = null, RenderFragment? actions = null)
    {
        Title = title;
        Subtitle = subtitle;
        Actions = actions;
        OnChanged?.Invoke();
    }
}
