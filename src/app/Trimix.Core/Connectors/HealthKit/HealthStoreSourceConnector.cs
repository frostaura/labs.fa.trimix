using System.Globalization;
using Trimix.Contracts.Dtos;
using Trimix.Core.Abstractions;

namespace Trimix.Core.Connectors.HealthKit;

/// <summary>
/// The Apple-HealthKit source connector (architecture overview §2, §0 "the only first-party clean cloud
/// surface"). It enumerates <c>underwaterDiving</c> workouts through the <see cref="IHealthStore"/> port,
/// pulls each workout's depth + temperature series, re-segments the depth stream into individual dives on
/// surface gaps &gt; <see cref="SurfaceGapSeconds"/> s (the AWU2UDDF pattern — one workout can hold a
/// repetitive-dive pair), and maps each segment straight into the existing <see cref="ImportedDive"/> seam
/// so the whole <c>ImportService</c> downstream (site match → cap → <c>dive_logged_import</c> → paywall)
/// runs unchanged. HealthKit is depth+temp only, so <see cref="ImportedDive.MinTempC"/> and each sample's
/// <see cref="ProfileSampleDto.TempC"/> are populated when a reading is in range and <em>every other rich
/// channel stays null</em> (gas, tank, NDL, ceiling, CNS, setpoint, HR).
///
/// State (<c>_buffer</c>) is per-session: <see cref="ListDivesSinceAsync"/> does the enumerate → read →
/// segment work once and buffers the resulting dives; <see cref="FetchProfileAsync"/> resolves a
/// <see cref="DiveRef"/> back to its buffered dive — mirroring <c>BleDeviceSourceConnector</c>. The
/// connector depends only on the Core port, never on a HealthKit binding, so it compiles + unit-tests on
/// net10.0 with a fake store; the real bindings live in the iOS platform impl.
///
/// Legally clean: reading the user's own HealthKit store touches no vendor cloud or ToS
/// (<see cref="LegalPosture.CleanOfficial"/>), so it ships on (<c>EnabledByDefault=true</c>). iOS-only —
/// Android has no dive-profile health surface, so <see cref="Platforms.iOS"/>.
/// </summary>
public sealed class HealthStoreSourceConnector(IHealthStore healthStore) : IDiveSourceConnector
{
    /// <summary>A depth-sample gap wider than this ends one dive and starts the next (AWU2UDDF surface-interval rule).</summary>
    public const double SurfaceGapSeconds = 60.0;

    /// <summary>A depth sample borrows the nearest temperature reading within this window; beyond it, the sample's temp is null.</summary>
    private const double TemperatureMatchToleranceSeconds = 15.0;

    private IReadOnlyList<ImportedDive> _buffer = [];

    public ConnectorDescriptor Descriptor { get; } = new(
        ConnectorId: "apple_health",
        Kind: ConnectorKind.HealthStore,
        Legal: LegalPosture.CleanOfficial,
        Fidelity: ProfileFidelity.DepthTempOnly,
        Auth: AuthModel.OnDeviceConsent,
        Platforms: Platforms.iOS,
        EnabledByDefault: true);

    public Task<ConnectorAuthResult> AuthenticateAsync(CancellationToken ct = default) =>
        healthStore.RequestAuthorizationAsync(ct);

    public async Task<IReadOnlyList<DiveRef>> ListDivesSinceAsync(SyncCursor cursor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cursor);

        var dives = new List<ImportedDive>();
        await foreach (var workout in healthStore.EnumerateDiveWorkoutsAsync(cursor.Since, ct).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();

            var depth = await healthStore.ReadDepthSeriesAsync(workout, ct).ConfigureAwait(false);
            if (depth.Count == 0)
            {
                continue; // A workout with no readable depth series yields no importable dive.
            }

            var temperature = await healthStore.ReadTemperatureSeriesAsync(workout, ct).ConfigureAwait(false);
            foreach (var segment in SegmentOnSurfaceGaps(depth))
            {
                dives.Add(MapSegment(segment, temperature));
            }
        }

        _buffer = dives;

        var refs = new List<DiveRef>(dives.Count);
        for (var i = 0; i < dives.Count; i++)
        {
            var dive = dives[i];
            refs.Add(new DiveRef(
                Descriptor.ConnectorId,
                i.ToString(CultureInfo.InvariantCulture),
                dive.StartUtc,
                dive.DurationSec));
        }

