using System.Globalization;
using Trimix.Contracts.Dtos;

namespace Trimix.Core.Charts;

/// <summary>
/// Additive overlay geometry for the depth chart (aggregator §8.4). All builders are null-tolerant:
/// a secondary channel is surfaced only when the source actually recorded it. Everything here is
/// import-and-display or arithmetic over stored values — no tissue-loading / deco is ever computed;
/// recorded NDL / ceiling / setpoint / ascent-rate are surfaced verbatim. <c>BuildDepthChart</c> and
/// <see cref="DepthChartPath"/> are unchanged and remain the primary depth line/area.
/// </summary>
public static partial class ChartGeometry
{
    private const double SetpointEpsilon = 0.01;

    /// <summary>
    /// Builds the secondary overlays for a dive's samples, sharing <paramref name="depth"/>'s X axis
    /// (elapsed/duration) and — for the ceiling band and marker placement — its max-depth
    /// normalization. Returns <see cref="ProfileOverlays.Empty"/> for an empty sample set.
    /// </summary>
    public static ProfileOverlays BuildProfileOverlays(IReadOnlyList<ProfileSampleDto> samples, DepthChartPath depth)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(depth);
        if (samples.Count == 0)
        {
            return ProfileOverlays.Empty;
        }

        var ordered = samples.OrderBy(s => s.TSeconds).ToArray();

        // Share the depth chart's axis exactly; fall back to the samples only if a caller handed us
        // an Empty path alongside real samples (keeps the builder robust in isolation).
        var duration = depth.DurationSeconds > 0 ? depth.DurationSeconds : ordered[^1].TSeconds;
        var maxDepth = depth.MaxDepthM > 0 ? depth.MaxDepthM : ordered.Max(s => Math.Max(0, s.DepthM));

        var temperature = BuildSeries(ordered, duration, s => s.TempC, "temp", ChartAxisSide.Right);
        var tank = BuildSeries(ordered, duration, s => s.TankPressureBar, "tank_pressure", ChartAxisSide.Right);
        var ndl = BuildSeries(ordered, duration, s => s.Ndl, "ndl", ChartAxisSide.Right);
        var ceiling = BuildCeilingBand(ordered, duration, maxDepth);
        var markers = BuildMarkers(ordered, duration, maxDepth);

