using AngleSharp.Dom;
using Bunit;
using Xunit;

namespace MendixTools.Components.Tests;

/// <summary>
/// MT-04 bUnit follow-up (pinned in MT-08 DoD): a Radio group (one <c>Name</c>, one bound
/// field) is single-selection — selecting one option clears the previously-selected sibling.
/// </summary>
public sealed class RadioTests : TestContext
{
    [Fact]
    public void SelectingOneOption_DeselectsThePeer()
    {
        var cut = RenderComponent<RadioGroupHarness>();

        var inputs = cut.FindAll("input[type=radio]");
        var clean = inputs[0];
        var merge = inputs[1];

        // Nothing selected initially.
        Assert.False(IsOn(clean));
        Assert.False(IsOn(merge));

        // Select "clean".
        clean.Change("clean");
        Assert.True(IsOn(cut.FindAll("input[type=radio]")[0]));
        Assert.False(IsOn(cut.FindAll("input[type=radio]")[1]));

        // Select "merge" — the previously-selected "clean" must clear.
        cut.FindAll("input[type=radio]")[1].Change("merge");
        Assert.False(IsOn(cut.FindAll("input[type=radio]")[0]));
        Assert.True(IsOn(cut.FindAll("input[type=radio]")[1]));

        // Exactly one selected at all times.
        Assert.Equal("merge", cut.Instance.Selected);
    }

    // A radio renders the ·dot· sibling and the --on circle class only when checked.
    private static bool IsOn(IElement input)
    {
        var circle = input.NextElementSibling;
        return circle is not null && circle.ClassList.Contains("mxt-radio__circle--on");
    }
}
