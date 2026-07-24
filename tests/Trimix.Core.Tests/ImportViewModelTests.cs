using Microsoft.Extensions.Logging.Abstractions;
using Trimix.Contracts;
using Trimix.Contracts.Dtos;
using Trimix.Core.Abstractions;
using Trimix.Core.DiveComputer;
using Trimix.Core.DiveComputer.Protocols;
using Trimix.Core.Storage;
using Trimix.Core.Tests.Fakes;
using Trimix.Core.ViewModels;
using Xunit;

namespace Trimix.Core.Tests;

/// <summary>
/// PRD Flow B end-to-end over the simulator: permission → scan → import → free-cap purchase
/// moment (<c>paywall_shown</c> with <c>trigger=ble_import_cap</c>).
/// </summary>
public class ImportViewModelTests
{
    private readonly InMemoryLocalStore _store = new();
    private readonly FakeApiClient _api = new();
    private readonly FakeLocation _location = new();
    private readonly FakeEntitlementService _entitlements = new();
    private readonly RecordingAnalytics _analytics = new();
    private readonly FakeNavigator _navigator = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly FakePermissions _permissions = new();
    private readonly Guid _nearbySiteId = Guid.NewGuid();

    public ImportViewModelTests()
    {
        // Device is 1.2 km from a known site → inside the 2 km auto-match radius.
        _location.Fix = new GeoLocation(-34.1, 18.5);
        _api.SitesHandler = () => new SiteSearchResponse(
        [
            new SiteSummaryDto(_nearbySiteId, "Pinnacle Reef", -34.11, 18.51, 8, 30, "reef", 1.2),
            new SiteSummaryDto(Guid.NewGuid(), "Far Wreck", -35.0, 19.0, 20, 60, "wreck", 45.0),
        ]);
    }

    private ImportViewModel CreateViewModel()
    {
        var importService = new ImportService(
            [new BleDeviceSourceConnector(
                new SimulatorTransport(),
                [new ShearwaterProtocol(), new SuuntoProtocol(), new GarminProtocol(), new MaresProtocol()])],
            _api,
            _location,
            _entitlements,
            _store,
            _analytics,
            new FakeAppInfo(),
            new FakeClock(),
            NullLogger<ImportService>.Instance);
        return new ImportViewModel(importService, _permissions, _analytics, _navigator, _dialogs);
    }

    private async Task<ImportViewModel> RunFullImportAsync()
    {
        var vm = CreateViewModel();
        await vm.RequestPermissionAndScanCommand.ExecuteAsync(null);
        var device = Assert.Single(vm.Devices);
        await vm.ImportFromCommand.ExecuteAsync(device);
        return vm;
    }

    [Fact]
    public async Task Free_tier_cap_fires_the_paywall_with_ble_import_cap_trigger()
    {
        _entitlements.CurrentTier = EntitlementTier.Free;

        var vm = await RunFullImportAsync();

        // Fixture has 5 dives; free session cap is 3.
        Assert.Equal(FreeTier.FreeImportDivesPerSession, vm.ImportedCount);
        Assert.Equal(ShearwaterFixtures.Dives.Count, vm.FetchedCount);
        Assert.True(vm.CapReached);

        var paywall = Assert.Single(_analytics.Events, e => e.Name == FunnelEvents.PaywallShown);
        Assert.Equal(FunnelEvents.Values.TriggerBleImportCap, paywall.Props[FunnelEvents.Props.Trigger]);
        Assert.Equal(FunnelEvents.Values.EntitlementStateFree, paywall.Props[FunnelEvents.Props.EntitlementState]);
        Assert.Contains(_navigator.Navigations, n => n.Route == Routes.Paywall);

        Assert.Equal(FreeTier.FreeImportDivesPerSession, (await _store.GetDivesAsync()).Count);
    }

