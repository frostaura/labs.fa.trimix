using Trimix.Contracts.Dtos;

namespace Trimix.Core.Charts;

/// <summary>A point in normalized chart space: X, Y ∈ [0, 1].</summary>
public readonly record struct ChartPoint(double X, double Y);

/// <summary>
/// Normalized geometry for the inverted-Y depth chart. <see cref="Line"/> is the depth polyline;
/// <see cref="Area"/> is the same polyline closed along the surface (Y = 0) for the
/// Sonar→transparent gradient fill. The App scales these to pixels in its IDrawable.
/// </summary>
public sealed record DepthChartPath(
    IReadOnlyList<ChartPoint> Line,
    IReadOnlyList<ChartPoint> Area,
    double MaxDepthM,
    int DurationSeconds)
{
    public static readonly DepthChartPath Empty = new([], [], 0, 0);

    public bool IsEmpty => Line.Count == 0;
}

/// <summary>
/// Pure helper converting dive profile samples into normalized polyline/area points for the
/// depth-over-time chart (design language: "downward area chart … inverted Y (0 at top)").
/// Convention: X = elapsed/total ∈ [0,1]; Y = depth/maxDepth ∈ [0,1] with 0 = surface at the TOP
/// and 1 = max depth at the bottom — Y already grows downward like screen coordinates, so the
/// App's drawable applies no flip. The App draws it later (GraphicsView/IDrawable, §11).
/// </summary>
public static partial class ChartGeometry
{
    public static DepthChartPath BuildDepthChart(IReadOnlyList<ProfileSampleDto> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Count == 0)
        {
            return DepthChartPath.Empty;
        }

        var ordered = samples.OrderBy(s => s.TSeconds).ToArray();
        var duration = ordered[^1].TSeconds;
        var maxDepth = ordered.Max(s => Math.Max(0, s.DepthM));

        var line = new ChartPoint[ordered.Length];
        for (var i = 0; i < ordered.Length; i++)
        {
            var x = duration <= 0 ? 0 : (double)ordered[i].TSeconds / duration;
            var y = maxDepth <= 0 ? 0 : Math.Max(0, ordered[i].DepthM) / maxDepth;
            line[i] = new ChartPoint(x, y);
        }

        // Close the fill along the surface: first point up, polyline, last point up.
        var area = new ChartPoint[line.Length + 2];
        area[0] = line[0] with { Y = 0 };
        line.CopyTo(area.AsSpan(1));
        area[^1] = line[^1] with { Y = 0 };

        return new DepthChartPath(line, area, maxDepth, duration);
    }
}
