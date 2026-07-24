#if IOS
using Foundation;
using HealthKit;
using Microsoft.Extensions.Logging;
using Trimix.Core.Abstractions;
using Trimix.Core.Connectors.HealthKit;

namespace Trimix.App.Health;

/// <summary>
/// iOS <see cref="IHealthStore"/> implementation (architecture overview §2), the HealthKit twin of the
/// CoreBluetooth <c>BleTransport</c> / StoreKit <c>StoreKitBillingService</c> adapters: it lives in the
/// iOS platform head, is guarded by <c>#if IOS</c>, and is registered as the real port only on iOS. The
/// Core <see cref="HealthStoreSourceConnector"/> depends on the port, never on these HealthKit bindings, so
/// the hexagonal boundary holds (net10.0 Core never sees <c>HealthKit</c>).
///
/// It reads the only clean dive surface HealthKit exposes: <c>HKQuantityTypeIdentifier.UnderwaterDepth</c>
/// (metres) + <c>WaterTemperature</c> (°C) series written by Apple-Watch-Ultra / Oceanic+ / the Apple Depth
/// app, bounded by <c>HKWorkoutActivityType.UnderwaterDiving</c> workouts. Depth+temp only — no gas/tank/deco.
///
/// STORE / BUILD NOTES for the integrator (iOS build is the release gate, not this phase):
///   • Info.plist: add <c>NSHealthShareUsageDescription</c> (required user-facing read reason).
///   • Entitlements (device build): add <c>com.apple.developer.healthkit</c> (and, if you narrow types,
///     the HealthKit access entitlement). Deployment target iOS 16+ (underwater types are iOS 16 / watchOS 9).
///   • The iOS Simulator has no dive data; validation needs a real device + an Apple-Watch-Ultra dive.
///
/// SPIKE FLAGS (confirm exact .NET-for-iOS binding names when the iOS build is enabled — coded to the
/// closest known surface here):
///   • <c>HKQuantitySeriesSampleQuery</c> and its per-datum delegate parameter order/type
///     (<c>HKQuantitySeriesSampleQueryQuantityDelegate</c>) — the series read is the spike-critical binding.
///   • <c>HKQuery.GetPredicateForWorkouts(HKWorkoutActivityType)</c> vs a differently-named overload.
///   • <c>HKUnit</c> factory: used <c>HKUnit.FromString("m")</c> / <c>"degC"</c> for stability over static
///     properties whose names have drifted across bindings.
/// </summary>
public sealed class HealthKitHealthStore(ILogger<HealthKitHealthStore> logger) : IHealthStore
{
    private static readonly HKQuantityType DepthType =
        HKQuantityType.Create(HKQuantityTypeIdentifier.UnderwaterDepth)!;

    private static readonly HKQuantityType TemperatureType =
        HKQuantityType.Create(HKQuantityTypeIdentifier.WaterTemperature)!;

    private static readonly HKUnit MetreUnit = HKUnit.FromString("m");
    private static readonly HKUnit CelsiusUnit = HKUnit.FromString("degC");

    private readonly HKHealthStore _store = new();

    public async Task<ConnectorAuthResult> RequestAuthorizationAsync(CancellationToken ct = default)
    {
        if (!HKHealthStore.IsHealthDataAvailable)
        {
            return new ConnectorAuthResult(
                ConnectorAuthStatus.Unavailable,
                "Apple Health is not available on this device.");
        }

        var readTypes = new NSSet<HKObjectType>(DepthType, TemperatureType, HKObjectType.GetWorkoutType());

        var completion = new TaskCompletionSource<(bool Success, NSError? Error)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = ct.Register(() => completion.TrySetCanceled(ct));

        // We only READ; typesToShare is null. HealthKit deliberately never tells us whether the user granted
        // read access (an empty result is indistinguishable from a denial), so a completed prompt is treated
        // as Granted and a truly empty logbook simply imports nothing.
        _store.RequestAuthorization(null, readTypes, (success, error) => completion.TrySetResult((success, error)));

        var (ok, err) = await completion.Task.ConfigureAwait(false);
        if (!ok)
        {
            logger.LogWarning("HealthKit authorization request failed: {Error}", err?.LocalizedDescription);
            return new ConnectorAuthResult(ConnectorAuthStatus.Denied, err?.LocalizedDescription);
        }

        return new ConnectorAuthResult(ConnectorAuthStatus.Granted);
    }

