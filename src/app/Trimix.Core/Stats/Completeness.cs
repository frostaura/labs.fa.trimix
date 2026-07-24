namespace Trimix.Core.Stats;

/// <summary>
/// Data-completeness signal for a career aggregate (aggregator architecture §8.1). Multi-source
/// dives have heterogeneous fidelity (a HealthKit dive carries depth+temp only; a Shearwater/FIT
/// dive carries the full channel set), so every stat that depends on an optional channel reports
/// how many dives could actually feed it — the UI renders this verbatim as
/// "SAC available for 42 of 120 dives", so a depth-temp-only source never silently skews a stat.
/// </summary>
/// <param name="Available">Dives (or samples) that carried the channel this metric needs.</param>
/// <param name="Total">Dives (or samples) considered.</param>
public readonly record struct Completeness(int Available, int Total)
{
    /// <summary>Available / Total, or 0 when nothing was considered.</summary>
    public double Fraction => Total <= 0 ? 0 : (double)Available / Total;

    /// <summary>Nothing available (empty input, or no dive carried the channel).</summary>
    public static readonly Completeness None = new(0, 0);
}

/// <summary>
/// A computed statistic paired with its <see cref="Completeness"/>. Callers read <see cref="Value"/>
/// only when <see cref="HasData"/> is true; otherwise the value is the metric's neutral default
/// (0, null, or an empty collection) and the completeness explains why.
/// </summary>
public readonly record struct StatValue<T>(T Value, Completeness Completeness)
{
    /// <summary>True when at least one dive fed this metric.</summary>
    public bool HasData => Completeness.Available > 0;
}
