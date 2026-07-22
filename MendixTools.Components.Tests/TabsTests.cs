using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Xunit;

namespace MendixTools.Components.Tests;

/// <summary>
/// MT-04 bUnit follow-up (pinned in MT-08 DoD): Tabs keyboard index math —
/// ArrowRight/ArrowLeft move-and-select with wraparound, Home selects the first tab,
/// End selects the last. Items are [one, two, three].
/// </summary>
public sealed class TabsTests : TestContext
{
    public TabsTests()
    {
        // Tabs calls ElementReference.FocusAsync after a keyboard move (JS interop).
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void ArrowRight_MovesToNextTab()
    {
        var cut = RenderComponent<TabsHarness>();
        Key(cut, index: 0, "ArrowRight");
        Assert.Equal("two", cut.Instance.Active);
    }

    [Fact]
    public void ArrowRight_WrapsFromLastToFirst()
    {
        var cut = RenderComponent<TabsHarness>();
        Key(cut, index: 2, "ArrowRight");
        Assert.Equal("one", cut.Instance.Active);
    }

    [Fact]
    public void ArrowLeft_WrapsFromFirstToLast()
    {
        var cut = RenderComponent<TabsHarness>();
        Key(cut, index: 0, "ArrowLeft");
        Assert.Equal("three", cut.Instance.Active);
    }

    [Fact]
    public void Home_SelectsFirstTab()
    {
        var cut = RenderComponent<TabsHarness>();
        Key(cut, index: 2, "Home");
        Assert.Equal("one", cut.Instance.Active);
    }

    [Fact]
    public void End_SelectsLastTab()
    {
        var cut = RenderComponent<TabsHarness>();
        Key(cut, index: 0, "End");
        Assert.Equal("three", cut.Instance.Active);
    }

    private static void Key(IRenderedComponent<TabsHarness> cut, int index, string key)
    {
        var tab = cut.FindAll("button[role=tab]")[index];
        tab.KeyDown(new KeyboardEventArgs { Key = key });
    }
}
