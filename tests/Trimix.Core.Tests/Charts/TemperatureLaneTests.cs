using Trimix.Contracts.Dtos;
using Trimix.Core.Charts;
using Xunit;

namespace Trimix.Core.Tests.Charts;

/// <summary>
/// The temperature mini-lane. These tests exist because the previous behaviour — normalising temperature over
/// its own observed min/max — rendered a real dive as a full-height binary staircase: dive computers report
/// whole °C, so a 26–28 °C dive had three distinct values and 1 Hz sensor flicker between adjacent degrees drew
/// top-to-bottom vertical strokes hundreds of times.
/// </summary>
public class TemperatureLaneTests
{
    private static ProfileSampleDto Sample(int t, double depth, double? temp) =>
        new(t, depth, temp, null);

    private static DepthChartPath Depth(IReadOnlyList<ProfileSampleDto> samples) =>
        ChartGeometry.BuildDepthChart(samples);

    private static TemperatureLane? Build(IReadOnlyList<ProfileSampleDto> samples) =>
        ChartGeometry.BuildTemperatureLane(samples, Depth(samples));

    [Fact]
    public void Two_readings_are_required()
    {
        // One reading is a value, not a series — there is nothing to draw between.
        var samples = new[] { Sample(0, 0, 20), Sample(60, 20, null), Sample(120, 5, null) };

        Assert.Null(Build(samples));
    }

    [Fact]
    public void No_readings_at_all_yields_no_lane()
    {
        var samples = new[] { Sample(0, 0, null), Sample(60, 20, null) };

        Assert.Null(Build(samples));
    }

    [Fact]
    public void A_narrow_real_world_span_is_floored_so_it_cannot_render_as_a_staircase()
    {
        // THE REGRESSION TEST. A real 26–28 °C dive: three whole-degree values. Autoscaling to the observed
        // 2 °C span would put 26 at Y=1.0 and 28 at Y=0.0 — the full lane height for 2 °C of change.
        var samples = new[] { Sample(0, 0, 26), Sample(60, 20, 27), Sample(120, 5, 28) };

        var lane = Build(samples)!;

        // Axis padded to the 4 °C minimum span and rounded to whole degrees: 25…29.
        Assert.Equal(25, lane.AxisMinC);
        Assert.Equal(29, lane.AxisMaxC);

        // 26 °C is now three quarters down a 25–29 axis, not pinned to the floor.
        Assert.Equal(0.75, lane.Points[0].Y, 6);
        Assert.Equal(0.25, lane.Points[^1].Y, 6);
    }

    [Fact]
    public void Observed_values_are_reported_for_labels_not_the_padded_axis()
    {
        // The eyebrow must show the temperature the diver actually saw, never the padded axis bounds.
        var samples = new[] { Sample(0, 0, 26), Sample(60, 20, 27), Sample(120, 5, 28) };

        var lane = Build(samples)!;

        Assert.Equal(26, lane.ObservedMinC);
        Assert.Equal(28, lane.ObservedMaxC);
        Assert.NotEqual(lane.ObservedMinC, lane.AxisMinC);
    }

    [Fact]
    public void A_wide_span_is_not_padded_beyond_whole_degree_rounding()
    {
        // A 20 °C span is far above the floor, so the axis is exactly the observed range.
        var samples = new[] { Sample(0, 0, 10), Sample(60, 20, 30), Sample(120, 5, 20) };

        var lane = Build(samples)!;

        Assert.Equal(10, lane.AxisMinC);
        Assert.Equal(30, lane.AxisMaxC);

        // X is shared with the depth chart (t / duration).
        Assert.Equal(0.0, lane.Points[0].X, 6);
        Assert.Equal(0.5, lane.Points[1].X, 6);
        Assert.Equal(1.0, lane.Points[2].X, 6);

        // Value-up over the axis: coldest at the bottom, warmest at the top, mid in the middle.
        Assert.Equal(1.0, lane.Points[0].Y, 6);
        Assert.Equal(0.0, lane.Points[1].Y, 6);
        Assert.Equal(0.5, lane.Points[2].Y, 6);
    }

    [Fact]
    public void A_flat_temperature_sits_on_the_lane_midline()
    {
        var samples = new[] { Sample(0, 5, 20), Sample(60, 20, 20), Sample(120, 5, 20) };

        var lane = Build(samples)!;

        // Span floored to 4 and centred on 20 → 18…22, so a flat 20 is exactly mid-lane.
        Assert.Equal(18, lane.AxisMinC);
        Assert.Equal(22, lane.AxisMaxC);
        Assert.All(lane.Points, p => Assert.Equal(0.5, p.Y, 6));
    }

    [Fact]
    public void A_one_hz_profile_is_smoothed_and_decimated_so_no_point_reaches_the_lane_edges()
    {
        // 5,600 samples flickering between two adjacent whole degrees — exactly what a real dive computer
        // records. Smoothing before decimating must turn this into a curve near mid-lane, never a staircase
        // slamming between the top and bottom edges.
        var samples = Enumerable.Range(0, 5600)
            .Select(i => Sample(i, 12, i % 2 == 0 ? 26 : 27))
            .ToArray();

        var lane = Build(samples)!;

        Assert.InRange(lane.Points.Count, 2, 120);
        Assert.Equal(0.0, lane.Points[0].X, 6);
        Assert.Equal(1.0, lane.Points[^1].X, 6);
        Assert.All(lane.Points, p => Assert.InRange(p.Y, 0.05, 0.95));
    }

    [Fact]
    public void Temp_at_x_interpolates_back_to_degrees_for_the_scrub_readout()
    {
        var samples = new[] { Sample(0, 0, 10), Sample(60, 20, 30), Sample(120, 5, 20) };
        var lane = Build(samples)!;

        Assert.Equal(10, ChartGeometry.TempAtX(lane, 0.0)!.Value, 4);
        Assert.Equal(30, ChartGeometry.TempAtX(lane, 0.5)!.Value, 4);
        Assert.Equal(20, ChartGeometry.TempAtX(lane, 1.0)!.Value, 4);

        // Clamped past both ends rather than extrapolating off the axis.
        Assert.Equal(10, ChartGeometry.TempAtX(lane, -1.0)!.Value, 4);
        Assert.Equal(20, ChartGeometry.TempAtX(lane, 2.0)!.Value, 4);
    }

    [Fact]
    public void Temp_at_x_is_null_without_a_lane()
    {
        Assert.Null(ChartGeometry.TempAtX(null, 0.5));
    }
}
