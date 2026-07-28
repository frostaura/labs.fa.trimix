using Trimix.Core.Abstractions;
using Trimix.Core.Platform;
using Trimix.Core.Tests.Connectors.Cloud;
using Trimix.Core.Tests.Fakes;
using Trimix.Core.ViewModels;
using Xunit;

namespace Trimix.Core.Tests.ViewModels;

/// <summary>
/// The shared "Sync now" session (C5): unlinked taps route to Providers with the nudge, linked taps
/// land on the logbook and stream the run into the sheet strings/progress, CapReached presents the
/// paywall with the pending-dives count, and a NeedsInteraction source surfaces the relink toast.
/// </summary>
public class SyncSessionViewModelTests
{
    private readonly FakeSyncCoordinator _coordinator = new();
    private readonly InMemoryConnectorConsentStore _consent = new();
    private readonly FakeNavigator _navigator = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly InMemoryPreferences _preferences = new();

    private SyncSessionViewModel NewVm(IEnumerable<IDiveSourceConnector>? sources = null) => new(
        _coordinator, sources ?? [], _consent, _navigator, _dialogs, _preferences, new FakeClock())
    {
        AutoSyncDelayMs = 0,
        DoneLingerMs = 0,
    };

    [Fact]
    public async Task Sync_now_while_unlinked_routes_to_Providers_with_the_nudge()
    {
        _coordinator.HasLinkedSourcesValue = false;
        var vm = NewVm();

        await vm.SyncNowCommand.ExecuteAsync(null);

        var nav = Assert.Single(_navigator.Navigations);
        Assert.Equal(Routes.Providers, nav.Route);
        Assert.Equal("1", nav.Parameters![Routes.Params.Nudge]);
        Assert.Equal(0, _coordinator.SyncCalls);
    }

    [Fact]
    public async Task Sync_now_while_linked_lands_on_the_logbook_and_streams_the_run()
    {
        // The progress wrapper reports inline when no SynchronizationContext exists; xUnit installs
        // one, so drop it to observe ticks deterministically (production posts to the UI context).
        SynchronizationContext.SetSynchronizationContext(null);
        _coordinator.HasLinkedSourcesValue = true;
        await _consent.GrantAsync("garmin_connect");
        _coordinator.TicksToEmit.Add(new SyncProgress(SyncStage.Importing, "garmin_connect", 0, 1, 0, 0));
        _coordinator.TicksToEmit.Add(new SyncProgress(SyncStage.Importing, "garmin_connect", 1, 1, 3, 0));
        _coordinator.TicksToEmit.Add(new SyncProgress(SyncStage.Done, null, 1, 1, 3, 2));
        var syncSubs = new List<string>();
        var vm = NewVm([new FakeSourceConnector("garmin_connect")]);
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.SyncSub))
            {
                syncSubs.Add(vm.SyncSub);
            }
        };

        await vm.SyncNowCommand.ExecuteAsync(null);

        Assert.Equal(Routes.Logbook, _navigator.Navigations[0].Route);
        Assert.Equal(1, _coordinator.SyncCalls);
        Assert.False(vm.IsSyncing);
        Assert.Equal(1, vm.Progress);
        Assert.Equal("Finishing up", vm.SyncLabel);
        // The sheet's sub-line carried the prototype copy shape while streaming.
        Assert.Contains(syncSubs, s => s == "3 imported · checking Garmin Dive · PADI & SSI kept in sync");
        Assert.NotNull(_preferences.Get(PreferenceKeys.LastSyncUtc)); // last-sync instant persisted
        Assert.Equal("last sync just now", vm.LastSyncLabel);
    }

    [Fact]
    public async Task Cap_reached_presents_the_paywall_with_the_pending_dive_count()
    {
        _coordinator.HasLinkedSourcesValue = true;
        _coordinator.NextReport = new SyncReport(
            [new SourceSyncOutcome("garmin_connect", ConnectorAuthStatus.Granted, Imported: 5, Fetched: 8, CapReached: true)],
            [],
            [],
            CapReached: true);
        var vm = NewVm();

        await vm.SyncNowCommand.ExecuteAsync(null);

        var paywall = _navigator.Navigations.Last();
        Assert.Equal(Routes.Paywall, paywall.Route);
        Assert.Equal("3", paywall.Parameters![Routes.Params.PendingDives]); // 8 fetched − 5 imported
    }

    [Fact]
    public async Task Auth_expired_source_surfaces_the_relink_toast()
    {
        _coordinator.HasLinkedSourcesValue = true;
        _coordinator.NextReport = new SyncReport(
            [new SourceSyncOutcome("suunto_cloud", ConnectorAuthStatus.NeedsInteraction, 0, 0, false)],
            [],
            [],
            CapReached: false);
        var vm = NewVm();

        await vm.SyncNowCommand.ExecuteAsync(null);

        Assert.Contains(_dialogs.Toasts, t => t.Contains("Relink Suunto", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_failed_run_never_strands_the_sheet_open()
    {
        _coordinator.HasLinkedSourcesValue = true;
        var vm = NewVm();
        var throwing = new ThrowingCoordinator();
        var vmThrowing = new SyncSessionViewModel(
            throwing, [], _consent, _navigator, _dialogs, _preferences, new FakeClock())
        {
            DoneLingerMs = 0,
        };

        await vmThrowing.SyncNowCommand.ExecuteAsync(null);

        Assert.False(vmThrowing.IsSyncing);
        Assert.Contains(_dialogs.Toasts, t => t.Contains("Sync failed", StringComparison.Ordinal));
        _ = vm;
    }

    private sealed class ThrowingCoordinator : ISyncCoordinator
    {
        public bool HasLinkedSources => true;

        public Task<SyncReport> SyncNowAsync(
            Func<Task>? onCapReached = null,
            IProgress<SyncProgress>? progress = null,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("vendor endpoint broke");
    }
}
