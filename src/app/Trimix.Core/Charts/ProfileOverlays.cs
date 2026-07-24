namespace Trimix.Core.Charts;

/// <summary>Which side of the depth chart a secondary series is scaled against.</summary>
public enum ChartAxisSide
{
    Left,
    Right,
}

/// <summary>
/// A recorded secondary channel drawn over the depth chart. Shares the depth chart's X axis
/// (X = elapsed/duration ∈ [0,1]) but is scaled to its OWN [<see cref="MinValue"/>,
/// <see cref="MaxValue"/>] on a secondary (usually right) axis. Convention: value-up — the series
/// maximum maps to Y = 0 (top) and the minimum to Y = 1 (bottom); a flat series sits at Y = 0.5.
/// (Depth is the deliberate exception in <see cref="ChartGeometry.BuildDepthChart"/>: depth grows
/// downward because down is physically down.) <see cref="ChannelKey"/> ∈ {"temp","tank_pressure",
/// "ndl","hr"}.
/// </summary>
public sealed record ChartSeries(
    IReadOnlyList<ChartPoint> Points,
    double MinValue,
    double MaxValue,
    string ChannelKey,
    ChartAxisSide Axis);

/// <summary>
/// A shaded region between two curves that share the depth chart's X axis. For the ceiling band,
/// <see cref="Upper"/> is the surface (Y = 0) and <see cref="Lower"/> is the recorded-ceiling depth
/// curve, normalized against the SAME max depth as the main depth line — the do-not-ascend zone.
/// </summary>
public sealed record ChartBand(
    IReadOnlyList<ChartPoint> Upper,
    IReadOnlyList<ChartPoint> Lower,
    string ChannelKey);

/// <summary>A recorded point event on the shared time axis. <see cref="Y"/> is the normalized
/// depth-axis position at that sample (for placement against the depth line).</summary>
public sealed record ChartMarker(double X, double Y, ChartMarkerKind Kind, string Label);

/// <summary>Kinds of recorded event marker. None imply computation — each surfaces a value the
/// dive computer stored.</summary>
public enum ChartMarkerKind
{
    GasSwitch,
    DecoOnset,
    AscentWarning,
    Setpoint,
}

/// <summary>
/// The additive overlay bundle for one dive's depth chart. Every member is null/empty when the
/// underlying source did not record that channel (heterogeneous fidelity: a depth-temp-only source
/// yields at most <see cref="Temperature"/>). <see cref="Markers"/> is never null.
/// </summary>
public sealed record ProfileOverlays(
    ChartSeries? Temperature,
    ChartSeries? TankPressure,
    ChartBand? CeilingBand,
    ChartSeries? Ndl,
    IReadOnlyList<ChartMarker> Markers)
{
    public static readonly ProfileOverlays Empty = new(null, null, null, null, []);

    public bool HasAny =>
        Temperature is not null
        || TankPressure is not null
        || CeilingBand is not null
        || Ndl is not null
        || Markers.Count > 0;
}

/// <summary>
/// The accessible, columnar data-table alternative to the depth chart (WCAG). A column appears only
/// when at least one sample carries that channel, so the table grows exactly with the drawn overlays
/// and never shows an empty column for a depth-temp-only dive. Cells are invariant numeric strings;
/// the integrator re-formats via UnitFormatter for unit toggles.
/// </summary>
public sealed record ProfileTable(
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<string>> Rows);

/// <summary>Thresholds for recorded-channel markers (surfaced, never computed).</summary>
public static class ChartThresholds
{
    /// <summary>Ascent rate (m/min) above which a recorded sample raises an ascent-warning marker.</summary>
    public const double AscentWarnMPerMin = 10.0;
}
