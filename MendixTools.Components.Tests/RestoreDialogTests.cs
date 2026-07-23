using AngleSharp.Dom;
using Bunit;
using Mendix_Tools.Components.UI;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace MendixTools.Components.Tests;

/// <summary>
/// MT-18 — the restore CONFIGURE dialog (step one of the two-step flow). Verifies the design-spec
/// shape from BackupsScreen.jsx that the page relies on: the target-database field is prefilled from
/// the default, the "Merge into existing" strategy is VISIBLE but DISABLED (CUT for v1 — D5), the
/// keep-file checkbox reflects its default, and "Start restore" emits a <see cref="RestoreDialogResult"/>
/// carrying the (possibly edited) database name + keep-file choice — it starts NO work (the MT-19
/// guard, exercised in DestructiveConfirmTests, gates the destructive step downstream).
///
/// ⛔ SAFETY: this renders UI only — no download, no restore, no database is touched.
/// </summary>
public sealed class RestoreDialogTests : TestContext
{
    private const string DefaultDb = "acme_local";

    public RestoreDialogTests()
    {
        // The reused Dialog primitive imports mxt-interop.js and installs a focus trap on render.
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void TargetDatabase_IsPrefilledFromDefault()
    {
        var cut = Render(out _, out _);
        var input = TargetInput(cut);
        Assert.Equal(DefaultDb, input.GetAttribute("value"));
    }

    [Fact]
    public void MergeStrategy_IsVisibleButDisabled()
    {
        var cut = Render(out _, out _);
        var radios = cut.FindAll("input[type=radio]");
        Assert.Equal(2, radios.Count);

        // Clean is enabled and selected; Merge is present but disabled (D5 cut).
        var clean = radios[0];
        var merge = radios[1];
        Assert.False(clean.HasAttribute("disabled"));
        Assert.True(clean.HasAttribute("checked"));
        Assert.True(merge.HasAttribute("disabled"));
        Assert.False(merge.HasAttribute("checked"));
    }

    [Fact]
    public void KeepFileCheckbox_ReflectsDefault_WhenOn()
    {
        var cut = Render(out _, out _, keepFileDefault: true);
        Assert.True(Checkbox(cut).HasAttribute("checked"));
    }

    [Fact]
    public void KeepFileCheckbox_ReflectsDefault_WhenOff()
    {
        var cut = Render(out _, out _, keepFileDefault: false);
        Assert.False(Checkbox(cut).HasAttribute("checked"));
    }

    [Fact]
    public void StartRestore_EmitsResult_WithDefaultName_AndKeepFile()
    {
        var cut = Render(out var started, out _, keepFileDefault: true);
        StartButton(cut).Click();

        var result = Assert.Single(started);
        Assert.Equal(DefaultDb, result.TargetDatabaseName);
        Assert.True(result.KeepFile);
    }

    [Fact]
    public void StartRestore_EmitsEditedName_Trimmed()
    {
        var cut = Render(out var started, out _);
        TargetInput(cut).Input("  other_db  ");
        StartButton(cut).Click();

        var result = Assert.Single(started);
        Assert.Equal("other_db", result.TargetDatabaseName);
    }

    [Fact]
    public void StartRestore_IsDisabled_WhenTargetBlank()
    {
        var cut = Render(out var started, out _, defaultDb: "");
        Assert.True(StartButton(cut).HasAttribute("disabled"));

        // A blank target never emits, even if the click is forced through.
        StartButton(cut).Click();
        Assert.Empty(started);
    }

    [Fact]
    public void Cancel_FiresCancel_NotStart()
    {
        var cut = Render(out var started, out var cancelled);
        // The footer's first button is Cancel.
        cut.FindAll(".mxt-dialog__footer button")[0].Click();
        Assert.Equal(1, cancelled.Count);
        Assert.Empty(started);
    }

    // ---- helpers ----

    private sealed class Counter { public int Count; }

    private IRenderedComponent<RestoreDialog> Render(
        out List<RestoreDialogResult> started,
        out Counter cancelled,
        string defaultDb = DefaultDb,
        bool keepFileDefault = true)
    {
        var s = new List<RestoreDialogResult>();
        var c = new Counter();
        started = s;
        cancelled = c;
        return RenderComponent<RestoreDialog>(parameters => parameters
            .Add(p => p.Open, true)
            .Add(p => p.Created, "2026-07-22 09:12")
            .Add(p => p.DefaultDatabaseName, defaultDb)
            .Add(p => p.KeepFileDefault, keepFileDefault)
            .Add(p => p.OnStart, EventCallback.Factory.Create<RestoreDialogResult>(this, r => s.Add(r)))
            .Add(p => p.OnCancel, EventCallback.Factory.Create(this, () => c.Count++)));
    }

    private static IElement TargetInput(IRenderedComponent<RestoreDialog> cut)
        => cut.Find(".mxt-restore-dialog .mxt-input__control");

    private static IElement Checkbox(IRenderedComponent<RestoreDialog> cut)
        => cut.Find(".mxt-restore-dialog input[type=checkbox]");

    private static IElement StartButton(IRenderedComponent<RestoreDialog> cut)
        => cut.FindAll(".mxt-dialog__footer button")[1];
}
