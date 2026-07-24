using Trimix.Core.Abstractions;

namespace Trimix.Core.Connectors.HealthKit;

/// <summary>
/// A HealthKit <c>underwaterDiving</c> workout envelope — a stable id plus the workout's UTC start/end.
/// It bounds a dive session, but a single workout's depth series can still contain more than one dive
/// separated by a surface interval, so <see cref="HealthStoreSourceConnector"/> re-segments each workout's
/// depth series on gaps &gt; 60 s (the AWU2UDDF pattern) rather than trusting one-workout-per-dive.
/// </summary>
public sealed record HealthDiveWorkout(string Id, DateTime StartUtc, DateTime EndUtc);

/// <summary>
/// One timestamped scalar from a HealthKit quantity series. Depth points carry metres; temperature points
/// carry degrees Celsius. <paramref name="TimeUtc"/> is always UTC (the real store converts CoreFoundation
/// dates to UTC; fakes construct UTC directly).
/// </summary>
public sealed record HealthSeriesPoint(DateTime TimeUtc, double Value);

/// <summary>
/// Hexagonal PORT (architecture overview §2, "put a PORT interface in Core") over Apple HealthKit's only
/// dive surface: <c>HKQuantityTypeIdentifier.underwaterDepth</c> + <c>waterTemperature</c> series written
/// by Apple-Watch-Ultra / Oceanic+ / the Apple Depth app, bounded by <c>HKWorkoutActivityType.underwaterDiving</c>
/// workouts. Depth+temp only — HealthKit carries no gas, tank, deco, or heart channel for a dive, so every
/// other <see cref="Trimix.Contracts.Dtos.ProfileSampleDto"/> channel stays null (<see cref="ProfileFidelity.DepthTempOnly"/>).
///
/// The real implementation is iOS-only (<c>App/Platforms/iOS/Health/HealthKitHealthStore.cs</c>, behind
/// <c>#if IOS</c>); tests drive a synthetic in-memory fake. Android has no equivalent — Health Connect
/// exposes no dive-profile data type — so this port has no Android impl and the connector is
/// <see cref="Platforms.iOS"/> only.
/// </summary>
public interface IHealthStore
{
    /// <summary>
    /// Requests on-device read consent for the underwater-depth + water-temperature quantity types and the
    /// workout type. Maps HealthKit's authorization outcome to a <see cref="ConnectorAuthResult"/>:
    /// <see cref="ConnectorAuthStatus.Granted"/> when the prompt completed (HealthKit never reveals whether
    /// the user granted read access — an empty result set is indistinguishable from a denial),
    /// <see cref="ConnectorAuthStatus.Unavailable"/> when HealthKit is absent (iPad / no health data).
    /// </summary>
    Task<ConnectorAuthResult> RequestAuthorizationAsync(CancellationToken ct = default);

    /// <summary>
    /// Enumerates <c>underwaterDiving</c> workouts newer than <paramref name="since"/> (null = all),
    /// oldest-first. Each workout is later resolved to one-or-more dives by reading + segmenting its series.
    /// </summary>
    IAsyncEnumerable<HealthDiveWorkout> EnumerateDiveWorkoutsAsync(DateTime? since, CancellationToken ct = default);

    /// <summary>Reads the underwater-depth series (metres) recorded within <paramref name="workout"/>'s window.</summary>
    Task<IReadOnlyList<HealthSeriesPoint>> ReadDepthSeriesAsync(HealthDiveWorkout workout, CancellationToken ct = default);

    /// <summary>Reads the water-temperature series (°C) recorded within <paramref name="workout"/>'s window; may be empty.</summary>
    Task<IReadOnlyList<HealthSeriesPoint>> ReadTemperatureSeriesAsync(HealthDiveWorkout workout, CancellationToken ct = default);
}
