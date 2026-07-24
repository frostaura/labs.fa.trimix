using Trimix.Core.Abstractions;
using Trimix.Core.Connectors.HealthKit;
using Trimix.Core.Tests.Fakes;

namespace Trimix.Core.Tests.Connectors.HealthKit;

/// <summary>
/// A deterministic, in-memory <see cref="IHealthStore"/> for driving <see cref="HealthStoreSourceConnector"/>
/// with no HealthKit binding: it hands back canned workouts and canned depth/temperature series keyed by
/// workout id. Records what was asked (auth calls, the <c>since</c> filter) so tests can assert the connector
/// forwards the sync cursor and calls authorize.
/// </summary>
public sealed class FakeHealthStore : IHealthStore
{
    private readonly Dictionary<string, IReadOnlyList<HealthSeriesPoint>> _depth = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<HealthSeriesPoint>> _temperature = new(StringComparer.Ordinal);
    private readonly List<HealthDiveWorkout> _workouts = [];

    public ConnectorAuthStatus AuthStatus { get; set; } = ConnectorAuthStatus.Granted;

    public int AuthorizeCalls { get; private set; }

    public DateTime? LastSince { get; private set; }

    public bool SinceRequested { get; private set; }

    /// <summary>Adds a workout with its depth series (required) and optional temperature series.</summary>
    public FakeHealthStore AddWorkout(
        HealthDiveWorkout workout,
        IReadOnlyList<HealthSeriesPoint> depth,
        IReadOnlyList<HealthSeriesPoint>? temperature = null)
    {
        _workouts.Add(workout);
        _depth[workout.Id] = depth;
        _temperature[workout.Id] = temperature ?? [];
        return this;
    }

    public Task<ConnectorAuthResult> RequestAuthorizationAsync(CancellationToken ct = default)
    {
        AuthorizeCalls++;
        return Task.FromResult(new ConnectorAuthResult(AuthStatus));
    }

    public IAsyncEnumerable<HealthDiveWorkout> EnumerateDiveWorkoutsAsync(DateTime? since, CancellationToken ct = default)
    {
        SinceRequested = true;
        LastSince = since;
        var matched = since is { } s
            ? _workouts.Where(w => w.StartUtc >= s)
            : _workouts;
        return AsyncStream.From(matched.OrderBy(w => w.StartUtc), ct);
    }

    public Task<IReadOnlyList<HealthSeriesPoint>> ReadDepthSeriesAsync(HealthDiveWorkout workout, CancellationToken ct = default) =>
        Task.FromResult(_depth.TryGetValue(workout.Id, out var series) ? series : []);

    public Task<IReadOnlyList<HealthSeriesPoint>> ReadTemperatureSeriesAsync(HealthDiveWorkout workout, CancellationToken ct = default) =>
        Task.FromResult(_temperature.TryGetValue(workout.Id, out var series) ? series : []);
}
