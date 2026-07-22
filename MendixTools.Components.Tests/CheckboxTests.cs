using Bunit;
using Mendix_Tools.Components.UI;
using Xunit;

namespace MendixTools.Components.Tests;

/// <summary>
/// MT-04 bUnit follow-up (pinned in MT-08 DoD): an indeterminate ("mixed") Checkbox must
/// toggle to <c>checked</c>, not to unchecked — the deviation-prone "select all" behaviour.
/// </summary>
public sealed class CheckboxTests : TestContext
{
    [Fact]
    public void MixedCheckbox_TogglesToChecked_NotUnchecked()
    {
        var checkedValue = false;
        var cut = RenderComponent<Checkbox>(parameters => parameters
            .Add(p => p.Indeterminate, true)
            .Add(p => p.Checked, false)
            .Add(p => p.CheckedChanged, (bool v) => checkedValue = v));

        // A mixed checkbox advertises aria-checked="mixed" before interaction.
        var input = cut.Find("input[type=checkbox]");
        Assert.Equal("mixed", input.GetAttribute("aria-checked"));

        // Native checkbox change from the mixed state fires checked=true.
        input.Change(true);

        Assert.True(checkedValue);
    }

    [Fact]
    public void Checkbox_RendersLabel()
    {
        var cut = RenderComponent<Checkbox>(parameters => parameters
            .Add(p => p.Label, "Keep the downloaded .backup file"));

        Assert.Contains("Keep the downloaded .backup file", cut.Markup);
    }
}
