using Trimix.Core.Formatting;
using Trimix.Core.Platform;
using Trimix.Core.Storage;
using Trimix.Core.Tests.Fakes;
using Trimix.Core.ViewModels;
using Xunit;
using static Trimix.Core.Tests.Stats.StatsTestFactory;

namespace Trimix.Core.Tests;

/// <summary>
/// The "You" surface (StatsViewModel): renders <see cref="Trimix.Core.Stats.DiveStats"/> career
/// metrics with verbatim data-completeness captions and emits no funnel events.
/// </summary>
public class StatsViewModelTests
{
    private readonly InMemoryLocalStore _store = new();
    private readonly UnitFormatter _units = new(new InMemoryPreferences());
    private readonly FakeNavigator _navigator = new();

    private StatsViewModel CreateViewModel() => new(
        _store,
        _units,
        _navigator,
        new InMemoryPreferences(),
        new FakeEntitlementService(),
        new Trimix.Core.Tests.Connectors.Cloud.InMemoryConnectorConsentStore(),
        [],
        []);

    [Fact]
    public async Task Empty_store_reports_empty_state()
    {
        var vm = CreateViewModel();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.True(vm.IsEmpty);
        Assert.Equal(0, vm.TotalDives);
    }

    [Fact]
    public async Task Computes_summary_and_completeness_captions()
    {
        // Two SAC-capable dives (tank consumed + volume) + one depth-temp-only dive.
        var withGas = new[]
        {
            Dive(start: BaseUtc, durationSec: 3000, maxDepthM: 20, avgDepthM: 15,
                cylinders: [Cyl(o2: 21, startBar: 200, endBar: 100, volumeL: 12)]),
            Dive(start: BaseUtc.AddDays(1), durationSec: 2400, maxDepthM: 18, avgDepthM: 12,
                cylinders: [Cyl(o2: 32, startBar: 210, endBar: 120, volumeL: 12)]),
        };
        var depthOnly = Dive(start: BaseUtc.AddDays(2), durationSec: 1800, maxDepthM: 10,
            sourceVendor: "apple_health");
        foreach (var dive in withGas)
        {
            await _store.UpsertDiveAsync(dive);
        }

        await _store.UpsertDiveAsync(depthOnly);

        var vm = CreateViewModel();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.False(vm.IsEmpty);
        Assert.Equal(3, vm.TotalDives);
        // SAC computable for the two gas dives, not the depth-temp-only one.
        Assert.Equal("SAC available for 2 of 3 dives", vm.SacCaption);
        Assert.NotEqual("—", vm.SacText);
        Assert.Equal("Gas mix available for 2 of 3 dives", vm.GasMixCaption);
        Assert.Contains("Nitrox", vm.GasMixText);
    }

    [Fact]
    public async Task Career_totals_dedupe_cross_source_duplicates_of_one_physical_dive()
    {
        // ONE physical dive imported from two sources is two vendor-keyed raw rows by design. The "You" totals
        // must count it once, not twice — the bug this guards is counts/sums inflating with each connected
        // source while the logbook card below shows the dive once.
        await _store.UpsertDiveAsync(Dive(start: BaseUtc, durationSec: 3000, maxDepthM: 20, sourceVendor: "shearwater"));
        await _store.UpsertDiveAsync(Dive(start: BaseUtc, durationSec: 3000, maxDepthM: 20, sourceVendor: "apple_health"));

        var vm = CreateViewModel();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Equal(1, vm.TotalDives); // one physical dive, not two raw rows
        Assert.Equal(_units.FormatDuration(3000), vm.TotalBottomTimeText); // bottom time not doubled
    }

    [Fact]
    public async Task Rmv_caption_reports_its_own_subset_not_the_sac_completeness()
    {
        // SAC needs a consumed cylinder; RMV additionally needs a recorded VolumeL. Here two dives have SAC but
        // only one has a volume, so SAC is 2/2 while RMV is 1/2 — the RMV caption must not claim the SAC basis.
        await _store.UpsertDiveAsync(Dive(start: BaseUtc, durationSec: 3000, maxDepthM: 20, avgDepthM: 15,
            cylinders: [Cyl(o2: 21, startBar: 200, endBar: 100, volumeL: 12)]));
        await _store.UpsertDiveAsync(Dive(start: BaseUtc.AddDays(1), durationSec: 2400, maxDepthM: 18, avgDepthM: 12,
            cylinders: [Cyl(o2: 32, startBar: 210, endBar: 120)])); // no VolumeL → SAC yes, RMV no

        var vm = CreateViewModel();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Equal("SAC available for 2 of 2 dives", vm.SacCaption);
        Assert.Equal("RMV available for 1 of 2 dives", vm.RmvCaption);
    }

    [Fact]
    public async Task Open_settings_navigates_to_the_settings_route()
    {
        var vm = CreateViewModel();
        await vm.OpenSettingsCommand.ExecuteAsync(null);

        Assert.Contains(_navigator.Navigations, n => n.Route == Routes.Settings);
    }
}
