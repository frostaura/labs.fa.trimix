using Trimix.Core.Charts;
using Trimix.Core.Formatting;
using Trimix.Core.Platform;
using Trimix.Core.Storage;
using Trimix.Core.Tests.Fakes;
using Trimix.Core.ViewModels;
using Xunit;
using static Trimix.Core.Tests.Stats.StatsTestFactory;

namespace Trimix.Core.Tests;

/// <summary>
/// Dive-detail integration of the stats surface: recorded-channel chart overlays, the columnar
/// (unit-formatted) WCAG table, cross-source merge badges, and display-only recorded NDL/ceiling
/// readouts. Guardrail: readouts surface recorded values only; nothing computes deco.
/// </summary>
public class DiveDetailViewModelTests
{
    private readonly InMemoryLocalStore _store = new();
    private readonly FakeApiClient _api = new();
    private readonly UnitFormatter _units = new(new InMemoryPreferences());
    private readonly FakeNavigator _navigator = new();

    private DiveDetailViewModel CreateViewModel() => new(_store, _api, _units, _navigator);

    [Fact]
    public async Task Builds_recorded_overlays_and_the_columnar_table()
    {
        var dive = Dive(samples:
        [
            Sample(0, 0, temp: 22, tank: 200),
            Sample(60, 15, temp: 20, tank: 180),
            Sample(120, 28, temp: 18, tank: 150),
        ]);
        await _store.UpsertDiveAsync(dive);

        var vm = CreateViewModel();
        await vm.LoadCommand.ExecuteAsync(dive.Id);

        Assert.True(vm.Overlays.HasAny);
        Assert.NotNull(vm.Overlays.Temperature);
        Assert.NotNull(vm.Overlays.TankPressure);
        Assert.Contains("Depth", vm.ProfileTableHeaders);
        Assert.Contains("Temp", vm.ProfileTableHeaders);
        Assert.Contains("Tank", vm.ProfileTableHeaders);
        Assert.Equal(dive.ProfileSamples.Count, vm.ProfileTable.Count);
        // Depth cell is unit-formatted (default metric): "15 m".
        var depthColumn = vm.ProfileTableHeaders.ToList().IndexOf("Depth");
        Assert.Equal("15 m", vm.ProfileTable[1].Cells[depthColumn]);
        // Fixed-unit channel cells carry their unit so the accessible table is unambiguous ("180 bar").
        var tankColumn = vm.ProfileTableHeaders.ToList().IndexOf("Tank");
        Assert.Equal("180 bar", vm.ProfileTable[1].Cells[tankColumn]);
    }

    [Fact]
    public async Task Chart_semantic_description_names_the_recorded_overlay_channels()
    {
        // WCAG text alternative: the description must reflect the multi-channel canvas, not a static
        // "Depth over time", and must count the recorded event markers (here an ascent-warning tick).
        var dive = Dive(samples:
        [
            Sample(0, 0, temp: 22, tank: 200, ceiling: 0),
            Sample(60, 30, temp: 18, tank: 150, ceiling: 4, ascent: 14),
        ]);
        await _store.UpsertDiveAsync(dive);

        var vm = CreateViewModel();
        await vm.LoadCommand.ExecuteAsync(dive.Id);

        Assert.Contains("temperature", vm.ChartSemanticDescription);
        Assert.Contains("tank pressure", vm.ChartSemanticDescription);
        Assert.Contains("recorded ceiling", vm.ChartSemanticDescription);
        Assert.Contains("recorded event marker", vm.ChartSemanticDescription);
        Assert.True(vm.HasOverlayLegend);
        Assert.True(vm.HasTemperatureOverlay);
        Assert.True(vm.HasCeilingOverlay);
    }

    [Fact]
    public async Task Merge_cluster_surfaces_source_badges_and_richest_union()
    {
        var shearwater = Dive(
            start: BaseUtc, durationSec: 3000, maxDepthM: 30, sourceVendor: "shearwater",
            samples: [Sample(0, 0, temp: 21, tank: 200), Sample(60, 30, temp: 19, tank: 150)]);
        var appleWatch = Dive(
            start: BaseUtc.AddSeconds(10), durationSec: 2990, maxDepthM: 30.5,
            sourceVendor: "apple_health", samples: [Sample(0, 0, temp: 21)]);
        await _store.UpsertDiveAsync(shearwater);
        await _store.UpsertDiveAsync(appleWatch);

        var vm = CreateViewModel();
        // Open via the depth-temp-only row's id — must still resolve to the merged union.
        await vm.LoadCommand.ExecuteAsync(appleWatch.Id);

        Assert.True(vm.IsMerged);
        Assert.Contains("Shearwater", vm.SourceBadges);
        Assert.Contains("Apple Watch", vm.SourceBadges);
        Assert.Contains(vm.Dive!.ProfileSamples, s => s.TankPressureBar is not null);
    }

    [Fact]
    public async Task Surfaces_recorded_ndl_and_ceiling_without_computing_deco()
    {
        // Recorded (imported) deco channels — the readouts are the min/max of these stored values,
        // never a computed tissue model.
        var dive = Dive(samples:
        [
            Sample(0, 0, ndl: 99),
            Sample(600, 40, ndl: 5, ceiling: 3),
            Sample(900, 40, ndl: 0, ceiling: 6),
        ]);
        await _store.UpsertDiveAsync(dive);

        var vm = CreateViewModel();
        await vm.LoadCommand.ExecuteAsync(dive.Id);

        var ndl = Assert.Single(vm.RecordedReadouts, r => r.Label == "Min NDL (recorded)");
        Assert.Equal("0 min", ndl.Value);
        var ceiling = Assert.Single(vm.RecordedReadouts, r => r.Label == "Max ceiling (recorded)");
        Assert.Equal("6 m", ceiling.Value);
        // A deco-onset marker surfaces the recorded flag (never computed).
        Assert.Contains(vm.Overlays.Markers, m => m.Kind == ChartMarkerKind.DecoOnset);
    }

    [Fact]
    public async Task Missing_dive_navigates_back()
    {
        var vm = CreateViewModel();
        await vm.LoadCommand.ExecuteAsync(Guid.NewGuid());

        Assert.Equal(1, _navigator.BackCount);
        Assert.Null(vm.Dive);
    }
}
