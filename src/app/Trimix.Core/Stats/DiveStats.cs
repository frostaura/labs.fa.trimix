using System.Globalization;
using System.Text.RegularExpressions;
using Trimix.Contracts.Dtos;

namespace Trimix.Core.Stats;

/// <summary>
/// Pure, null-tolerant career analytics over a set of dives (aggregator architecture §8.3). Every
/// metric that leans on an optional channel returns a <see cref="StatValue{T}"/> so the UI can caption
/// it with its <see cref="Completeness"/> ("SAC available for 42 of 120 dives") — heterogeneous-fidelity
/// sources never silently skew a stat. Storage stays metric; unit formatting is the UI's job.
///
/// <para>Guardrail: everything here is arithmetic over values a source <b>recorded</b> (depth, pressure,
/// recorded NDL/ceiling). Nothing here computes tissue loading or deco.</para>
/// </summary>
public static partial class DiveStats
{
    /// <summary>The three lifetime aggregates the logbook strip shows: dive count, deepest, total bottom time.</summary>
    public static CareerSummary Summarize(IReadOnlyList<DiveDto> dives)
    {
        ArgumentNullException.ThrowIfNull(dives);
        if (dives.Count == 0)
        {
            return CareerSummary.Empty;
        }

        return new CareerSummary(
            dives.Count,
            dives.Max(d => d.MaxDepthM),
            dives.Sum(d => d.DurationSec));
    }

    /// <summary>Dive-count histogram by max-depth band <c>[n·binM, (n+1)·binM)</c>. Contiguous, zero-filled bands.</summary>
    public static StatValue<IReadOnlyList<HistogramBin>> DepthHistogram(IReadOnlyList<DiveDto> dives, double binM = 5)
    {
        ArgumentNullException.ThrowIfNull(dives);
        if (binM <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(binM));
        }

        if (dives.Count == 0)
        {
            return new StatValue<IReadOnlyList<HistogramBin>>([], Completeness.None);
        }

        var counts = new Dictionary<int, int>();
        var maxIndex = 0;
        foreach (var dive in dives)
        {
            var index = (int)Math.Floor(Math.Max(0, dive.MaxDepthM) / binM);
            counts[index] = counts.GetValueOrDefault(index) + 1;
            maxIndex = Math.Max(maxIndex, index);
        }

        var bins = new List<HistogramBin>(maxIndex + 1);
        for (var i = 0; i <= maxIndex; i++)
        {
            bins.Add(new HistogramBin(i * binM, (i + 1) * binM, counts.GetValueOrDefault(i)));
        }

