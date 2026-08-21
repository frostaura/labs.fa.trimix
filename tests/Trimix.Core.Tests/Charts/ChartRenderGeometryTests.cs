using Trimix.Contracts.Dtos;
using Trimix.Core.Charts;
using Xunit;

namespace Trimix.Core.Tests.Charts;

/// <summary>
/// The drawing-only decimated depth path, and the average-depth reference line.
///
/// <para><c>BuildRenderPath</c> exists as a SEPARATE path from <c>BuildDepthChart</c> precisely so the full
/// polyline stays intact for scrub interpolation and the max-depth label. The tests below assert that
/// separation holds — if a future change decimates inside <c>BuildDepthChart</c> instead, the nine facts in
/// <c>ChartGeometryTests</c> will fail, and these will stop being meaningful.</para>
/// </summary>
public class ChartRenderGeometryTests
{
    private static ProfileSampleDto Sample(int t, double depth) => new(t, depth, null, null);

    /// <summary>A dive that descends to <paramref name="maxDepth"/> at its midpoint and returns.</summary>
    private static DepthChartPath Profile(int count, double maxDepth = 30)
    {
        var samples = Enumerable.Range(0, count)
            .Select(i =>
            {
                var half = (count - 1) / 2.0;
                var depth = i <= half ? maxDepth * (i / half) : maxDepth * (2 - (i / half));
                return Sample(i, Math.Max(0, depth));
            })
            .ToArray();

        return ChartGeometry.BuildDepthChart(samples);
    }

    [Fact]
    public void A_short_profile_renders_point_for_point()
    {
        var full = Profile(50);

        var render = ChartGeometry.BuildRenderPath(full);

        // Under budget: the very same instance, so no allocation and no chance of drift.
        Assert.Same(full, render);
    }

    [Fact]
    public void An_empty_chart_passes_through()
    {
        Assert.Same(DepthChartPath.Empty, ChartGeometry.BuildRenderPath(DepthChartPath.Empty));
    }

    [Fact]
    public void A_one_hz_profile_is_capped_at_the_render_budget()
    {
        // A 100-minute 1 Hz dive: ~19 points per pixel before decimation.
        var full = Profile(5581);

        var render = ChartGeometry.BuildRenderPath(full);

        Assert.Equal(5581, full.Line.Count);
        Assert.InRange(render.Line.Count, 2, 400);
    }

    [Fact]
    public void Render_path_preserves_max_depth_duration_and_the_deepest_point()
    {
        var full = Profile(5581, maxDepth: 28.4);

        var render = ChartGeometry.BuildRenderPath(full);

        // The labelled max depth is derived from these, so they must survive verbatim.
        Assert.Equal(full.MaxDepthM, render.MaxDepthM);
        Assert.Equal(full.DurationSeconds, render.DurationSeconds);

        // The deepest sample is always retained, so the drawn line still touches the plot floor and still
        // agrees with the depth-axis floor label.
        Assert.Equal(1.0, render.Line.Max(p => p.Y), 6);
        Assert.Equal(0.0, render.Line[0].X, 6);
        Assert.Equal(1.0, render.Line[^1].X, 6);
    }

    [Fact]
    public void Render_path_stays_time_ordered()
    {
        var render = ChartGeometry.BuildRenderPath(Profile(5581));

        var xs = render.Line.Select(p => p.X).ToList();
        Assert.Equal(xs.OrderBy(x => x), xs);
    }

    [Fact]
    public void Render_path_keeps_the_area_closed_at_the_surface()
    {
        var render = ChartGeometry.BuildRenderPath(Profile(5581));

        Assert.Equal(render.Line.Count + 2, render.Area.Count);
        Assert.Equal(0.0, render.Area[0].Y, 6);
        Assert.Equal(0.0, render.Area[^1].Y, 6);
        Assert.Equal(render.Line[0].X, render.Area[0].X, 6);
        Assert.Equal(render.Line[^1].X, render.Area[^1].X, 6);
    }

    [Fact]
    public void Scrub_reads_the_full_path_so_decimation_cannot_degrade_it()
    {
        // Documents the contract: the view model interpolates Chart (full), never ChartRender. The two agree
        // closely on a smooth profile, but the full path is the one that must be used.
        var full = Profile(5581);
        var render = ChartGeometry.BuildRenderPath(full);

        for (var i = 0; i <= 20; i++)
        {
            var x = i / 20.0;
            Assert.Equal(ChartGeometry.YAtX(full, x), ChartGeometry.YAtX(render, x), 2);
        }
    }

    [Fact]
    public void Average_depth_reference_normalizes_against_the_chart_max_depth()
    {
        var chart = Profile(101, maxDepth: 40);

        var reference = ChartGeometry.BuildAverageDepthReference(chart, 20)!;

        Assert.Equal(0.5, reference.Y, 6);
        Assert.Equal(20, reference.DepthM);
    }

    [Fact]
    public void Average_depth_reference_clamps_an_average_deeper_than_the_deepest_sample()
    {
        // A merged cross-source dive can carry a summary average deeper than the samples that survived
        // import; the line must sit on the floor rather than below the plot.
        var chart = Profile(101, maxDepth: 30);

        var reference = ChartGeometry.BuildAverageDepthReference(chart, 45)!;

        Assert.Equal(1.0, reference.Y, 6);
        Assert.Equal(45, reference.DepthM);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    public void Average_depth_reference_is_null_without_a_usable_average(double? average)
    {
        Assert.Null(ChartGeometry.BuildAverageDepthReference(Profile(101), average));
    }

    [Fact]
    public void Average_depth_reference_is_null_for_an_empty_chart()
    {
        Assert.Null(ChartGeometry.BuildAverageDepthReference(DepthChartPath.Empty, 12));
    }
}
