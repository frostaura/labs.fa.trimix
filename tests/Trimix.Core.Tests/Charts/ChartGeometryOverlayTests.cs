using Trimix.Contracts.Dtos;
using Trimix.Core.Charts;
using Xunit;

namespace Trimix.Core.Tests.Charts;

/// <summary>
/// Additive overlay geometry: secondary channel series, the recorded-ceiling band, and recorded
/// event markers. All null-tolerant — a channel is surfaced only when samples carry it, so the
/// depth-temp-only (HealthKit) case yields at most a temperature series.
/// </summary>
public class ChartGeometryOverlayTests
{
    // Rich sample with every channel; pass null to omit any one.
    private static ProfileSampleDto Rich(
        int t,
        double depth,
        double? temp = null,
        double? ppO2 = null,
        double? ndl = null,
        double? ceiling = null,
        double? ascent = null,
        double? cns = null,
        double? tank = null,
        double? setpoint = null,
        double? hr = null) =>
        new(t, depth, temp, ppO2, ndl, ceiling, ascent, cns, tank, setpoint, hr);

    private static DepthChartPath Depth(params ProfileSampleDto[] samples) =>
        ChartGeometry.BuildDepthChart(samples);

    [Fact]
    public void Empty_samples_yield_the_empty_overlay_bundle()
    {
        var overlays = ChartGeometry.BuildProfileOverlays([], DepthChartPath.Empty);

        Assert.Same(ProfileOverlays.Empty, overlays);
        Assert.False(overlays.HasAny);
        Assert.Empty(overlays.Markers);
    }

    [Fact]
    public void Depth_only_samples_produce_nothing_and_HasAny_is_false()
    {
        var samples = new[] { Rich(0, 0), Rich(60, 20), Rich(120, 8) };
        var overlays = ChartGeometry.BuildProfileOverlays(samples, Depth(samples));

        Assert.Null(overlays.Temperature);
        Assert.Null(overlays.TankPressure);
        Assert.Null(overlays.Ndl);
        Assert.Null(overlays.CeilingBand);
        Assert.Empty(overlays.Markers);
        Assert.False(overlays.HasAny);
    }

    [Fact]
    public void Depth_temp_only_healthkit_case_yields_only_a_temperature_series()
    {
        var samples = new[] { Rich(0, 0, temp: 20), Rich(60, 20, temp: 14), Rich(120, 5, temp: 16) };
        var overlays = ChartGeometry.BuildProfileOverlays(samples, Depth(samples));

        Assert.NotNull(overlays.Temperature);
        Assert.Equal("temp", overlays.Temperature!.ChannelKey);
        Assert.Equal(ChartAxisSide.Right, overlays.Temperature.Axis);
        Assert.Null(overlays.TankPressure);
        Assert.Null(overlays.Ndl);
        Assert.Null(overlays.CeilingBand);
        Assert.Empty(overlays.Markers);
        Assert.True(overlays.HasAny);
    }

    [Fact]
    public void A_channel_with_fewer_than_two_readings_is_not_emitted()
    {
        // Only one sample carries temperature → no series (needs >= 2).
        var samples = new[] { Rich(0, 0, temp: 20), Rich(60, 20), Rich(120, 5) };
        var overlays = ChartGeometry.BuildProfileOverlays(samples, Depth(samples));

        Assert.Null(overlays.Temperature);
    }

    [Fact]
    public void Temperature_series_normalizes_value_up_and_shares_the_depth_x_axis()
    {
        var samples = new[] { Rich(0, 0, temp: 10), Rich(60, 20, temp: 30), Rich(120, 5, temp: 20) };
        var overlays = ChartGeometry.BuildProfileOverlays(samples, Depth(samples));

        var series = Assert.IsType<ChartSeries>(overlays.Temperature);
        Assert.Equal(10, series.MinValue);
        Assert.Equal(30, series.MaxValue);

        // X shared with the depth chart (t / duration).
        Assert.Equal(0.0, series.Points[0].X, 6);
        Assert.Equal(0.5, series.Points[1].X, 6);
        Assert.Equal(1.0, series.Points[2].X, 6);

        // Value-up: max (30) → Y 0 (top); min (10) → Y 1 (bottom); mid (20) → 0.5.
        Assert.Equal(1.0, series.Points[0].Y, 6); // temp 10 = coldest = bottom
        Assert.Equal(0.0, series.Points[1].Y, 6); // temp 30 = warmest = top
        Assert.Equal(0.5, series.Points[2].Y, 6); // temp 20 = mid
    }

