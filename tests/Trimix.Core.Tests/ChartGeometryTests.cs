using Trimix.Contracts.Dtos;
using Trimix.Core.Charts;
using Xunit;

namespace Trimix.Core.Tests;

/// <summary>Inverted-Y depth chart geometry: normalization, surface-closed area, degenerate inputs.</summary>
public class ChartGeometryTests
{
    private static ProfileSampleDto Sample(int t, double depth, double? temp = null) => new(t, depth, temp, null);

    [Fact]
    public void Empty_samples_produce_the_empty_path()
    {
        var path = ChartGeometry.BuildDepthChart([]);
        Assert.True(path.IsEmpty);
        Assert.Empty(path.Line);
        Assert.Empty(path.Area);
    }

    [Fact]
    public void Normalizes_time_to_x_and_depth_to_inverted_y()
    {
        var path = ChartGeometry.BuildDepthChart([Sample(0, 0), Sample(60, 20), Sample(120, 10)]);

        Assert.Equal(20, path.MaxDepthM);
        Assert.Equal(120, path.DurationSeconds);
        Assert.Equal(new ChartPoint(0, 0), path.Line[0]);
        Assert.Equal(new ChartPoint(0.5, 1.0), path.Line[1]); // max depth → Y = 1 (bottom of chart)
        Assert.Equal(new ChartPoint(1.0, 0.5), path.Line[2]);
    }

    [Fact]
    public void Deeper_means_larger_y_screen_convention()
    {
        var path = ChartGeometry.BuildDepthChart([Sample(0, 5), Sample(10, 30), Sample(20, 12)]);
        Assert.True(path.Line[1].Y > path.Line[0].Y); // descending → Y grows downward
        Assert.True(path.Line[1].Y > path.Line[2].Y); // ascending → Y shrinks
    }

    [Fact]
    public void Area_closes_along_the_surface()
    {
        var path = ChartGeometry.BuildDepthChart([Sample(0, 10), Sample(30, 20), Sample(60, 5)]);

        Assert.Equal(path.Line.Count + 2, path.Area.Count);
        Assert.Equal(0, path.Area[0].Y);        // opens at the surface
        Assert.Equal(0, path.Area[^1].Y);       // closes at the surface
        Assert.Equal(path.Line[0].X, path.Area[0].X);
        Assert.Equal(path.Line[^1].X, path.Area[^1].X);
    }

    [Fact]
    public void Single_sample_collapses_to_origin_x()
    {
        var path = ChartGeometry.BuildDepthChart([Sample(0, 18)]);
        var point = Assert.Single(path.Line);
        Assert.Equal(0, point.X);
        Assert.Equal(1, point.Y);
    }

    [Fact]
    public void All_surface_samples_stay_at_y_zero()
    {
        var path = ChartGeometry.BuildDepthChart([Sample(0, 0), Sample(10, 0)]);
        Assert.All(path.Line, p => Assert.Equal(0, p.Y));
        Assert.Equal(0, path.MaxDepthM);
    }

    [Fact]
    public void Unordered_samples_are_sorted_by_time()
    {
        var path = ChartGeometry.BuildDepthChart([Sample(60, 20), Sample(0, 0), Sample(30, 10)]);
        Assert.Equal(0, path.Line[0].X);
        Assert.Equal(0.5, path.Line[1].X);
        Assert.Equal(1.0, path.Line[2].X);
    }

    [Fact]
    public void Negative_depths_clamp_to_surface()
    {
        var path = ChartGeometry.BuildDepthChart([Sample(0, -2), Sample(10, 10)]);
        Assert.Equal(0, path.Line[0].Y);
    }

    [Fact]
    public void Fixture_profile_produces_full_range_geometry()
    {
        // End-to-end sanity with a realistic profile shape from the Shearwater fixtures.
        var dive = Core.DiveComputer.ShearwaterFixtures.Dives[1];
        var samples = Enumerable.Range(0, dive.SampleCount)
            .Select(i => Sample(
                i * dive.SampleIntervalSec,
                Core.DiveComputer.ShearwaterFixtures.DepthAt(dive, i)))
            .ToList();

        var path = ChartGeometry.BuildDepthChart(samples);

        Assert.Equal(dive.MaxDepthM, path.MaxDepthM, 3);
        Assert.Equal(1.0, path.Line.Max(p => p.Y), 3);  // bottom phase reaches the chart floor
        Assert.Equal(0.0, path.Line.Min(p => p.Y), 3);  // starts at the surface
        Assert.True(path.Line.SequenceEqual(path.Line.OrderBy(p => p.X))); // monotone X
    }
}
