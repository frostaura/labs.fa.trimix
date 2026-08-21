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

        var ceilings = BuildCeilingBands(ordered, duration, maxDepth);
        var markers = BuildMarkers(ordered, duration, maxDepth);

        return new ProfileOverlays(ceilings, markers);
    }

    /// <summary>Point budget for the temperature mini-lane's drawn polyline.</summary>
    internal const int TemperatureLanePoints = 120;

    /// <summary>Floor for the temperature smoothing window; the real window scales with the decimation ratio.</summary>
    internal const int TemperatureSmoothingWindow = 5;

    /// <summary>
    /// Builds the temperature mini-lane that renders under the depth chart, sharing its X axis. Returns null
    /// when fewer than two samples carry a temperature (a single reading is a value, not a series).
    ///
    /// <para>Two deliberate departures from naive normalisation, both required for this to read as a curve:</para>
    /// <list type="number">
    ///   <item><description><b>A minimum axis span.</b> Temperatures arrive as whole °C, so a 26–28 °C dive has
    ///   only three distinct values; scaling those to the full lane height produces a binary staircase with
    ///   top-to-bottom vertical strokes wherever the sensor flickers between adjacent degrees. Padding the axis
    ///   to at least <see cref="ChartThresholds.MinTemperatureSpanC"/> keeps 1 °C to a fraction of the
    ///   height.</description></item>
    ///   <item><description><b>Smooth before decimating.</b> Reducing ~5,600 samples to 120 is a ~47:1 ratio;
    ///   point-sampling that would alias the flicker into arbitrary spikes. A centred moving average whose width
    ///   scales with the ratio removes the quantisation noise first.</description></item>
    /// </list>
    /// </summary>
    public static TemperatureLane? BuildTemperatureLane(IReadOnlyList<ProfileSampleDto> samples, DepthChartPath depth)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(depth);

        var present = samples
            .Where(s => s.TempC is not null)
            .OrderBy(s => s.TSeconds)
            .ToArray();
        if (present.Length < 2)
        {
            return null;
        }

        var values = present.Select(s => s.TempC!.Value).ToArray();
        var observedMin = values.Min();
        var observedMax = values.Max();

        // Padded, whole-degree axis with a minimum span.
        var mid = (observedMin + observedMax) / 2.0;
        var span = Math.Max(observedMax - observedMin, ChartThresholds.MinTemperatureSpanC);
        var axisMin = Math.Floor(mid - (span / 2.0));
        var axisMax = Math.Ceiling(mid + (span / 2.0));
        var axisRange = axisMax - axisMin;

        var smoothed = SmoothForLane(values, TemperatureLanePoints);

        var duration = depth.DurationSeconds > 0 ? depth.DurationSeconds : present[^1].TSeconds;
        var kept = KeepIndices(present.Length, TemperatureLanePoints, i => smoothed[i]);

        var points = new ChartPoint[kept.Length];
        for (var i = 0; i < kept.Length; i++)
        {
            var index = kept[i];
            var y = axisRange <= 0 ? 0.5 : (axisMax - smoothed[index]) / axisRange;
            points[i] = new ChartPoint(AxisX(present[index].TSeconds, duration), Math.Clamp(y, 0, 1));
        }

        return new TemperatureLane(points, axisMin, axisMax, observedMin, observedMax);
    }

    /// <summary>
    /// Centred moving average sized to the decimation ratio, edges clamped by replication. Width is odd so the
    /// window is symmetric about each sample.
    /// </summary>
    private static double[] SmoothForLane(double[] values, int targetPoints)
    {
        // Smoothing exists ONLY to stop decimation aliasing the quantisation noise. A series that fits inside
        // the point budget is drawn as recorded, so smoothing it would distort real readings for no benefit —
        // and on a handful of samples it would flatten the curve entirely.
        if (values.Length <= targetPoints)
        {
            return values;
        }

        var ratio = (int)Math.Ceiling((double)values.Length / Math.Max(1, targetPoints));
        var window = Math.Max(TemperatureSmoothingWindow, (2 * ratio) + 1);
        if (window <= 1)
        {
            return values;
        }

        var half = window / 2;
        var smoothed = new double[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            double sum = 0;
            for (var offset = -half; offset <= half; offset++)
            {
                sum += values[Math.Clamp(i + offset, 0, values.Length - 1)];
            }

            smoothed[i] = sum / window;
        }

        return smoothed;
    }

    /// <summary>
    /// Interpolates the lane's temperature (°C) at normalized elapsed <paramref name="x"/> ∈ [0,1] for the
    /// shared scrub readout — the temperature counterpart of <see cref="YAtX"/>. Clamped to the lane's ends.
    /// </summary>
    public static double? TempAtX(TemperatureLane? lane, double x)
    {
        if (lane is null || lane.Points.Count == 0)
        {
            return null;
        }

        var points = lane.Points;
        var y = points[^1].Y;

        if (x <= points[0].X)
        {
            y = points[0].Y;
        }
        else
        {
            for (var i = 1; i < points.Count; i++)
            {
                if (x <= points[i].X)
                {
                    var (x0, y0) = (points[i - 1].X, points[i - 1].Y);
                    var (x1, y1) = (points[i].X, points[i].Y);
                    var t = x1 <= x0 ? 0 : (x - x0) / (x1 - x0);
                    y = y0 + ((y1 - y0) * t);
                    break;
                }
            }
        }

        // Y is value-up over the padded axis; invert it back to a temperature.
        return lane.AxisMaxC - (y * (lane.AxisMaxC - lane.AxisMinC));
    }

    /// <summary>
    /// The dive's average depth as a horizontal reference line, normalized against the SAME max depth as the
    /// depth polyline. Null when there is nothing meaningful to draw.
    ///
    /// <para>The clamp is load-bearing: a merged cross-source dive can carry a summary average deeper than the
    /// deepest sample that survived import, which would otherwise place the line below the plot floor.</para>
    /// </summary>
    public static DepthReference? BuildAverageDepthReference(DepthChartPath chart, double? averageDepthM)
    {
        ArgumentNullException.ThrowIfNull(chart);
        if (chart.IsEmpty || chart.MaxDepthM <= 0 || averageDepthM is not double avg || avg <= 0)
        {
            return null;
        }

        return new DepthReference(Math.Clamp(avg / chart.MaxDepthM, 0, 1), avg);
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

        // Downsample before building rows. A cloud-synced dive computer profile is 1 Hz — a 100-minute
        // dive is ~5,600 samples — and the table is rendered by a BindableLayout, which materializes every
        // item even while collapsed. Emitting one row per sample therefore creates tens of thousands of
        // views on the UI thread and wedges the page for a minute or more.
        //
        // Capping is also the better accessibility outcome, not a compromise: 5,600 rows is not navigable
        // by a screen reader, whereas an evenly spaced sweep that always retains the first sample, the last
        // sample and the deepest point conveys the shape of the dive.
        var selected = Downsample(ordered, MaxTableRows);

        var headers = columns.Select(c => c.Header).ToArray();
        var rows = new IReadOnlyList<string>[selected.Length];
        for (var i = 0; i < selected.Length; i++)
        {
            var sample = selected[i];
            rows[i] = columns.Select(c => c.Cell(sample)).ToArray();
        }

        return new ProfileTable(headers, rows);
    }

    /// <summary>Row ceiling for the accessible table alternative. See <see cref="BuildProfileTable"/>.</summary>
    internal const int MaxTableRows = 150;

    /// <summary>
    /// Evenly reduces <paramref name="ordered"/> to at most <paramref name="max"/> samples, always keeping
    /// the first, the last and the deepest sample so the extremes a diver cares about survive.
    /// </summary>
    private static ProfileSampleDto[] Downsample(ProfileSampleDto[] ordered, int max) =>
        ordered.Length <= max
            ? ordered
            : [.. KeepIndices(ordered.Length, max, i => ordered[i].DepthM).Select(i => ordered[i])];

    /// <summary>
    /// Chooses which indices of a <paramref name="count"/>-long series survive a reduction to at most
    /// <paramref name="max"/>: always the first, the last and the single highest-ranked element, then evenly
    /// spaced fill for whatever budget remains. Returned ascending, so the reduced series keeps its order.
    ///
    /// <para>Shared deliberately by the accessible table (ranked by depth, so the deepest row survives) and by
    /// the drawn polyline (ranked by normalized Y, so the plot floor is still touched). One algorithm means the
    /// table's existing downsample tests also cover the render path.</para>
    /// </summary>
    private static int[] KeepIndices(int count, int max, Func<int, double> rank)
    {
        if (count <= max)
        {
            return [.. Enumerable.Range(0, count)];
        }

        var keep = new SortedSet<int> { 0, count - 1 };

        var highest = 0;
        for (var i = 1; i < count; i++)
        {
            if (rank(i) > rank(highest))
            {
                highest = i;
            }
        }

        keep.Add(highest);

        // Fill the remaining budget with evenly spaced indices.
        var budget = max - keep.Count;
        if (budget > 0)
        {
            var step = (double)(count - 1) / (budget + 1);
            for (var n = 1; n <= budget; n++)
            {
                keep.Add((int)Math.Round(n * step));
            }
        }

        return [.. keep];
    }

    private static double AxisX(int tSeconds, int duration) =>
        duration <= 0 ? 0 : (double)tSeconds / duration;

    private static double DepthY(double depthM, double maxDepth) =>
        maxDepth <= 0 ? 0 : Math.Clamp(Math.Max(0, depthM) / maxDepth, 0, 1);

    /// <summary>
    /// The recorded-ceiling do-not-ascend zones: Upper = surface (Y = 0), Lower = ceiling depth normalized by
    /// the SAME max depth as the main line. One band per <b>contiguous run</b> of samples recording a ceiling.
    ///
    /// <para>Runs matter. Filtering to "every sample with a ceiling" and emitting one band loses the fact that
    /// a gap existed, and the drawable then closes a single polygon across it — bridging two separate deco
    /// windows into one continuous zone and asserting a ceiling over minutes that recorded none. Garmin's
    /// ceiling channel flickers 0 → 3 → 0, so this is the common case, not an edge case.</para>
    ///
    /// <para>A run of one sample is skipped: it has zero width, so it would render as an invisible sliver or a
    /// bare vertical line. The DecoOnset marker already flags the moment deco began.</para>
    /// </summary>
    private static IReadOnlyList<ChartBand> BuildCeilingBands(
        IReadOnlyList<ProfileSampleDto> ordered, int duration, double maxDepth)
    {
        var bands = new List<ChartBand>();
        var run = new List<ProfileSampleDto>();

        void Flush()
        {
            if (run.Count >= 2)
            {
                var upper = new ChartPoint[run.Count];
                var lower = new ChartPoint[run.Count];
                for (var i = 0; i < run.Count; i++)
                {
                    var x = AxisX(run[i].TSeconds, duration);
                    upper[i] = new ChartPoint(x, 0);
                    lower[i] = new ChartPoint(x, DepthY(run[i].CeilingM!.Value, maxDepth));
                }

                bands.Add(new ChartBand(upper, lower, "ceiling"));
            }

            run.Clear();
        }

        foreach (var sample in ordered)
        {
            if (sample.CeilingM is > 0)
            {
                run.Add(sample);
            }
            else
            {
                Flush();
            }
        }

        Flush();
        return bands;
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

        // Ascent warnings: rising edge of each recorded fast-ascent run, with HYSTERESIS. The run only ends
        // once the recorded rate falls below AscentWarnClearMPerMin — without that, a 1 Hz rate oscillating
        // around the 10 m/min threshold emits a marker on every crossing and buries the chart in chevrons.
        var warned = false;
        foreach (var s in ordered)
        {
            if (s.AscentRateMPerMin is not { } rate)
            {
                continue;
            }

            if (rate > ChartThresholds.AscentWarnMPerMin)
            {
                if (!warned)
                {
                    markers.Add(new ChartMarker(
                        AxisX(s.TSeconds, duration),
                        DepthY(s.DepthM, maxDepth),
                        ChartMarkerKind.AscentWarning,
                        "Ascent " + rate.ToString("0", CultureInfo.InvariantCulture) + " m/min"));
                }

                warned = true;
            }
            else if (rate < ChartThresholds.AscentWarnClearMPerMin)
            {
                warned = false;
            }
        }

        markers.Sort((a, b) => a.X.CompareTo(b.X));
        return ThinMarkers(markers);
    }

    /// <summary>Minimum X separation between two markers of the SAME kind (fraction of the timeline).</summary>
    internal const double MarkerMinXSeparation = 0.02;

    /// <summary>Most markers of any one kind that will be drawn.</summary>
    internal const int MaxMarkersPerKind = 6;

    /// <summary>
    /// Bounds marker density so the chart stays readable: within each kind, drops any marker closer than
    /// <see cref="MarkerMinXSeparation"/> to one already kept, then caps the kind at
    /// <see cref="MaxMarkersPerKind"/>. Input must already be X-ordered; output stays X-ordered.
    ///
    /// <para><see cref="ChartMarkerKind.DecoOnset"/> is exempt from both rules — there is only ever one, and it
    /// is the most safety-relevant glyph on the chart, so it must never be thinned away by neighbours.</para>
    /// </summary>
    private static IReadOnlyList<ChartMarker> ThinMarkers(List<ChartMarker> ordered)
    {
        var kept = new List<ChartMarker>(ordered.Count);
        var lastX = new Dictionary<ChartMarkerKind, double>();
        var counts = new Dictionary<ChartMarkerKind, int>();

        foreach (var marker in ordered)
        {
            if (marker.Kind == ChartMarkerKind.DecoOnset)
            {
                kept.Add(marker);
                continue;
            }

            if (lastX.TryGetValue(marker.Kind, out var previous)
                && marker.X - previous < MarkerMinXSeparation)
            {
                continue;
            }

            counts.TryGetValue(marker.Kind, out var count);
            if (count >= MaxMarkersPerKind)
            {
                continue;
            }

            kept.Add(marker);
            lastX[marker.Kind] = marker.X;
            counts[marker.Kind] = count + 1;
        }

        return kept;
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