    [Fact]
    public async Task Funnel_events_fire_in_flow_order()
    {
        _entitlements.CurrentTier = EntitlementTier.Free;

        await RunFullImportAsync();

        var names = _analytics.Names;
        var expectedOrder = new[]
        {
            FunnelEvents.BlePermissionRequested,
            FunnelEvents.BlePermissionGranted,
            FunnelEvents.BleDeviceConnected,
            FunnelEvents.DivesFetched,
            FunnelEvents.DiveLoggedImport,
            FunnelEvents.PaywallShown,
        };
        var positions = expectedOrder.Select(n => names.ToList().IndexOf(n)).ToList();
        Assert.All(positions, p => Assert.True(p >= 0, "expected event missing"));
        Assert.True(positions.SequenceEqual(positions.OrderBy(p => p)), "events out of flow order");

        var connected = _analytics.Events.Single(e => e.Name == FunnelEvents.BleDeviceConnected);
        Assert.Equal("shearwater", connected.Props[FunnelEvents.Props.Vendor]);
        Assert.Equal("Perdix 2", connected.Props[FunnelEvents.Props.Model]);

        var fetched = _analytics.Events.Single(e => e.Name == FunnelEvents.DivesFetched);
        Assert.Equal("5", fetched.Props[FunnelEvents.Props.Count]);

        Assert.Equal(
            FreeTier.FreeImportDivesPerSession,
            _analytics.Events.Count(e => e.Name == FunnelEvents.DiveLoggedImport));
        Assert.All(
            _analytics.Events.Where(e => e.Name == FunnelEvents.DiveLoggedImport),
            e => Assert.Equal(FunnelEvents.Values.SourceImport, e.Props[FunnelEvents.Props.Source]));
    }

    [Fact]
    public async Task Reimport_after_upgrade_does_not_duplicate_dives()
    {
        // PRD Flow B step 5: free user imports (capped), buys Pro, re-imports "all dives". The
        // previously-imported dives must upsert onto the same deterministic ids, not duplicate.
        _entitlements.CurrentTier = EntitlementTier.Free;
        await RunFullImportAsync();
        Assert.Equal(FreeTier.FreeImportDivesPerSession, (await _store.GetDivesAsync()).Count);

        _entitlements.CurrentTier = EntitlementTier.Pro;
        await RunFullImportAsync();

        // 5 unique dives total, no duplicates of the 3 imported while free.
        Assert.Equal(ShearwaterFixtures.Dives.Count, (await _store.GetDivesAsync()).Count);
    }

    [Fact]
    public async Task Paid_tier_imports_everything_with_no_paywall()
    {
        _entitlements.CurrentTier = EntitlementTier.Pro;

        var vm = await RunFullImportAsync();

        Assert.Equal(ShearwaterFixtures.Dives.Count, vm.ImportedCount);
        Assert.False(vm.CapReached);
        Assert.DoesNotContain(_analytics.Events, e => e.Name == FunnelEvents.PaywallShown);
        Assert.Empty(_navigator.Navigations);
        Assert.Equal(ShearwaterFixtures.Dives.Count, (await _store.GetDivesAsync()).Count);
    }

    [Fact]
    public async Task Imported_dives_auto_match_the_nearest_site_within_2km()
    {
        _entitlements.CurrentTier = EntitlementTier.Lifetime;

        var vm = await RunFullImportAsync();

        Assert.True(vm.SiteMatched);
        Assert.All(await _store.GetDivesAsync(), d =>
        {
            Assert.Equal(_nearbySiteId, d.SiteId);
            Assert.Equal(DiveSources.Import, d.Source);
            Assert.Equal("shearwater", d.SourceVendor);
            Assert.NotEmpty(d.ProfileSamples);
        });
    }

    [Fact]
    public async Task No_location_fix_imports_without_site_match()
    {
        _entitlements.CurrentTier = EntitlementTier.Pro;
        _location.Fix = null;

        var vm = await RunFullImportAsync();

        Assert.False(vm.SiteMatched);
        Assert.All(await _store.GetDivesAsync(), d => Assert.Null(d.SiteId));
    }

    [Fact]
    public async Task Permission_denied_stops_the_flow_and_tracks_the_outcome()
    {
        _permissions.GrantBluetooth = false;
        var vm = CreateViewModel();

        await vm.RequestPermissionAndScanCommand.ExecuteAsync(null);

        Assert.Equal(ImportStep.PermissionDenied, vm.Step);
        Assert.Empty(vm.Devices);
        Assert.Equal(
            new[] { FunnelEvents.BlePermissionRequested, FunnelEvents.BlePermissionDenied },
            _analytics.Names);
    }
}
