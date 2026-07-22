using Mendix_Tools.Components.Layout;
using Xunit;

namespace MendixTools.Core.Tests;

/// <summary>
/// MT-05/06/07 review follow-up (pinned in MT-08 DoD): ThemeService is default-light,
/// Toggle flips the theme, and the choice round-trips through persistence across a
/// simulated restart. The persistence seam (<see cref="IThemeStore"/>) is faked so the
/// service's logic is verified without the MAUI runtime.
/// </summary>
public sealed class ThemeServiceTests
{
    [Fact]
    public void Default_IsLight()
    {
        var service = new ThemeService(new FakeThemeStore());

        Assert.False(service.IsDark);
        Assert.Equal("light", service.Current);
    }

    [Fact]
    public void Toggle_FlipsTheme()
    {
        var service = new ThemeService(new FakeThemeStore());

        Assert.Equal("dark", service.Toggle());
        Assert.True(service.IsDark);

        Assert.Equal("light", service.Toggle());
        Assert.False(service.IsDark);
    }

    [Fact]
    public void Choice_PersistsAcrossRestart()
    {
        var store = new FakeThemeStore();

        // First "session": switch to dark.
        var first = new ThemeService(store);
        first.Toggle();
        Assert.True(first.IsDark);

        // Second "session" (new instance, same backing store) restores dark.
        var second = new ThemeService(store);
        Assert.True(second.IsDark);
        Assert.Equal("dark", second.Current);
    }

    /// <summary>In-memory <see cref="IThemeStore"/> standing in for MAUI Preferences.</summary>
    private sealed class FakeThemeStore : IThemeStore
    {
        private readonly Dictionary<string, string> _values = new();

        public string Get(string key, string defaultValue) =>
            _values.TryGetValue(key, out var value) ? value : defaultValue;

        public void Set(string key, string value) => _values[key] = value;
    }
}