    [Fact]
    public void A_flat_channel_collapses_to_the_axis_midline()
    {
        var samples = new[] { Rich(0, 5, tank: 200), Rich(60, 20, tank: 200), Rich(120, 5, tank: 200) };
        var overlays = ChartGeometry.BuildProfileOverlays(samples, Depth(samples));

        var series = Assert.IsType<ChartSeries>(overlays.TankPressure);
        Assert.Equal(200, series.MinValue);
        Assert.Equal(200, series.MaxValue);
        Assert.All(series.Points, p => Assert.Equal(0.5, p.Y, 6));
    }

    [Fact]
    public void Tank_pressure_and_ndl_emit_their_own_right_axis_series()
    {
        var samples = new[]
        {
            Rich(0, 0, tank: 210, ndl: 99),
            Rich(60, 30, tank: 150, ndl: 12),
            Rich(120, 5, tank: 90, ndl: 40),
        };
        var overlays = ChartGeometry.BuildProfileOverlays(samples, Depth(samples));

        Assert.NotNull(overlays.TankPressure);
        Assert.Equal("tank_pressure", overlays.TankPressure!.ChannelKey);
        Assert.Equal(90, overlays.TankPressure.MinValue);
        Assert.Equal(210, overlays.TankPressure.MaxValue);

        Assert.NotNull(overlays.Ndl);
        Assert.Equal("ndl", overlays.Ndl!.ChannelKey);
        Assert.Equal(ChartAxisSide.Right, overlays.Ndl.Axis);
    }

    [Fact]
    public void Ceiling_band_spans_only_ceiling_samples_and_normalizes_by_the_main_max_depth()
    {
        // maxDepth = 40; a 6m ceiling recorded at two samples → Lower Y = 6/40 = 0.15.
        var samples = new[]
        {
            Rich(0, 0),
            Rich(60, 40),
            Rich(120, 12, ceiling: 6),
            Rich(180, 6, ceiling: 6),
        };
        var overlays = ChartGeometry.BuildProfileOverlays(samples, Depth(samples));

        var band = Assert.IsType<ChartBand>(overlays.CeilingBand);
        Assert.Equal("ceiling", band.ChannelKey);
        Assert.Equal(2, band.Upper.Count);
        Assert.Equal(2, band.Lower.Count);
        Assert.All(band.Upper, p => Assert.Equal(0.0, p.Y, 6)); // upper edge = surface
        Assert.Equal(6.0 / 40.0, band.Lower[0].Y, 6);
        Assert.Equal(6.0 / 40.0, band.Lower[1].Y, 6);
        // Band X positions match the ceiling samples' shared-axis X (120/180, 180/180).
        Assert.Equal(120.0 / 180.0, band.Lower[0].X, 6);
        Assert.Equal(1.0, band.Lower[1].X, 6);
    }

    [Fact]
    public void No_recorded_ceiling_means_no_band()
    {
        var samples = new[] { Rich(0, 0, temp: 20), Rich(60, 20, temp: 14) };
        var overlays = ChartGeometry.BuildProfileOverlays(samples, Depth(samples));

        Assert.Null(overlays.CeilingBand);
    }

    [Fact]
    public void Setpoint_transitions_emit_setpoint_markers_at_the_changed_sample()
    {
        var samples = new[]
        {
            Rich(0, 0, setpoint: 0.7),
            Rich(60, 30, setpoint: 0.7),
            Rich(120, 30, setpoint: 1.3), // transition here
            Rich(180, 6, setpoint: 1.3),
        };
        var overlays = ChartGeometry.BuildProfileOverlays(samples, Depth(samples));

        var marker = Assert.Single(overlays.Markers);
        Assert.Equal(ChartMarkerKind.Setpoint, marker.Kind);
        Assert.Equal(120.0 / 180.0, marker.X, 6);
        Assert.Contains("1.3", marker.Label);
    }

