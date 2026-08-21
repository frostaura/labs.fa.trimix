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

    /// <summary>Point budget for the DRAWN depth polyline. See <see cref="BuildRenderPath"/>.</summary>
    internal const int MaxRenderPoints = 400;

    /// <summary>
    /// Reduces a depth path to a drawing-sized polyline. A cloud-synced 1 Hz profile is ~5,600 points for a
    /// 100-minute dive, drawn into roughly 300 device-independent units — about 19 points per pixel, which
    /// renders the depth sensor's ±0.1 m quantisation as a furry line rather than a clean trace.
    ///
    /// <para>This is deliberately a SEPARATE path from <see cref="BuildDepthChart"/>, not a change to it: the
    /// full polyline is what <see cref="YAtX"/> interpolates for the scrub crosshair, and what the overlays
    /// and the max-depth label are derived from. Only the drawable consumes the reduced one.</para>
    ///
    /// <para><see cref="DepthChartPath.MaxDepthM"/> and <see cref="DepthChartPath.DurationSeconds"/> are copied
    /// verbatim, and the deepest point always survives, so the drawn line still touches the plot floor and
    /// still agrees with the labelled max depth.</para>
    /// </summary>
    public static DepthChartPath BuildRenderPath(DepthChartPath full, int maxPoints = MaxRenderPoints)
    {
        ArgumentNullException.ThrowIfNull(full);
        if (full.IsEmpty || full.Line.Count <= maxPoints)
        {
            return full;
        }

        var line = full.Line;
        var kept = KeepIndices(line.Count, maxPoints, i => line[i].Y);

        var reduced = new ChartPoint[kept.Length];
        for (var i = 0; i < kept.Length; i++)
        {
            reduced[i] = line[kept[i]];
        }

        // Close the fill along the surface, exactly as BuildDepthChart does.
        var area = new ChartPoint[reduced.Length + 2];
        area[0] = reduced[0] with { Y = 0 };
        reduced.CopyTo(area.AsSpan(1));
        area[^1] = reduced[^1] with { Y = 0 };

        return new DepthChartPath(reduced, area, full.MaxDepthM, full.DurationSeconds);
    }

    /// <summary>
    /// Linear interpolation of the normalized depth polyline at normalized elapsed
    /// <paramref name="x"/> ∈ [0,1] — the touch-to-scrub crosshair's Y (C6). Clamped to the
    /// chart's ends; 0 for an empty chart.
    /// </summary>
    public static double YAtX(DepthChartPath path, double x)
    {
        ArgumentNullException.ThrowIfNull(path);
        var line = path.Line;
        if (line.Count == 0)
        {
            return 0;
        }

        if (x <= line[0].X)
        {
            return line[0].Y;
        }

        for (var i = 1; i < line.Count; i++)
        {
            if (x <= line[i].X)
            {
                var (x0, y0) = (line[i - 1].X, line[i - 1].Y);
                var (x1, y1) = (line[i].X, line[i].Y);
                var t = x1 <= x0 ? 0 : (x - x0) / (x1 - x0);
                return y0 + (y1 - y0) * t;
            }
        }

        return line[^1].Y;
    }
}