    public async IAsyncEnumerable<HealthDiveWorkout> EnumerateDiveWorkoutsAsync(
        DateTime? since,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var workoutPredicate = HKQuery.GetPredicateForWorkouts(HKWorkoutActivityType.UnderwaterDiving);
        NSPredicate predicate = since is { } s
            ? NSCompoundPredicate.CreateAndPredicate(
                [workoutPredicate, HKQuery.GetPredicateForSamples((NSDate)DateTime.SpecifyKind(s, DateTimeKind.Utc), null, HKQueryOptions.None)])
            : workoutPredicate;

        var sort = new[] { new NSSortDescriptor(HKSample.SortIdentifierStartDate, ascending: true) };

        var completion = new TaskCompletionSource<HKSample[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = ct.Register(() => completion.TrySetCanceled(ct));

        var query = new HKSampleQuery(
            HKObjectType.GetWorkoutType(),
            predicate,
            HKSampleQuery.NoLimit,
            sort,
            (_, results, error) =>
            {
                if (error is not null)
                {
                    completion.TrySetException(new InvalidOperationException(error.LocalizedDescription));
                    return;
                }

                completion.TrySetResult(results ?? []);
            });

        _store.ExecuteQuery(query);

        var samples = await completion.Task.ConfigureAwait(false);
        foreach (var sample in samples)
        {
            ct.ThrowIfCancellationRequested();
            if (sample is HKWorkout workout)
            {
                yield return new HealthDiveWorkout(
                    Id: workout.Uuid.AsString(),
                    StartUtc: ToUtc(workout.StartDate),
                    EndUtc: ToUtc(workout.EndDate));
            }
        }
    }

    public Task<IReadOnlyList<HealthSeriesPoint>> ReadDepthSeriesAsync(HealthDiveWorkout workout, CancellationToken ct = default) =>
        ReadSeriesAsync(DepthType, MetreUnit, workout, ct);

    public Task<IReadOnlyList<HealthSeriesPoint>> ReadTemperatureSeriesAsync(HealthDiveWorkout workout, CancellationToken ct = default) =>
        ReadSeriesAsync(TemperatureType, CelsiusUnit, workout, ct);

    /// <summary>
    /// Runs an <c>HKQuantitySeriesSampleQuery</c> bounded to the workout's window and materialises each
    /// series datum as a <see cref="HealthSeriesPoint"/>. The Apple Watch Ultra writes ~2–3 s cadence, so
    /// the series carries a genuine depth/time profile. SPIKE: the delegate parameter order below is the
    /// item most likely to need adjusting against the installed Microsoft.iOS binding.
    /// </summary>
    private async Task<IReadOnlyList<HealthSeriesPoint>> ReadSeriesAsync(
        HKQuantityType quantityType,
        HKUnit unit,
        HealthDiveWorkout workout,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(workout);

        var predicate = HKQuery.GetPredicateForSamples(
            (NSDate)DateTime.SpecifyKind(workout.StartUtc, DateTimeKind.Utc),
            (NSDate)DateTime.SpecifyKind(workout.EndUtc, DateTimeKind.Utc),
            HKQueryOptions.None);

        var points = new List<HealthSeriesPoint>();
        var completion = new TaskCompletionSource<IReadOnlyList<HealthSeriesPoint>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = ct.Register(() => completion.TrySetCanceled(ct));

        var query = new HKQuantitySeriesSampleQuery(
            quantityType,
            predicate,
            (_, quantity, dateInterval, _, done, error) =>
            {
                if (error is not null)
                {
                    completion.TrySetException(new InvalidOperationException(error.LocalizedDescription));
                    return;
                }

                if (quantity is not null && dateInterval is not null)
                {
                    points.Add(new HealthSeriesPoint(
                        TimeUtc: ToUtc(dateInterval.StartDate),
                        Value: quantity.GetDoubleValue(unit)));
                }

                if (done)
                {
                    completion.TrySetResult(points);
                }
            });

        _store.ExecuteQuery(query);
        return await completion.Task.ConfigureAwait(false);
    }

    /// <summary>Converts a CoreFoundation date to a UTC <see cref="DateTime"/> (the port contract is UTC).</summary>
    private static DateTime ToUtc(NSDate date) => DateTime.SpecifyKind((DateTime)date, DateTimeKind.Utc);
}
#endif