        return new ProfileOverlays(temperature, tank, ceiling, ndl, markers);
    }

    /// <summary>
    /// Builds the columnar WCAG data-table alternative. Elapsed and Depth are always present (every
    /// sample carries them); every other column appears only when at least one sample records it, in
    /// the fixed order Elapsed · Depth · Temp · PpO2 · NDL · Ceiling · Ascent · Tank · HR · Setpoint.
    /// The Ascent column is the text equivalent of the AscentWarning overlay marker (recorded ascent
    /// rate, m/min) — so the table extends to every drawn overlay and no channel is color/marker-only.
    /// Cells are invariant numeric strings (the integrator re-formats via UnitFormatter); a null value
    /// in an included column is an empty cell.
    /// </summary>
    public static ProfileTable BuildProfileTable(IReadOnlyList<ProfileSampleDto> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Count == 0)
        {
            return new ProfileTable([], []);
        }

        var ordered = samples.OrderBy(s => s.TSeconds).ToArray();

        var columns = new List<(string Header, Func<ProfileSampleDto, string> Cell)>
        {
            ("Elapsed", s => s.TSeconds.ToString(CultureInfo.InvariantCulture)),
            ("Depth", s => s.DepthM.ToString(CultureInfo.InvariantCulture)),
        };

        AddOptional(columns, ordered, "Temp", s => s.TempC);
        AddOptional(columns, ordered, "PpO2", s => s.PpO2);
        AddOptional(columns, ordered, "NDL", s => s.Ndl);
        AddOptional(columns, ordered, "Ceiling", s => s.CeilingM);
        AddOptional(columns, ordered, "Ascent", s => s.AscentRateMPerMin); // text equivalent of the AscentWarning marker
        AddOptional(columns, ordered, "Tank", s => s.TankPressureBar);
        AddOptional(columns, ordered, "HR", s => s.HeartRate);
        AddOptional(columns, ordered, "Setpoint", s => s.Setpoint);

        var headers = columns.Select(c => c.Header).ToArray();
        var rows = new IReadOnlyList<string>[ordered.Length];
        for (var i = 0; i < ordered.Length; i++)
        {
            var sample = ordered[i];
            rows[i] = columns.Select(c => c.Cell(sample)).ToArray();
        }

        return new ProfileTable(headers, rows);
    }

    private static double AxisX(int tSeconds, int duration) =>
        duration <= 0 ? 0 : (double)tSeconds / duration;

    private static double DepthY(double depthM, double maxDepth) =>
        maxDepth <= 0 ? 0 : Math.Clamp(Math.Max(0, depthM) / maxDepth, 0, 1);

    /// <summary>
    /// Emits a secondary series for <paramref name="channel"/> only when ≥2 samples carry it.
    /// Y is value-up over the series' own [min,max]: max → 0 (top), min → 1 (bottom), flat → 0.5.
    /// </summary>
    private static ChartSeries? BuildSeries(
        IReadOnlyList<ProfileSampleDto> ordered,
        int duration,
        Func<ProfileSampleDto, double?> channel,
        string channelKey,
        ChartAxisSide axis)
    {
        var present = ordered.Where(s => channel(s) is not null).ToArray();
        if (present.Length < 2)
        {
            return null;
        }

        var values = present.Select(s => channel(s)!.Value).ToArray();
        var min = values.Min();
        var max = values.Max();
        var range = max - min;

        var points = new ChartPoint[present.Length];
        for (var i = 0; i < present.Length; i++)
        {
            var y = range <= 0 ? 0.5 : (max - values[i]) / range;
            points[i] = new ChartPoint(AxisX(present[i].TSeconds, duration), y);
        }

        return new ChartSeries(points, min, max, channelKey, axis);
    }

    /// <summary>
    /// The recorded-ceiling do-not-ascend band: Upper = surface (Y = 0), Lower = ceiling depth
    /// normalized by the SAME max depth as the main line. Emitted only when ≥1 sample records a
    /// ceiling &gt; 0.
    /// </summary>
    private static ChartBand? BuildCeilingBand(IReadOnlyList<ProfileSampleDto> ordered, int duration, double maxDepth)
    {
        var withCeiling = ordered.Where(s => s.CeilingM is > 0).ToArray();
        if (withCeiling.Length == 0)
        {
            return null;
        }

        var upper = new ChartPoint[withCeiling.Length];
        var lower = new ChartPoint[withCeiling.Length];
        for (var i = 0; i < withCeiling.Length; i++)
        {
            var x = AxisX(withCeiling[i].TSeconds, duration);
            upper[i] = new ChartPoint(x, 0);
            lower[i] = new ChartPoint(x, DepthY(withCeiling[i].CeilingM!.Value, maxDepth));
        }

        return new ChartBand(upper, lower, "ceiling");
    }

    /// <summary>
    /// Recorded event markers only: a Setpoint marker at each recorded setpoint transition; a single
    /// DecoOnset at the first sample with recorded NDL ≤ 0 OR ceiling &gt; 0; an AscentWarning at the
    /// rising edge of each recorded fast-ascent run (rate &gt; <see cref="ChartThresholds.AscentWarnMPerMin"/>).
    /// GasSwitch is never emitted per-sample — the schema carries no per-sample gas identity.
    /// </summary>
    private static IReadOnlyList<ChartMarker> BuildMarkers(IReadOnlyList<ProfileSampleDto> ordered, int duration, double maxDepth)
    {
        var markers = new List<ChartMarker>();

        // Setpoint transitions (CCR): a recorded change between consecutive samples.
        for (var i = 1; i < ordered.Count; i++)
        {
            var prev = ordered[i - 1].Setpoint;
            var cur = ordered[i].Setpoint;
            if (prev is not null && cur is not null && Math.Abs(prev.Value - cur.Value) > SetpointEpsilon)
            {
                markers.Add(new ChartMarker(
                    AxisX(ordered[i].TSeconds, duration),
                    DepthY(ordered[i].DepthM, maxDepth),
                    ChartMarkerKind.Setpoint,
                    "Setpoint " + cur.Value.ToString("0.0", CultureInfo.InvariantCulture)));
            }
        }

        // Deco onset: the first sample the computer flagged as into-deco (recorded, never computed).
        var deco = ordered.FirstOrDefault(s => s.Ndl is <= 0 || s.CeilingM is > 0);
        if (deco is not null)
        {
            markers.Add(new ChartMarker(
                AxisX(deco.TSeconds, duration),
                DepthY(deco.DepthM, maxDepth),
                ChartMarkerKind.DecoOnset,
                "Deco"));
        }

        // Ascent warnings: rising edge of each recorded fast-ascent run (dedupes contiguous samples).
        var warned = false;
        foreach (var s in ordered)
        {
            var fast = s.AscentRateMPerMin is { } rate && rate > ChartThresholds.AscentWarnMPerMin;
            if (fast && !warned)
            {
                markers.Add(new ChartMarker(
                    AxisX(s.TSeconds, duration),
                    DepthY(s.DepthM, maxDepth),
                    ChartMarkerKind.AscentWarning,
                    "Ascent " + s.AscentRateMPerMin!.Value.ToString("0", CultureInfo.InvariantCulture) + " m/min"));
            }

            warned = fast;
        }

        markers.Sort((a, b) => a.X.CompareTo(b.X));
        return markers;
    }

    private static void AddOptional(
        List<(string Header, Func<ProfileSampleDto, string> Cell)> columns,
        IReadOnlyList<ProfileSampleDto> ordered,
        string header,
        Func<ProfileSampleDto, double?> selector)
    {
        if (!ordered.Any(s => selector(s) is not null))
        {
            return;
        }

        columns.Add((header, s => selector(s) is { } v ? v.ToString(CultureInfo.InvariantCulture) : string.Empty));
    }
}
