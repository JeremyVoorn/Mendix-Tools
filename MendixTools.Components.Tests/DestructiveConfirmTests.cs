using AngleSharp.Dom;
using Bunit;
using Mendix_Tools.Components.UI;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Xunit;

namespace MendixTools.Components.Tests;

/// <summary>
/// MT-19 — the Tier 2 typed-identifier guard (resolved decision D5). Verifies the match rule
/// (trim then exact + case-sensitive), the disabled-until-match confirm button, and the inert
/// cancel/Esc paths. The token in these tests is the restore example's <c>acme_local</c>.
/// </summary>
public sealed class DestructiveConfirmTests : TestContext
{
    private const string Token = "acme_local";

    public DestructiveConfirmTests()
    {
        // The reused Dialog primitive imports mxt-interop.js and installs a focus trap on render.
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void ConfirmButton_DisabledInitially()
    {
        var cut = Render(out _, out _);
        Assert.True(IsDisabled(ConfirmButton(cut)));
    }

    [Fact]
    public void ConfirmButton_StaysDisabled_WhileTypedValueDiffers()
    {
        var cut = Render(out _, out _);
        Type(cut, "acme_loca"); // one char short
        Assert.True(IsDisabled(ConfirmButton(cut)));
    }

    [Fact]
    public void ConfirmButton_Enables_OnExactMatch()
    {
        var cut = Render(out _, out _);
        Type(cut, Token);
        Assert.False(IsDisabled(ConfirmButton(cut)));
    }

    [Fact]
    public void Match_TrimsLeadingAndTrailingWhitespace()
    {
        var cut = Render(out _, out _);
        Type(cut, "  acme_local  ");
        Assert.False(IsDisabled(ConfirmButton(cut)));
    }

    [Fact]
    public void Match_IsCaseSensitive()
    {
        var cut = Render(out _, out _);
        Type(cut, "ACME_LOCAL");
        Assert.True(IsDisabled(ConfirmButton(cut)));
    }

    [Fact]
    public void OnConfirm_Fires_WhenEnabledButtonClicked()
    {
        var cut = Render(out var confirmed, out _);
        Type(cut, Token);
        ConfirmButton(cut).Click();
        Assert.Equal(1, confirmed.Count);
    }

    [Fact]
    public void Cancel_FiresClose_NotConfirm()
    {
        var cut = Render(out var confirmed, out var closed);
        // The footer's first button is Cancel.
        cut.FindAll(".mxt-dialog__footer button")[0].Click();
        Assert.Equal(1, closed.Count);
        Assert.Equal(0, confirmed.Count);
    }

    [Fact]
    public void Escape_FiresClose_NotConfirm()
    {
        var cut = Render(out var confirmed, out var closed);
        cut.Find(".mxt-dialog__overlay").KeyDown(new KeyboardEventArgs { Key = "Escape" });
        Assert.Equal(1, closed.Count);
        Assert.Equal(0, confirmed.Count);
    }

    [Fact]
    public void ReTypingToNonMatch_ReDisablesButton()
    {
        var cut = Render(out _, out _);
        Type(cut, Token);
        Assert.False(IsDisabled(ConfirmButton(cut))); // enabled on match

        Type(cut, "acme_local_x"); // no longer matches
        Assert.True(IsDisabled(ConfirmButton(cut)));
    }

    [Fact]
    public void Consequence_RendersIdentifierInMono()
    {
        var cut = Render(out _, out _);
        var mono = cut.Find(".mxt-dconfirm__id");
        Assert.Equal(Token, mono.TextContent);
    }

    // ---- helpers ----

    private sealed class Counter { public int Count; }

    private IRenderedComponent<DestructiveConfirm> Render(out Counter confirmed, out Counter closed)
    {
        var c = new Counter();
        var x = new Counter();
        confirmed = c;
        closed = x;
        return RenderComponent<DestructiveConfirm>(parameters => parameters
            .Add(p => p.Open, true)
            .Add(p => p.Title, "Drop and recreate acme_local")
            .Add(p => p.ConsequenceText,
                "This drops and recreates `acme_local`. Open connections will be terminated. This cannot be undone.")
            .Add(p => p.TokenLabel, "database name")
            .Add(p => p.TokenValue, Token)
            .Add(p => p.ConfirmLabel, "Drop and restore")
            .Add(p => p.OnConfirm, EventCallback.Factory.Create(this, () => c.Count++))
            .Add(p => p.OnClose, EventCallback.Factory.Create(this, () => x.Count++)));
    }

    private static void Type(IRenderedComponent<DestructiveConfirm> cut, string value)
        => cut.Find(".mxt-dconfirm input").Input(value);

    private static IElement ConfirmButton(IRenderedComponent<DestructiveConfirm> cut)
        => cut.FindAll(".mxt-dialog__footer button")[1];

    private static bool IsDisabled(IElement button) => button.HasAttribute("disabled");
}
