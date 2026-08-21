namespace Trimix.Core.Charts;

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
/// The additive overlay bundle for one dive's depth chart. Both members are empty when the underlying source
/// recorded nothing beyond depth.
///
/// <para><see cref="CeilingBands"/> is a LIST, one band per contiguous run of recorded ceiling. A single band
/// spanning every ceiling sample would bridge the gaps between separate deco windows and paint a do-not-ascend
/// zone over minutes where none was recorded.</para>
///
/// <para>Temperature moved out to <see cref="TemperatureLane"/> (its own canvas). Tank-pressure and NDL series
/// were removed: no connector populates <c>TankPressureBar</c>, and NDL was never drawn — both remain available
/// as text in the accessible profile table and the recorded-readouts card.</para>
/// </summary>
public sealed record ProfileOverlays(
    IReadOnlyList<ChartBand> CeilingBands,
    IReadOnlyList<ChartMarker> Markers)
{
    public static readonly ProfileOverlays Empty = new([], []);

    public bool HasAny => CeilingBands.Count > 0 || Markers.Count > 0;
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

/// <summary>
/// The temperature mini-lane drawn UNDER the depth chart, sharing its X axis.
///
/// <para>Y is value-up over a PADDED whole-degree axis with a minimum span
/// (<see cref="ChartThresholds.MinTemperatureSpanC"/>). That padding is the whole point: dive computers report
/// temperature in whole °C, so normalising to the observed span turns 1 °C of quantisation into 50–100 % of the
/// lane height and the channel renders as a full-height binary staircase rather than a curve.</para>
///
/// <para><see cref="ObservedMinC"/>/<see cref="ObservedMaxC"/> are carried separately from the axis because
/// labels must show the temperature the diver actually saw, never the padded axis bounds.</para>
/// </summary>
public sealed record TemperatureLane(
    IReadOnlyList<ChartPoint> Points,
    double AxisMinC,
    double AxisMaxC,
    double ObservedMinC,
    double ObservedMaxC);

/// <summary>
/// One horizontal reference line on the depth axis — the dive's average depth. <see cref="Y"/> is normalized
/// against the same max depth as the depth polyline; <see cref="DepthM"/> is the value a label formats.
///
/// <para>Deliberately neither part of <see cref="ProfileOverlays"/> (that bundle is per-sample recorded
/// channels, whereas average depth is a dive-summary derivative) nor of <c>DepthChartPath</c> (that record is
/// sample-derived geometry pinned by existing tests).</para>
/// </summary>
public sealed record DepthReference(double Y, double DepthM);

/// <summary>Thresholds for recorded-channel markers (surfaced, never computed).</summary>
public static class ChartThresholds
{
    /// <summary>Ascent rate (m/min) above which a recorded sample raises an ascent-warning marker.</summary>
    public const double AscentWarnMPerMin = 10.0;

    /// <summary>
    /// Recorded ascent rate a fast-ascent run must drop BELOW before another warning can fire. Pure display
    /// de-bounce over recorded values — a rate oscillating around <see cref="AscentWarnMPerMin"/> otherwise
    /// emits a marker on every crossing. No tissue model is involved.
    /// </summary>
    public const double AscentWarnClearMPerMin = 8.0;

    /// <summary>Narrowest temperature span (°C) the mini-lane axis will ever scale to.</summary>
    public const double MinTemperatureSpanC = 4.0;
}
