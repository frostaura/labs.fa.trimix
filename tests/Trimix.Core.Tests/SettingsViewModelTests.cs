using Trimix.Core;
using Trimix.Core.Platform;
using Trimix.Core.Storage;
using Trimix.Core.Tests.Connectors.Push;
using Trimix.Core.Tests.Fakes;
using Trimix.Core.ViewModels;
using Xunit;

namespace Trimix.Core.Tests;

/// <summary>
/// Settings actions, serverless with no accounts: "Delete my data" is a local wipe (dives + every
/// connector consent+token, plus the cached paid tier), "Reset app" is the superset factory reset that
/// also clears preferences and replays onboarding, and "Replay onboarding" is a pure re-show that
/// touches no data.
/// </summary>
public sealed class SettingsViewModelTests
{
    private sealed record Sut(
        SettingsViewModel Vm,
        InMemoryLocalStore Store,
        InMemoryPreferences Preferences,
        FakeDialogs Dialogs,
        RecordingConnectorPurgeService Purge,
        FakeEntitlementService Entitlements,
        FakeNavigator Navigator);

    private static Sut NewSut()
    {
        var store = new InMemoryLocalStore();
        var preferences = new InMemoryPreferences();
        var dialogs = new FakeDialogs { ConfirmResult = true };
        var purge = new RecordingConnectorPurgeService();
        var entitlements = new FakeEntitlementService();
        var navigator = new FakeNavigator();
        var vm = new SettingsViewModel(
            entitlements,
            preferences,
            store,
            new FakeAppInfo(),
            navigator,
            dialogs,
            purge);
        return new Sut(vm, store, preferences, dialogs, purge, entitlements, navigator);
    }

    [Fact]
    public async Task DeleteMyData_wipes_local_data_purges_connectors_and_clears_tier()
    {
        var sut = NewSut();
        await sut.Store.UpsertDiveAsync(DiveFixture.Trimix());

        await sut.Vm.DeleteMyDataCommand.ExecuteAsync(null);

        Assert.Equal(1, sut.Purge.PurgeAllCalls);              // connector consent+tokens purged (awaited)
        Assert.Equal(1, sut.Entitlements.ClearLocalGrantCalls); // paid tier dropped to Free
        Assert.All(await sut.Store.GetDivesAsync(), d => Assert.True(d.Deleted));
        Assert.Contains("Your data has been deleted.", sut.Dialogs.Toasts);
    }

    [Fact]
    public async Task DeleteMyData_declined_does_nothing()
    {
        var sut = NewSut();
        await sut.Store.UpsertDiveAsync(DiveFixture.Trimix());
        sut.Dialogs.ConfirmResult = false;

        await sut.Vm.DeleteMyDataCommand.ExecuteAsync(null);

        Assert.Equal(0, sut.Purge.PurgeAllCalls);              // declined => nothing erased or purged
        Assert.Equal(0, sut.Entitlements.ClearLocalGrantCalls);
        Assert.All(await sut.Store.GetDivesAsync(), d => Assert.False(d.Deleted));
        Assert.Empty(sut.Dialogs.Toasts);
    }

    [Fact]
    public async Task ResetApp_factory_resets_clears_prefs_and_replays_onboarding()
    {
        var sut = NewSut();
        await sut.Store.UpsertDiveAsync(DiveFixture.Trimix());
        sut.Preferences.Set(PreferenceKeys.FirstRunTracked, "1");
        sut.Preferences.Set(PreferenceKeys.DepthUnit, "ft");
        sut.Preferences.Set(PreferenceKeys.UserName, "Dean");

        await sut.Vm.ResetAppCommand.ExecuteAsync(null);

        Assert.Equal(1, sut.Purge.PurgeAllCalls);
        Assert.Equal(1, sut.Entitlements.ClearLocalGrantCalls);
        Assert.All(await sut.Store.GetDivesAsync(), d => Assert.True(d.Deleted));
        // Every on-device setting is cleared — a fresh-install state.
        Assert.Null(sut.Preferences.Get(PreferenceKeys.FirstRunTracked));
        Assert.Null(sut.Preferences.Get(PreferenceKeys.DepthUnit));
        Assert.Null(sut.Preferences.Get(PreferenceKeys.UserName));
        // Onboarding replays.
        Assert.Contains(sut.Navigator.Navigations, n => n.Route == Routes.Onboarding);
    }

    [Fact]
    public async Task ResetApp_declined_does_nothing()
    {
        var sut = NewSut();
        await sut.Store.UpsertDiveAsync(DiveFixture.Trimix());
        sut.Preferences.Set(PreferenceKeys.FirstRunTracked, "1");
        sut.Dialogs.ConfirmResult = false;

        await sut.Vm.ResetAppCommand.ExecuteAsync(null);

        Assert.Equal(0, sut.Purge.PurgeAllCalls);
        Assert.Equal("1", sut.Preferences.Get(PreferenceKeys.FirstRunTracked)); // untouched
        Assert.All(await sut.Store.GetDivesAsync(), d => Assert.False(d.Deleted));
        Assert.Empty(sut.Navigator.Navigations);
    }

    [Fact]
    public async Task ReplayOnboarding_re_shows_onboarding_without_touching_any_data()
    {
        var sut = NewSut();
        await sut.Store.UpsertDiveAsync(DiveFixture.Trimix());
        sut.Preferences.Set(PreferenceKeys.FirstRunTracked, "1");

        await sut.Vm.ReplayOnboardingCommand.ExecuteAsync(null);

        Assert.Contains(sut.Navigator.Navigations, n => n.Route == Routes.Onboarding);
        // A pure re-show: dives, links, tier and first-run state all untouched.
        Assert.Equal(0, sut.Purge.PurgeAllCalls);
        Assert.Equal(0, sut.Entitlements.ClearLocalGrantCalls);
        Assert.Equal("1", sut.Preferences.Get(PreferenceKeys.FirstRunTracked));
        Assert.All(await sut.Store.GetDivesAsync(), d => Assert.False(d.Deleted));
    }
}
