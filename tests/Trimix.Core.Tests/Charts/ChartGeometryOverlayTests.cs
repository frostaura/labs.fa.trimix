using Trimix.Contracts.Dtos;
using Trimix.Core.Charts;
using Xunit;

namespace Trimix.Core.Tests.Charts;

/// <summary>
/// Additive overlay geometry: the recorded-ceiling bands and recorded event markers. Both are empty unless the
/// samples carry the channel.
///
/// <para>Temperature moved to <see cref="TemperatureLane"/> and is covered by <c>TemperatureLaneTests</c>; the
/// tank-pressure and NDL series were removed (no connector populates tank pressure, and NDL was never drawn —
/// both remain as text in the profile table and the recorded readouts).</para>
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

        Assert.Empty(overlays.CeilingBands);
        Assert.Empty(overlays.Markers);
        Assert.False(overlays.HasAny);
        // The depth-only case must also yield no temperature lane.
        Assert.Null(ChartGeometry.BuildTemperatureLane(samples, Depth(samples)));
    }

    [Fact]
    public void Depth_temp_only_healthkit_case_yields_no_main_canvas_overlays()
    {
        // Temperature is drawn on its own canvas now, so a depth+temp source adds NOTHING to the main chart.
        var samples = new[] { Rich(0, 0, temp: 20), Rich(60, 20, temp: 14), Rich(120, 5, temp: 16) };
        var overlays = ChartGeometry.BuildProfileOverlays(samples, Depth(samples));

        Assert.Empty(overlays.CeilingBands);
        Assert.Empty(overlays.Markers);
        Assert.False(overlays.HasAny);
        Assert.NotNull(ChartGeometry.BuildTemperatureLane(samples, Depth(samples)));
    }

    [Fact]
    public void Ceiling_bands_span_only_contiguous_ceiling_runs_and_normalize_by_the_main_max_depth()
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

        // The two ceiling samples are CONSECUTIVE, so they form exactly one run — every number below is
        // unchanged from when this returned a single band.
        var band = Assert.Single(overlays.CeilingBands);
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

        Assert.Empty(overlays.CeilingBands);
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

    // ── de-noising regressions ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Two_separate_deco_windows_produce_two_bands()
    {
        // THE REGRESSION TEST for the bridged amber slab. Filtering to "samples with a ceiling" and closing one
        // polygon over the survivors asserted a do-not-ascend zone across the gap between these two windows.
        var samples = new[]
        {
            Rich(0, 0),
            Rich(60, 30, ceiling: 6),
            Rich(120, 24, ceiling: 6),
            Rich(180, 10),               // ← ceiling cleared: the gap
            Rich(240, 18, ceiling: 3),
            Rich(300, 12, ceiling: 3),
        };

        var overlays = ChartGeometry.BuildProfileOverlays(samples, Depth(samples));

        Assert.Equal(2, overlays.CeilingBands.Count);
        var first = overlays.CeilingBands[0];
        var second = overlays.CeilingBands[1];
        // The bands must not touch — the gap has to survive into the geometry.
        Assert.True(first.Lower[^1].X < second.Lower[0].X);
        Assert.Equal(6.0 / 30.0, first.Lower[0].Y, 6);
        Assert.Equal(3.0 / 30.0, second.Lower[0].Y, 6);
    }

    [Fact]
    public void A_single_sample_deco_blip_produces_no_band_but_still_marks_deco_onset()
    {
        // A lone ceiling sample is a zero-width sliver. Dropping it is safe because the DecoOnset marker
        // already records the moment the computer flagged deco.
        var samples = new[] { Rich(0, 0), Rich(60, 30, ceiling: 6), Rich(120, 10) };

        var overlays = ChartGeometry.BuildProfileOverlays(samples, Depth(samples));

        Assert.Empty(overlays.CeilingBands);
        Assert.Contains(overlays.Markers, m => m.Kind == ChartMarkerKind.DecoOnset);
    }

    [Fact]
    public void Ascent_rate_oscillating_around_the_threshold_warns_once()
    {
        // THE REGRESSION TEST for marker spam. Without hysteresis this emitted a chevron on every crossing:
        // a 1 Hz ascent-rate channel hovering at the limit buried the chart in glyphs.
        var samples = new[]
        {
            Rich(0, 30, ascent: 12),
            Rich(10, 28, ascent: 9),
            Rich(20, 26, ascent: 13),
            Rich(30, 24, ascent: 9),
            Rich(40, 22, ascent: 12),
        };

        var overlays = ChartGeometry.BuildProfileOverlays(samples, Depth(samples));

        Assert.Single(overlays.Markers, m => m.Kind == ChartMarkerKind.AscentWarning);
    }

    [Fact]
    public void A_run_that_clears_the_hysteresis_floor_can_warn_again()
    {
        // Dropping below the clear threshold genuinely ends the run, so a later fast ascent is a new warning.
        var samples = new[]
        {
            Rich(0, 40, ascent: 12),
            Rich(60, 30, ascent: 2),     // well below the clear floor
            Rich(120, 20, ascent: 14),
        };

        var overlays = ChartGeometry.BuildProfileOverlays(samples, Depth(samples));

        Assert.Equal(2, overlays.Markers.Count(m => m.Kind == ChartMarkerKind.AscentWarning));
    }

    [Fact]
    public void Ascent_warnings_are_capped_per_kind()
    {
        // 20 well-separated fast-ascent runs; only the first six are drawn.
        var samples = new List<ProfileSampleDto>();
        for (var run = 0; run < 20; run++)
        {
            samples.Add(Rich((run * 100) + 0, 30, ascent: 14));  // rising edge
            samples.Add(Rich((run * 100) + 50, 28, ascent: 1));  // clears the run
        }

        var overlays = ChartGeometry.BuildProfileOverlays(samples, Depth([.. samples]));

        var warnings = overlays.Markers.Where(m => m.Kind == ChartMarkerKind.AscentWarning).ToList();
        Assert.Equal(6, warnings.Count);
        Assert.Equal(warnings.Select(m => m.X).Distinct().Count(), warnings.Count);
    }

    [Fact]
    public void Markers_closer_than_the_separation_floor_are_thinned()
    {
        // Two distinct runs only ~1 % of the timeline apart would overlap as glyphs, so the second is dropped.
        var samples = new List<ProfileSampleDto> { Rich(0, 5, ascent: 14), Rich(2, 5, ascent: 1) };
        samples.Add(Rich(4, 5, ascent: 14));
        samples.Add(Rich(400, 5, ascent: 1));

        var overlays = ChartGeometry.BuildProfileOverlays(samples, Depth([.. samples]));

        Assert.Single(overlays.Markers, m => m.Kind == ChartMarkerKind.AscentWarning);
    }

    [Fact]
    public void Deco_onset_is_never_thinned_away_by_its_neighbours()
    {
        // DecoOnset is the most safety-relevant glyph, so it is exempt from separation and capping even when it
        // lands on top of another marker.
        var samples = new[]
        {
            Rich(0, 30, ascent: 14, ceiling: 6),
            Rich(10, 28, ceiling: 6),
        };

        var overlays = ChartGeometry.BuildProfileOverlays(samples, Depth(samples));

        Assert.Contains(overlays.Markers, m => m.Kind == ChartMarkerKind.DecoOnset);
        Assert.Contains(overlays.Markers, m => m.Kind == ChartMarkerKind.AscentWarning);
    }
}

