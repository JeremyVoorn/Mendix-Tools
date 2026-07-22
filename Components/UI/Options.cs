// Mendix Tools — item records for the list-driven primitives (MT-04, MT-05, MT-06).
// These mirror the plain-object props of the design-system JSX references.

using Microsoft.AspNetCore.Components;

namespace Mendix_Tools.Components.UI;

/// <summary>
/// An option for <c>Select</c> (Select.jsx <c>options</c>: <c>[{value,label}]</c>).
/// Alternative to passing raw <c>&lt;option&gt;</c> elements as child content.
/// </summary>
public sealed record SelectOption(string Value, string Label);

/// <summary>
/// A tab definition for <c>Tabs</c> (Tabs.jsx <c>tabs</c>: <c>[{value,label,icon,count}]</c>).
/// <paramref name="Icon"/> is a bundled Lucide name (rendered at 15px, per the ui_kits usage);
/// <paramref name="Count"/> renders as a small mono pill.
/// </summary>
public sealed record TabItem(string Value, string Label, string? Icon = null, int? Count = null);

// ---- MT-05 Toast ----

/// <summary>
/// A transient result notification rendered by <c>ToastStack</c> (readme: feedback group).
/// Created via <c>ToastService.Show(...)</c>. <paramref name="Icon"/> is a bundled Lucide name;
/// when null the stack picks a sensible default for the tone. <paramref name="DurationMs"/> of
/// 0 disables auto-dismiss (manual dismiss only).
/// </summary>
public sealed record ToastMessage(
    Guid Id,
    ToastTone Tone,
    string? Title,
    string? Message,
    string? Icon,
    int DurationMs);

// ---- MT-06 DataTable ----

/// <summary>
/// A column definition for <c>DataTable&lt;TRow&gt;</c> (DataTable.jsx column:
/// <c>{key,header,width,align,mono,render}</c>). Provide either <see cref="Value"/> (plain text
/// accessor, matches the JSX <c>r[c.key]</c> default) or <see cref="Render"/> (a custom cell
/// template, matches the JSX <c>render</c>); <see cref="Render"/> wins when both are set.
/// </summary>
/// <typeparam name="TRow">The row model type.</typeparam>
public sealed class DataColumn<TRow>
{
    /// <summary>Stable column key (jsx <c>key</c>). Used for the header/cell element key.</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>Uppercased header label (jsx <c>header</c>).</summary>
    public string Header { get; init; } = string.Empty;

    /// <summary>Cell + header alignment (jsx <c>align</c>, default left).</summary>
    public ColumnAlign Align { get; init; } = ColumnAlign.Left;

    /// <summary>Render the cell in mono for data/identifiers (jsx <c>mono</c>).</summary>
    public bool Mono { get; init; }

    /// <summary>Allow wrapping instead of the default nowrap (jsx <c>wrap</c>).</summary>
    public bool Wrap { get; init; }

    /// <summary>Optional fixed CSS width for the column (jsx <c>width</c>), e.g. <c>"160px"</c>.</summary>
    public string? Width { get; init; }

    /// <summary>Plain-text cell accessor (the JSX default <c>r[c.key]</c>).</summary>
    public Func<TRow, string?>? Value { get; init; }

    /// <summary>Custom cell template (jsx <c>render(value, row)</c>). Wins over <see cref="Value"/>.</summary>
    public RenderFragment<TRow>? Render { get; init; }
}

// ---- MT-06 LogViewer ----

/// <summary>
/// A single console line for <c>LogViewer</c> (LogViewer.jsx <c>{time,level,text}</c>).
/// <paramref name="Time"/> is an already-formatted timestamp (e.g. <c>"02:14:07"</c>) or null.
/// </summary>
public sealed record LogLine(string Text, LogLevel Level = LogLevel.Info, string? Time = null);