    [Fact]
    public void Deco_onset_marks_the_first_sample_with_recorded_deco_only_once()
    {
        // NDL hits 0 at t=120; a ceiling appears later too — still exactly one DecoOnset (the first).
        var samples = new[]
        {
            Rich(0, 0, ndl: 99),
            Rich(60, 30, ndl: 5),
            Rich(120, 32, ndl: 0),
            Rich(180, 20, ndl: 0, ceiling: 3),
        };
        var overlays = ChartGeometry.BuildProfileOverlays(samples, Depth(samples));

        var deco = Assert.Single(overlays.Markers, m => m.Kind == ChartMarkerKind.DecoOnset);
        Assert.Equal(120.0 / 180.0, deco.X, 6);
        Assert.Equal("Deco", deco.Label);
    }

    [Fact]
    public void Ceiling_alone_triggers_deco_onset_even_without_ndl()
    {
        var samples = new[] { Rich(0, 0), Rich(60, 30, ceiling: 4), Rich(120, 10, ceiling: 2) };
        var overlays = ChartGeometry.BuildProfileOverlays(samples, Depth(samples));

        var deco = Assert.Single(overlays.Markers, m => m.Kind == ChartMarkerKind.DecoOnset);
        Assert.Equal(60.0 / 120.0, deco.X, 6);
    }

    [Fact]
    public void Ascent_warning_fires_once_per_fast_run_at_its_rising_edge()
    {
        // Two separate fast-ascent runs (>10 m/min), each contiguous → one marker per run.
        var samples = new[]
        {
            Rich(0, 30, ascent: 2),
            Rich(30, 25, ascent: 12), // run 1 starts
            Rich(60, 18, ascent: 14), // still fast → no extra marker
            Rich(90, 15, ascent: 3),  // back to safe
            Rich(120, 8, ascent: 11), // run 2 starts
        };
        var overlays = ChartGeometry.BuildProfileOverlays(samples, Depth(samples));

        var warnings = overlays.Markers.Where(m => m.Kind == ChartMarkerKind.AscentWarning).ToArray();
        Assert.Equal(2, warnings.Length);
        Assert.Equal(30.0 / 120.0, warnings[0].X, 6);
        Assert.Equal(120.0 / 120.0, warnings[1].X, 6);
    }

    [Fact]
    public void Ascent_exactly_at_threshold_does_not_warn()
    {
        var samples = new[] { Rich(0, 30, ascent: 10), Rich(60, 20, ascent: 10) };
        var overlays = ChartGeometry.BuildProfileOverlays(samples, Depth(samples));

        Assert.DoesNotContain(overlays.Markers, m => m.Kind == ChartMarkerKind.AscentWarning);
    }

    [Fact]
    public void Markers_are_ordered_by_x()
    {
        var samples = new[]
        {
            Rich(0, 0, setpoint: 0.7, ndl: 50),
            Rich(60, 30, setpoint: 0.7, ndl: 0),       // deco onset (earlier X than the setpoint switch)
            Rich(120, 30, setpoint: 1.3, ndl: 0),      // setpoint transition (later X)
        };
        var overlays = ChartGeometry.BuildProfileOverlays(samples, Depth(samples));

        Assert.True(overlays.Markers.Count >= 2);
        var xs = overlays.Markers.Select(m => m.X).ToArray();
        Assert.True(xs.SequenceEqual(xs.OrderBy(x => x)), "markers must be sorted by X");
    }

    [Fact]
    public void Marker_depth_y_matches_the_main_depth_normalization()
    {
        // maxDepth = 40; deco onset recorded at 20m → Y = 20/40 = 0.5.
        var samples = new[] { Rich(0, 0, ndl: 5), Rich(60, 40, ndl: 3), Rich(120, 20, ndl: 0) };
        var overlays = ChartGeometry.BuildProfileOverlays(samples, Depth(samples));

        var deco = Assert.Single(overlays.Markers, m => m.Kind == ChartMarkerKind.DecoOnset);
        Assert.Equal(0.5, deco.Y, 6);
    }
}
