namespace Trimix.Core.Stats;

/// <summary>The three lifetime aggregates the logbook strip shows (aggregator architecture §8.3).</summary>
public sealed record CareerSummary(int TotalDives, double MaxDepthM, int TotalBottomTimeSec)
{
    /// <summary>Empty career (no dives).</summary>
    public static readonly CareerSummary Empty = new(0, 0, 0);
}

/// <summary>One bar of a max-depth-count histogram: dives whose max depth falls in <c>[LowerM, UpperM)</c>.</summary>
public sealed record HistogramBin(double LowerM, double UpperM, int Count);

/// <summary>Total seconds spent inside a depth band <c>[LowerM, UpperM)</c>, summed across profile samples.</summary>
public sealed record DepthBandTime(double LowerM, double UpperM, double Seconds);

/// <summary>Dive count for one calendar month.</summary>
public sealed record MonthCount(int Year, int Month, int Count);

/// <summary>Rollup for one calendar year.</summary>
public sealed record YearRollup(int Year, int DiveCount, int TotalBottomTimeSec, double MaxDepthM);

/// <summary>Coldest / warmest temperature observed (°C), across dive-level and per-sample readings.</summary>
public sealed record TempRange(double MinC, double MaxC);

/// <summary>Dive counts by gas classification. Any dive lands in exactly one bucket.</summary>
public sealed record GasMixSummary(int Air, int Nitrox, int Trimix, int Unknown);

/// <summary>Per-dive SAC/RMV point for a trend chart. Components are null when not computable for that dive.</summary>
public sealed record SacPoint(Guid DiveId, DateTime StartUtc, double? SacBarPerMin, double? RmvLPerMin);

/// <summary>Career SAC/RMV means with the dive counts that fed each (RMV mean is null when no dive qualified).</summary>
public sealed record SacStats(double MeanSacBarPerMin, double? MeanRmvLPerMin, int SacDiveCount, int RmvDiveCount);

/// <summary>Consecutive-dive-day streaks: the run ending at the most recent dive day, and the longest ever.</summary>
public sealed record StreakStats(int CurrentDays, int LongestDays)
{
    /// <summary>No dives.</summary>
    public static readonly StreakStats None = new(0, 0);
}

/// <summary>One day of a GitHub-style calendar heatmap: the date and how many dives fell on it.</summary>
public sealed record HeatmapDay(DateOnly Date, int Count);