        return refs;
    }

    public Task<ImportedDive> FetchProfileAsync(DiveRef diveRef, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(diveRef);
        var index = int.Parse(diveRef.SourceId, CultureInfo.InvariantCulture);
        return Task.FromResult(_buffer[index]);
    }

    /// <summary>
    /// Splits a time-ordered depth series into per-dive segments wherever consecutive samples are more than
    /// <see cref="SurfaceGapSeconds"/> apart. Defensively re-sorts by time so an out-of-order source can't
    /// mis-segment. Each returned run has at least one sample.
    /// </summary>
    private static IEnumerable<IReadOnlyList<HealthSeriesPoint>> SegmentOnSurfaceGaps(IReadOnlyList<HealthSeriesPoint> depth)
    {
        var ordered = depth.OrderBy(p => p.TimeUtc).ToList();
        var current = new List<HealthSeriesPoint>();
        DateTime? previous = null;

        foreach (var point in ordered)
        {
            if (previous is { } prev && (point.TimeUtc - prev).TotalSeconds > SurfaceGapSeconds && current.Count > 0)
            {
                yield return current;
                current = [];
            }

            current.Add(point);
            previous = point.TimeUtc;
        }

        if (current.Count > 0)
        {
            yield return current;
        }
    }

    /// <summary>
    /// Maps one time-ordered depth segment (+ the workout's temperature series) to an <see cref="ImportedDive"/>.
    /// <c>TSeconds</c> is relative to the segment start; each sample's temperature is the nearest reading
    /// within <see cref="TemperatureMatchToleranceSeconds"/> (else null); <see cref="ImportedDive.MinTempC"/>
    /// is the minimum temperature reading inside the segment window (else null). Gas/cylinders are null —
    /// HealthKit records none.
    /// </summary>
    private static ImportedDive MapSegment(IReadOnlyList<HealthSeriesPoint> depth, IReadOnlyList<HealthSeriesPoint> temperature)
    {
        var start = depth[0].TimeUtc;
        var end = depth[^1].TimeUtc;
        var durationSec = (int)Math.Round((end - start).TotalSeconds, MidpointRounding.AwayFromZero);

        var maxDepthM = depth.Max(p => p.Value);
        var avgDepthM = depth.Average(p => p.Value);

        var windowTemps = temperature.Where(t => t.TimeUtc >= start && t.TimeUtc <= end).ToList();
        double? minTempC = windowTemps.Count > 0 ? windowTemps.Min(t => t.Value) : null;

        var samples = new List<ProfileSampleDto>(depth.Count);
        foreach (var point in depth)
        {
            var tSeconds = (int)Math.Round((point.TimeUtc - start).TotalSeconds, MidpointRounding.AwayFromZero);
            samples.Add(new ProfileSampleDto(
                TSeconds: tSeconds,
                DepthM: point.Value,
                TempC: NearestTemperature(temperature, point.TimeUtc),
                PpO2: null));
            // All rich channels (Ndl, CeilingM, AscentRateMPerMin, Cns, TankPressureBar, Setpoint,
            // HeartRate) default null — HealthKit's dive surface is depth+temp only.
        }

        return new ImportedDive(
            StartUtc: DateTime.SpecifyKind(start, DateTimeKind.Utc),
            DurationSec: durationSec,
            MaxDepthM: maxDepthM,
            AvgDepthM: avgDepthM,
            MinTempC: minTempC,
            GasMix: null,
            Samples: samples);
    }

    /// <summary>Nearest temperature reading to <paramref name="at"/> within the tolerance, else null.</summary>
    private static double? NearestTemperature(IReadOnlyList<HealthSeriesPoint> temperature, DateTime at)
    {
        double? nearest = null;
        var bestDelta = double.MaxValue;
        foreach (var t in temperature)
        {
            var delta = Math.Abs((t.TimeUtc - at).TotalSeconds);
            if (delta < bestDelta)
            {
                bestDelta = delta;
                nearest = t.Value;
            }
        }

        return bestDelta <= TemperatureMatchToleranceSeconds ? nearest : null;
    }
}
