using Trimix.Contracts.Dtos;

namespace Trimix.Core.Stats;

/// <summary>
/// Exact surface-air-consumption (SAC) and respiratory-minute-volume (RMV) arithmetic
/// (aggregator architecture §8.3). This is arithmetic over pressures a dive computer / transmitter
/// <b>recorded</b> — it is NOT deco / tissue-loading computation (the frozen liability line stands:
/// we never model tissue loading). Every method is null-tolerant and returns <c>null</c> when the
/// inputs a formula needs are absent.
///
/// <para>Per cylinder with <c>StartBar</c>, <c>EndBar</c> (both non-null, <c>StartBar &gt; EndBar</c>):</para>
/// <code>
/// consumedBar = StartBar − EndBar
/// Pavg (ATA)  = 1 + avgDepthM / 10
/// durationMin = DurationSec / 60
/// SAC (bar/min) =  consumedBar            / (Pavg × durationMin)   // volume-independent
/// RMV (L/min)   = (consumedBar × VolumeL) / (Pavg × durationMin)   // = SAC × VolumeL
/// </code>
/// </summary>
public static class SacFormula
{
    /// <summary>Metres of sea water equivalent to one bar of ambient pressure.</summary>
    public const double MetersSeaWaterPerBar = 10.0;

    /// <summary>Average ambient pressure (ATA) at a mean depth: <c>1 + avgDepthM / 10</c>.</summary>
    public static double AverageAtaPressure(double avgDepthM) =>
        1.0 + avgDepthM / MetersSeaWaterPerBar;

    /// <summary>
    /// SAC in bar/min for a single cylinder, or null unless <c>StartBar</c>/<c>EndBar</c> are both
    /// present with <c>StartBar &gt; EndBar</c>, <paramref name="durationSec"/> &gt; 0, and the average
    /// pressure is positive. Volume-independent (comparable only within the same cylinder size).
    /// </summary>
    public static double? Sac(CylinderDto cylinder, double avgDepthM, int durationSec)
    {
        ArgumentNullException.ThrowIfNull(cylinder);
        if (durationSec <= 0)
        {
            return null;
        }

        if (cylinder.StartBar is not double start || cylinder.EndBar is not double end || start <= end)
        {
            return null;
        }

        var pavg = AverageAtaPressure(avgDepthM);
        if (pavg <= 0)
        {
            return null;
        }

        var durationMin = durationSec / 60.0;
        return (start - end) / (pavg * durationMin);
    }

    /// <summary>
    /// RMV in L/min for a single cylinder (surface-litres consumed per minute). Needs everything
    /// <see cref="Sac"/> needs plus a positive cylinder <c>VolumeL</c>. Equals <c>SAC × VolumeL</c>.
    /// </summary>
    public static double? Rmv(CylinderDto cylinder, double avgDepthM, int durationSec)
    {
        ArgumentNullException.ThrowIfNull(cylinder);
        if (Sac(cylinder, avgDepthM, durationSec) is not double sac)
        {
            return null;
        }

        if (cylinder.VolumeL is not double volume || volume <= 0)
        {
            return null;
        }

        return sac * volume;
    }

    /// <summary>
    /// Dive-level SAC/RMV across all cylinders. SAC is the mean of the per-cylinder SACs that qualify
    /// (volume-independent, so a mean is the only meaningful single number for multi-tank dives); RMV
    /// is the sum of the per-cylinder RMVs (total surface consumption rate). Either component is null
    /// when no cylinder qualifies for it. For the common single-cylinder dive this reduces to that
    /// cylinder's SAC and RMV.
    /// </summary>
    public static (double? Sac, double? Rmv) ForDive(
        IReadOnlyList<CylinderDto> cylinders, double avgDepthM, int durationSec)
    {
        ArgumentNullException.ThrowIfNull(cylinders);
        var sacs = new List<double>();
        var rmvs = new List<double>();
        foreach (var cylinder in cylinders)
        {
            if (Sac(cylinder, avgDepthM, durationSec) is double sac)
            {
                sacs.Add(sac);
                if (Rmv(cylinder, avgDepthM, durationSec) is double rmv)
                {
                    rmvs.Add(rmv);
                }
            }
        }

        double? sacOut = sacs.Count > 0 ? sacs.Average() : null;
        double? rmvOut = rmvs.Count > 0 ? rmvs.Sum() : null;
        return (sacOut, rmvOut);
    }
}
