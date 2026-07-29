using Trimix.Core;
using Trimix.Core.Abstractions;
using Trimix.Core.Platform;
using Xunit;

namespace Trimix.Core.Tests;

/// <summary>
/// The dev/demo-mode seam: persisted toggle + live connector filtering. The filter must be a
/// structural no-op when no demo connectors exist (Release) and a clean either/or swap when they do.
/// </summary>
public sealed class DemoModeStateTests
{
    private static readonly string[] Mixed = ["garmin_connect", "demo_garmin", "suunto_cloud", "demo_suunto"];

    [Fact]
    public void Enabled_persists_to_preferences_and_raises_Changed()
    {
        var preferences = new InMemoryPreferences();
        var state = new DemoModeState(preferences);
        var observed = new List<bool>();
        state.Changed += (_, on) => observed.Add(on);

        Assert.False(state.Enabled);
        state.Enabled = true;
        state.Enabled = true; // idempotent — no duplicate event
        state.Enabled = false;

        Assert.Equal([true, false], observed);
        Assert.Equal("0", preferences.Get(PreferenceKeys.DevDemoMode));

        // A fresh instance over the same preferences reads the persisted value.
        preferences.Set(PreferenceKeys.DevDemoMode, "1");
        Assert.True(new DemoModeState(preferences).Enabled);
    }

    [Fact]
    public void SelectActive_swaps_between_the_real_and_demo_families()
    {
        var state = new DemoModeState(new InMemoryPreferences());

        Assert.Equal(["garmin_connect", "suunto_cloud"], DemoModeState.SelectActive(state, Mixed, id => id));

        state.Enabled = true;
        Assert.Equal(["demo_garmin", "demo_suunto"], DemoModeState.SelectActive(state, Mixed, id => id));
    }

    [Fact]
    public void SelectActive_is_a_no_op_without_demo_connectors_or_without_a_state()
    {
        var state = new DemoModeState(new InMemoryPreferences()) { Enabled = true };
        string[] realOnly = ["garmin_connect", "suunto_cloud"];

        // Enabled but nothing demo registered (Release shape): real connectors stay active.
        Assert.Equal(realOnly, DemoModeState.SelectActive(state, realOnly, id => id));

        // No state at all (plain test construction): pass-through, demo ids included.
        Assert.Equal(Mixed, DemoModeState.SelectActive(null, Mixed, id => id));
    }
}