        return new StatValue<IReadOnlyList<HistogramBin>>(bins, new Completeness(dives.Count, dives.Count));
    }

    /// <summary>Seconds spent per depth band, summed from profile samples (left-Riemann over sample intervals).</summary>
    public static StatValue<IReadOnlyList<DepthBandTime>> TimeAtDepth(IReadOnlyList<DiveDto> dives, double binM = 5)
    {
        ArgumentNullException.ThrowIfNull(dives);
        if (binM <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(binM));
        }

        var seconds = new Dictionary<int, double>();
        var maxIndex = 0;
        var withSamples = 0;
        foreach (var dive in dives)
        {
            var samples = dive.ProfileSamples;
            if (samples.Count == 0)
            {
                continue;
            }

            withSamples++;
            var ordered = samples.OrderBy(s => s.TSeconds).ToArray();
            for (var i = 0; i < ordered.Length - 1; i++)
            {
                var dt = ordered[i + 1].TSeconds - ordered[i].TSeconds;
                if (dt <= 0)
                {
                    continue;
                }

                var index = (int)Math.Floor(Math.Max(0, ordered[i].DepthM) / binM);
                seconds[index] = seconds.GetValueOrDefault(index) + dt;
                maxIndex = Math.Max(maxIndex, index);
            }
        }

        if (withSamples == 0)
        {
            return new StatValue<IReadOnlyList<DepthBandTime>>([], new Completeness(0, dives.Count));
        }

        var bands = new List<DepthBandTime>(maxIndex + 1);
        for (var i = 0; i <= maxIndex; i++)
        {
            bands.Add(new DepthBandTime(i * binM, (i + 1) * binM, seconds.GetValueOrDefault(i)));
        }

        return new StatValue<IReadOnlyList<DepthBandTime>>(bands, new Completeness(withSamples, dives.Count));
    }

    /// <summary>Dive counts per calendar month, chronologically ordered.</summary>
    public static IReadOnlyList<MonthCount> DivesByMonth(IReadOnlyList<DiveDto> dives)
    {
        ArgumentNullException.ThrowIfNull(dives);
        return dives
            .GroupBy(d => (d.StartUtc.Year, d.StartUtc.Month))
            .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
            .Select(g => new MonthCount(g.Key.Year, g.Key.Month, g.Count()))
            .ToList();
    }

    /// <summary>Per-calendar-year rollup: dive count, total bottom time, deepest.</summary>
    public static IReadOnlyList<YearRollup> PerYear(IReadOnlyList<DiveDto> dives)
    {
        ArgumentNullException.ThrowIfNull(dives);
        return dives
            .GroupBy(d => d.StartUtc.Year)
            .OrderBy(g => g.Key)
            .Select(g => new YearRollup(g.Key, g.Count(), g.Sum(d => d.DurationSec), g.Max(d => d.MaxDepthM)))
            .ToList();
    }

    /// <summary>The deepest dive (by max depth), or an empty <see cref="StatValue{T}"/> when there are none.</summary>
    public static StatValue<DiveDto?> Deepest(IReadOnlyList<DiveDto> dives)
    {
        ArgumentNullException.ThrowIfNull(dives);
        if (dives.Count == 0)
        {
            return new StatValue<DiveDto?>(null, Completeness.None);
        }

        var deepest = dives.Aggregate((best, d) => d.MaxDepthM > best.MaxDepthM ? d : best);
        return new StatValue<DiveDto?>(deepest, new Completeness(dives.Count, dives.Count));
    }

    /// <summary>The longest dive (by duration), or an empty <see cref="StatValue{T}"/> when there are none.</summary>
    public static StatValue<DiveDto?> Longest(IReadOnlyList<DiveDto> dives)
    {
        ArgumentNullException.ThrowIfNull(dives);
        if (dives.Count == 0)
        {
            return new StatValue<DiveDto?>(null, Completeness.None);
        }

        var longest = dives.Aggregate((best, d) => d.DurationSec > best.DurationSec ? d : best);
        return new StatValue<DiveDto?>(longest, new Completeness(dives.Count, dives.Count));
    }

    /// <summary>
    /// Mean average depth (m) over dives that carry a usable average depth (<c>AvgDepthM</c>, else the
    /// arithmetic mean of the profile samples' depth — recorded-depth arithmetic, not deco).
    /// </summary>
    public static StatValue<double> AverageDepthM(IReadOnlyList<DiveDto> dives)
    {
        ArgumentNullException.ThrowIfNull(dives);
        var values = dives.Select(TryAverageDepth).Where(v => v is not null).Select(v => v!.Value).ToList();
        return values.Count == 0
            ? new StatValue<double>(0, new Completeness(0, dives.Count))
            : new StatValue<double>(values.Average(), new Completeness(values.Count, dives.Count));
    }

    /// <summary>Mean dive duration (seconds). Duration is always present, so completeness is full.</summary>
    public static StatValue<double> AverageDurationSec(IReadOnlyList<DiveDto> dives)
    {
        ArgumentNullException.ThrowIfNull(dives);
        return dives.Count == 0
            ? new StatValue<double>(0, Completeness.None)
            : new StatValue<double>(dives.Average(d => d.DurationSec), new Completeness(dives.Count, dives.Count));
    }

    /// <summary>Coldest / warmest temperature (°C) across dive-level <c>MinTempC</c> and per-sample temps.</summary>
    public static StatValue<TempRange> TemperatureRange(IReadOnlyList<DiveDto> dives)
    {
        ArgumentNullException.ThrowIfNull(dives);
        double? min = null, max = null;
        var contributing = 0;
        foreach (var dive in dives)
        {
            var diveMin = min;
            var diveMax = max;
            var hasTemp = false;

            void Observe(double t)
            {
                hasTemp = true;
                diveMin = diveMin is null ? t : Math.Min(diveMin.Value, t);
                diveMax = diveMax is null ? t : Math.Max(diveMax.Value, t);
            }

            if (dive.MinTempC is double m)
            {
                Observe(m);
            }

            foreach (var s in dive.ProfileSamples)
            {
                if (s.TempC is double t)
                {
                    Observe(t);
                }
            }

            if (hasTemp)
            {
                contributing++;
                min = diveMin;
                max = diveMax;
            }
        }

        return contributing == 0
            ? new StatValue<TempRange>(new TempRange(0, 0), new Completeness(0, dives.Count))
            : new StatValue<TempRange>(new TempRange(min!.Value, max!.Value), new Completeness(contributing, dives.Count));
    }

    /// <summary>Dive counts by gas classification (Air / Nitrox / Trimix / Unknown), cylinders first then <c>GasMix</c>.</summary>
    public static StatValue<GasMixSummary> GasMixBreakdown(IReadOnlyList<DiveDto> dives)
    {
        ArgumentNullException.ThrowIfNull(dives);
        int air = 0, nitrox = 0, trimix = 0, unknown = 0;
        foreach (var dive in dives)
        {
            switch (Classify(dive))
            {
                case GasClass.Air: air++; break;
                case GasClass.Nitrox: nitrox++; break;
                case GasClass.Trimix: trimix++; break;
                default: unknown++; break;
            }
        }

        var known = air + nitrox + trimix;
        return new StatValue<GasMixSummary>(
            new GasMixSummary(air, nitrox, trimix, unknown),
            new Completeness(known, dives.Count));
    }

    /// <summary>Per-dive SAC/RMV points (chronological), for the dives where SAC is computable.</summary>
    public static StatValue<IReadOnlyList<SacPoint>> SacOverTime(IReadOnlyList<DiveDto> dives)
    {
        ArgumentNullException.ThrowIfNull(dives);
        var points = new List<SacPoint>();
        foreach (var dive in dives.OrderBy(d => d.StartUtc))
        {
            var (sac, rmv) = DiveSac(dive);
            if (sac is not null)
            {
                points.Add(new SacPoint(dive.Id, dive.StartUtc, sac, rmv));
            }
        }

        return new StatValue<IReadOnlyList<SacPoint>>(points, new Completeness(points.Count, dives.Count));
    }

    /// <summary>Career SAC/RMV means with the dive counts that fed each.</summary>
    public static StatValue<SacStats> SacSummary(IReadOnlyList<DiveDto> dives)
    {
        ArgumentNullException.ThrowIfNull(dives);
        var sacs = new List<double>();
        var rmvs = new List<double>();
        foreach (var dive in dives)
        {
            var (sac, rmv) = DiveSac(dive);
            if (sac is double s)
            {
                sacs.Add(s);
            }

            if (rmv is double r)
            {
                rmvs.Add(r);
            }
        }

        var stats = new SacStats(
            sacs.Count > 0 ? sacs.Average() : 0,
            rmvs.Count > 0 ? rmvs.Average() : null,
            sacs.Count,
            rmvs.Count);
        return new StatValue<SacStats>(stats, new Completeness(sacs.Count, dives.Count));
    }

    /// <summary>Current (run ending at the most recent dive day) and longest consecutive-dive-day streaks.</summary>
    public static StreakStats Streaks(IReadOnlyList<DiveDto> dives)
    {
        ArgumentNullException.ThrowIfNull(dives);
        var days = dives
            .Select(d => DateOnly.FromDateTime(d.StartUtc))
            .Distinct()
            .OrderBy(d => d)
            .ToList();
        if (days.Count == 0)
        {
            return StreakStats.None;
        }

        var longest = 1;
        var run = 1;
        for (var i = 1; i < days.Count; i++)
        {
            run = days[i] == days[i - 1].AddDays(1) ? run + 1 : 1;
            longest = Math.Max(longest, run);
        }

        var current = 1;
        for (var i = days.Count - 1; i > 0; i--)
        {
            if (days[i] == days[i - 1].AddDays(1))
            {
                current++;
            }
            else
            {
                break;
            }
        }

        return new StreakStats(current, longest);
    }

    /// <summary>Per-day dive counts for a single calendar year (GitHub-style grid), days with ≥1 dive only.</summary>
    public static IReadOnlyList<HeatmapDay> CalendarHeatmap(IReadOnlyList<DiveDto> dives, int year)
    {
        ArgumentNullException.ThrowIfNull(dives);
        return dives
            .Where(d => d.StartUtc.Year == year)
            .GroupBy(d => DateOnly.FromDateTime(d.StartUtc))
            .OrderBy(g => g.Key)
            .Select(g => new HeatmapDay(g.Key, g.Count()))
            .ToList();
    }

    /// <summary>
    /// Usable average depth (m) for a dive: <c>AvgDepthM</c> when present and positive, else the
    /// arithmetic mean of the recorded sample depths. Null when neither exists. Exposed for the
    /// integrator / tests; used by <see cref="AverageDepthM"/> and the SAC metrics.
    /// </summary>
    public static double? TryAverageDepth(DiveDto dive)
    {
        ArgumentNullException.ThrowIfNull(dive);
        if (dive.AvgDepthM is double avg && avg > 0)
        {
            return avg;
        }

        var samples = dive.ProfileSamples;
        if (samples.Count == 0)
        {
            return null;
        }

        return samples.Select(s => Math.Max(0, s.DepthM)).Average();
    }

    private static (double? Sac, double? Rmv) DiveSac(DiveDto dive)
    {
        if (dive.DurationSec <= 0 || dive.Cylinders is not { Count: > 0 } cylinders)
        {
            return (null, null);
        }

        if (TryAverageDepth(dive) is not double avgDepth)
        {
            return (null, null);
        }

        return SacFormula.ForDive(cylinders, avgDepth, dive.DurationSec);
    }
}
