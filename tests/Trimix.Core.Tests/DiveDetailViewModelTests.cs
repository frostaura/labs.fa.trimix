using Trimix.Contracts.Dtos;
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
    private readonly UnitFormatter _units = new(new InMemoryPreferences());
    private readonly FakeNavigator _navigator = new();
    private readonly RecordingMapLauncher _maps = new();

    private DiveDetailViewModel CreateViewModel() => new(
        _store,
        _units,
        _navigator,
        new FakeClock(),
        new Trimix.Core.Tests.Connectors.Cloud.InMemoryConnectorConsentStore(),
        new FakeProviderPriority(),
        [],
        _maps);

    [Fact]
    public async Task Merged_dive_exposes_tappable_sources_and_drills_in_place()
    {
        var gid = Guid.NewGuid();
        var sid = Guid.NewGuid();
        var t0 = new DateTime(2026, 3, 10, 9, 0, 0, DateTimeKind.Utc);
        // Overlapping windows → one cluster. Garmin is the richer profile, so (neutral priority) it is primary.
        var garmin = Dive(id: gid, start: t0, durationSec: 3000, maxDepthM: 30, sourceVendor: "garmin_connect",
            samples: [Sample(0, 0, temp: 20, ndl: 99, tank: 200), Sample(60, 30, temp: 18, ndl: 5, tank: 150)]);
        var scuba = Dive(id: sid, start: t0.AddSeconds(30), durationSec: 3010, maxDepthM: 30.2,
            sourceVendor: "scubapro", notes: "from scubapro", samples: [Sample(0, 0, temp: 21)]);
        await _store.UpsertDiveAsync(garmin);
        await _store.UpsertDiveAsync(scuba);
        var vm = CreateViewModel();

        await vm.LoadCommand.ExecuteAsync(gid);

        Assert.True(vm.IsMerged);
        Assert.Equal(2, vm.SourceOptions.Count);
        Assert.True(vm.SourceOptions[0].IsPrimary);   // richer garmin leads
        Assert.Equal(gid, vm.Dive!.Id);               // primary shown by default
        Assert.True(vm.SourceOptions.Single(s => s.Id == gid).IsSelected);

        await vm.SelectSourceCommand.ExecuteAsync(sid); // drill into ScubaPro's own recording

        Assert.Equal(sid, vm.Dive!.Id);
        Assert.Equal("scubapro", vm.Dive!.SourceVendor);
        Assert.True(vm.SourceOptions.Single(s => s.Id == sid).IsSelected);
        Assert.False(vm.SourceOptions.Single(s => s.Id == gid).IsSelected);
    }

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

        // Temperature now lives on its own canvas rather than as a main-chart overlay.
        Assert.NotNull(vm.TemperatureLane);
        Assert.True(vm.HasTemperatureLane);
        Assert.True(vm.IsLoaded);

        // The accessible table is built on FIRST OPEN, not on load — building 150 rows x 10 unit-formatted
        // cells during load cost a visible delay for something the diver had not asked to see.
        Assert.Empty(vm.ProfileTableHeaders);
        Assert.Empty(vm.ProfileTable);
        vm.ToggleProfileTableCommand.Execute(null);
        Assert.True(vm.IsProfileTableVisible);

        // Tank pressure is no longer DRAWN, but the data path survives — it is still a table column below.
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

        // Closing and reopening must not rebuild it.
        var built = vm.ProfileTable;
        vm.ToggleProfileTableCommand.Execute(null);
        vm.ToggleProfileTableCommand.Execute(null);
        Assert.Same(built, vm.ProfileTable);
    }

    [Fact]
    public async Task The_page_is_not_marked_loaded_until_everything_is_populated()
    {
        // The page keeps its sections hidden until IsLoaded flips, so this flag is what prevents the diver
        // watching an empty skeleton pop into real data.
        var dive = Dive(samples: [Sample(0, 0, temp: 26), Sample(60, 20, temp: 27)]);
        await _store.UpsertDiveAsync(dive);

        var vm = CreateViewModel();
        Assert.False(vm.IsLoaded);

        await vm.LoadCommand.ExecuteAsync(dive.Id);

        Assert.True(vm.IsLoaded);
        Assert.NotEqual("", vm.MaxDepthText);
        Assert.NotEqual("", vm.DepthFloorText);
        Assert.NotEmpty(vm.Panes);
    }

    [Fact]
    public async Task A_missing_dive_never_marks_the_page_loaded()
    {
        var vm = CreateViewModel();

        await vm.LoadCommand.ExecuteAsync(Guid.NewGuid());

        Assert.False(vm.IsLoaded);
    }

    [Fact]
    public async Task Chart_semantic_descriptions_describe_each_canvas_truthfully()
    {
        // WCAG text alternative. The main description must describe only what the main canvas draws — so it no
        // longer claims temperature (own canvas, own description) or tank pressure (never drawn at all) — and it
        // must now state the depth range and average, which a sighted user reads off the axis but which were
        // previously announced to nobody.
        // The ceiling run is deliberately TWO consecutive samples: a lone ceiling sample is a zero-width
        // sliver and no longer emits a band, so a one-sample fixture would not exercise the ceiling path.
        var dive = Dive(samples:
        [
            Sample(0, 0, temp: 22, tank: 200, ceiling: 0),
            Sample(60, 30, temp: 18, tank: 150, ceiling: 4, ascent: 14),
            Sample(120, 12, temp: 18, tank: 120, ceiling: 3),
        ]);
        await _store.UpsertDiveAsync(dive);

        var vm = CreateViewModel();
        await vm.LoadCommand.ExecuteAsync(dive.Id);

        Assert.Contains("surface to", vm.ChartSemanticDescription);
        Assert.Contains("Recorded ceiling", vm.ChartSemanticDescription);
        // Event markers were removed from the chart (the provider's own alert list on the Events section is
        // more authoritative), so the description must no longer claim them either.
        Assert.DoesNotContain("event marker", vm.ChartSemanticDescription);
        Assert.DoesNotContain("tank pressure", vm.ChartSemanticDescription);
        Assert.True(vm.HasOverlayLegend);
        Assert.True(vm.HasCeilingOverlay);

        // The lane carries its own description, so a screen-reader user learns a second canvas exists.
        Assert.True(vm.HasTemperatureLane);
        Assert.Contains("temperature", vm.TemperatureLaneSemanticDescription);
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

    // ── depth axis, tile averages, scrub, panes, map ─────────────────────────────────────────────────

    [Fact]
    public async Task Depth_axis_labels_describe_the_plot_not_the_provider_summary()
    {
        // The summary claims 30 m but the deepest retained sample is 28 m. The chart's floor IS that sample, so
        // the axis label must say 28 — labelling it 30 annotates the hairline with a value it does not
        // represent. The MAX DEPTH tile keeps the summary figure: that is the diver's logbook number.
        var dive = Dive(maxDepthM: 30, samples:
        [
            Sample(0, 0),
            Sample(60, 28),
            Sample(120, 5),
        ]);
        await _store.UpsertDiveAsync(dive);

        var vm = CreateViewModel();
        await vm.LoadCommand.ExecuteAsync(dive.Id);

        Assert.Equal("28 m", vm.DepthFloorText);
        Assert.Equal("0 m", vm.DepthSurfaceText);
        Assert.True(vm.HasDepthFloorLabel);
        Assert.Equal("30 m", vm.MaxDepthText);
    }

    [Fact]
    public async Task A_surface_only_dive_hides_the_floor_label()
    {
        var dive = Dive(maxDepthM: 0, samples: [Sample(0, 0), Sample(60, 0)]);
        await _store.UpsertDiveAsync(dive);

        var vm = CreateViewModel();
        await vm.LoadCommand.ExecuteAsync(dive.Id);

        Assert.False(vm.HasDepthFloorLabel);
    }

    [Fact]
    public async Task Average_depth_prefers_the_provider_value_over_the_sample_mean()
    {
        // Proves DiveStats.TryAverageDepth is the source rather than a re-implementation: the sample mean here
        // is nowhere near 19.7.
        var dive = Dive(avgDepthM: 19.7, samples: [Sample(0, 0), Sample(60, 40), Sample(120, 0)]);
        await _store.UpsertDiveAsync(dive);

        var vm = CreateViewModel();
        await vm.LoadCommand.ExecuteAsync(dive.Id);

        Assert.True(vm.HasAverageDepth);
        Assert.Equal("19.7 m", vm.AvgDepthText);
        Assert.Contains("average 19.7 m", vm.MaxDepthAccessibleText);
    }

    [Fact]
    public async Task Average_temperature_falls_back_to_the_sample_mean()
    {
        // No provider average reported, so the mean of the recorded temps is used: (20 + 22) / 2 = 21.
        var dive = Dive(minTempC: 20, samples: [Sample(0, 0, temp: 20), Sample(60, 20, temp: 22)]);
        await _store.UpsertDiveAsync(dive);

        var vm = CreateViewModel();
        await vm.LoadCommand.ExecuteAsync(dive.Id);

        Assert.True(vm.HasAverageTemp);
        Assert.Equal("21 °C", vm.AvgTempText);
        Assert.Contains("average 21 °C", vm.MinTempAccessibleText);
    }

    [Fact]
    public async Task Scrub_reports_depth_temperature_and_time_together()
    {
        var dive = Dive(samples:
        [
            Sample(0, 0, temp: 26),
            Sample(60, 20, temp: 27),
            Sample(120, 10, temp: 28),
        ]);
        await _store.UpsertDiveAsync(dive);

        var vm = CreateViewModel();
        await vm.LoadCommand.ExecuteAsync(dive.Id);

        vm.UpdateScrub(0.5);

        Assert.True(vm.IsScrubbing);
        Assert.NotEqual("", vm.ScrubDepthText);
        Assert.NotEqual("", vm.ScrubTimeText);
        Assert.True(vm.HasScrubTemp);
        Assert.Contains("°C", vm.ScrubTempText);
    }

    [Fact]
    public async Task Scrub_reports_no_temperature_when_the_dive_recorded_none()
    {
        var dive = Dive(samples: [Sample(0, 0), Sample(60, 20)]);
        await _store.UpsertDiveAsync(dive);

        var vm = CreateViewModel();
        await vm.LoadCommand.ExecuteAsync(dive.Id);

        vm.UpdateScrub(0.5);

        Assert.False(vm.HasScrubTemp);
        Assert.Equal("", vm.ScrubTempText);
    }

    [Fact]
    public async Task A_bare_dive_yields_only_the_profile_and_notes_panes()
    {
        // No fact groups, no tags, no coordinates, no events, no recorded readouts → no Details, no Events.
        var dive = Dive(samples: [Sample(0, 0), Sample(60, 20)]);
        await _store.UpsertDiveAsync(dive);

        var vm = CreateViewModel();
        await vm.LoadCommand.ExecuteAsync(dive.Id);

        Assert.Equal([DivePaneKind.Profile, DivePaneKind.Notes], vm.Panes);
        Assert.Equal(2, vm.PaneCount);
        Assert.Equal(0, vm.PaneIndex);
    }

    [Fact]
    public async Task A_rich_dive_yields_every_pane()
    {
        var dive = Dive(
            minTempC: 26,
            samples: [Sample(0, 0, ndl: 20), Sample(60, 20, ndl: 5)]) with
        {
            DiveTypeLabel = "SINGLE GAS",
            Tags = ["WRECK"],
            Alerts = [new DiveAlertDto(0, "Dive gas switched")],
            Environment = new DiveEnvironmentDto(EntryLatitude: -27.5, EntryLongitude: 32.6),
        };
        await _store.UpsertDiveAsync(dive);

        var vm = CreateViewModel();
        await vm.LoadCommand.ExecuteAsync(dive.Id);

        Assert.Equal(
            [DivePaneKind.Profile, DivePaneKind.Details, DivePaneKind.Events, DivePaneKind.Notes],
            vm.Panes);
        Assert.Equal(4, vm.PaneCount);
    }

    [Fact]
    public async Task Pane_index_is_clamped_when_the_pane_list_shrinks()
    {
        // The carousel must never be left bound to a position that no longer exists.
        var rich = Dive(samples: [Sample(0, 0), Sample(60, 20)]) with { Tags = ["WRECK"] };
        await _store.UpsertDiveAsync(rich);
        var vm = CreateViewModel();
        await vm.LoadCommand.ExecuteAsync(rich.Id);
        vm.PaneIndex = vm.PaneCount - 1;

        vm.Panes = [DivePaneKind.Profile];

        Assert.Equal(0, vm.PaneIndex);
    }

    [Fact]
    public async Task Entry_point_tap_hands_the_coordinate_to_the_platform_maps_app()
    {
        var dive = Dive(samples: [Sample(0, 0), Sample(60, 20)]) with
        {
            Environment = new DiveEnvironmentDto(EntryLatitude: -27.5225, EntryLongitude: 32.6866),
        };
        await _store.UpsertDiveAsync(dive);

        var vm = CreateViewModel();
        await vm.LoadCommand.ExecuteAsync(dive.Id);

        Assert.True(vm.HasEntryPoint);
        Assert.Equal("-27.5225, 32.6866", vm.EntryPointText);

        await vm.OpenEntryPointCommand.ExecuteAsync(null);

        var opened = Assert.Single(_maps.Opened);
        Assert.Equal(-27.5225, opened.Latitude, 4);
        Assert.Equal(32.6866, opened.Longitude, 4);
    }

    [Fact]
    public async Task Entry_point_tap_does_nothing_without_coordinates()
    {
        var dive = Dive(samples: [Sample(0, 0), Sample(60, 20)]);
        await _store.UpsertDiveAsync(dive);

        var vm = CreateViewModel();
        await vm.LoadCommand.ExecuteAsync(dive.Id);

        await vm.OpenEntryPointCommand.ExecuteAsync(null);

        Assert.False(vm.HasEntryPoint);
        Assert.Empty(_maps.Opened);
    }

    [Fact]
    public async Task Imperial_and_fahrenheit_reformat_every_chart_and_tile_label()
    {
        // One test guarding the unit toggles across all the labels added for the chart redesign.
        var preferences = new InMemoryPreferences();
        preferences.Set(PreferenceKeys.DepthUnit, DepthUnits.Feet);
        preferences.Set(PreferenceKeys.TempUnit, TempUnits.Fahrenheit);
        var units = new UnitFormatter(preferences);

        var dive = Dive(avgDepthM: 18.2, minTempC: 26, samples:
        [
            Sample(0, 0, temp: 26),
            Sample(60, 28, temp: 28),
        ]);
        await _store.UpsertDiveAsync(dive);

        var vm = new DiveDetailViewModel(
            _store, units, _navigator, new FakeClock(),
            new Trimix.Core.Tests.Connectors.Cloud.InMemoryConnectorConsentStore(), new FakeProviderPriority(), [], _maps);
        await vm.LoadCommand.ExecuteAsync(dive.Id);

        Assert.EndsWith("ft", vm.DepthSurfaceText);
        Assert.EndsWith("ft", vm.DepthFloorText);
        Assert.EndsWith("ft", vm.AvgDepthText);
        Assert.Contains("°F", vm.AvgTempText);
        Assert.Contains("°F", vm.TempRangeText);
    }
}

