using System.Globalization;
using System.Runtime.CompilerServices;
using Trimix.Contracts;
using Trimix.Contracts.Dtos;
using Trimix.Core.Abstractions;
using Trimix.Core.Connectors;
using Trimix.Core.Stats;

namespace Trimix.Core.Tests.Fakes;

/// <summary>
/// <see cref="IProviderPriority"/> double mirroring <see cref="ProviderPriorityService"/>. Constructed with
/// no order it is NEUTRAL — every rank is 0, so the composite delegate reduces to
/// <see cref="DiveMergePolicy.FidelityScore"/> and existing fidelity-based assertions are unaffected. Seed an
/// order to exercise priority-driven merges/badges.
/// </summary>
public sealed class FakeProviderPriority : IProviderPriority
{
    private List<string> _order;

    public FakeProviderPriority(params string[] order) => _order = order.ToList();

    public IReadOnlyList<string> Order() => _order;

    public int RankOf(string? sourceVendor) => Rank(_order, sourceVendor);

    public Func<DiveDto, int> FidelityDelegate()
    {
        var order = _order.ToList();
        return dive => Rank(order, dive.SourceVendor) * 1000 + DiveMergePolicy.FidelityScore(dive);
    }

    public void Set(IReadOnlyList<string> order) => _order = order.ToList();

    public void Move(string connectorId, int newIndex)
    {
        var from = _order.IndexOf(connectorId);
        if (from < 0)
        {
            return;
        }

        _order.RemoveAt(from);
        _order.Insert(Math.Clamp(newIndex, 0, _order.Count), connectorId);
    }

    private static int Rank(List<string> order, string? vendor)
    {
        if (string.IsNullOrEmpty(vendor))
        {
            return 0;
        }

        var index = order.IndexOf(vendor);
        return index < 0 ? 0 : order.Count - index;
    }
}

public sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 7, 3, 12, 0, 0, TimeSpan.Zero);
}

public sealed class FakeAppInfo : IAppInfo
{
    public string Platform { get; set; } = "android";

    public string AppVersion { get; set; } = "0.1.0-test";
}

public sealed class FakeConnectivity : IConnectivityService
{
    public bool IsOnline { get; set; } = true;

    public event EventHandler<bool>? ConnectivityChanged;

    public void RaiseChanged() => ConnectivityChanged?.Invoke(this, IsOnline);
}

public sealed class FakeLocation : ILocationService
{
    public GeoLocation? Fix { get; set; }

    public Task<GeoLocation?> GetCurrentLocationAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Fix);
}

public sealed class FakeNavigator : INavigator
{
    public List<(string Route, IReadOnlyDictionary<string, object>? Parameters)> Navigations { get; } = [];

    public int BackCount { get; private set; }

    public Task GoToAsync(string route, IReadOnlyDictionary<string, object>? parameters = null)
    {
        Navigations.Add((route, parameters));
        return Task.CompletedTask;
    }

    public Task GoBackAsync()
    {
        BackCount++;
        return Task.CompletedTask;
    }
}

public sealed class FakeDialogs : IUserDialogs
{
    public List<(string Title, string Message)> Alerts { get; } = [];

    public List<string> Toasts { get; } = [];

    public bool ConfirmResult { get; set; } = true;

    public Task AlertAsync(string title, string message, string cancel = "OK")
    {
        Alerts.Add((title, message));
        return Task.CompletedTask;
    }

    public Task<bool> ConfirmAsync(string title, string message, string accept = "OK", string cancel = "Cancel") =>
        Task.FromResult(ConfirmResult);

    public Task ToastAsync(string message)
    {
        Toasts.Add(message);
        return Task.CompletedTask;
    }
}

/// <summary>Spy <see cref="IConnectorPurgeService"/>: counts PurgeAllAsync calls (optionally throws).</summary>
public sealed class RecordingConnectorPurgeService : Trimix.Core.Privacy.IConnectorPurgeService
{
    public int PurgeAllCalls { get; private set; }

    /// <summary>When set, <see cref="PurgeAllAsync"/> throws it (models a failing purge).</summary>
    public Exception? Throws { get; set; }

    public Task PurgeAllAsync(CancellationToken cancellationToken = default)
    {
        PurgeAllCalls++;
        return Throws is { } ex ? Task.FromException(ex) : Task.CompletedTask;
    }
}

public sealed class FakeEntitlementService : IEntitlementService
{
    public EntitlementTier CurrentTier { get; set; } = EntitlementTier.Free;

    public int RefreshCalls { get; private set; }

    public int RestoreCalls { get; private set; }

    public int RestoreResult { get; set; }

    public event EventHandler<EntitlementTier>? TierChanged;

    public void RaiseTierChanged(EntitlementTier tier)
    {
        CurrentTier = tier;
        TierChanged?.Invoke(this, tier);
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<EntitlementTier> RefreshAsync(CancellationToken cancellationToken = default)
    {
        RefreshCalls++;
        return Task.FromResult(CurrentTier);
    }

    public Task OnSignedInAsync(CancellationToken cancellationToken = default) => RefreshAsync(cancellationToken);

    public Task<int> RestoreAsync(CancellationToken cancellationToken = default)
    {
        RestoreCalls++;
        if (RestoreThrows is { } ex)
        {
            throw ex;
        }

        return Task.FromResult(RestoreResult);
    }

    /// <summary>When set, <see cref="RestoreAsync"/> throws it (offline / foreign-receipt restore taps).</summary>
    public Exception? RestoreThrows { get; set; }

    public int ReconcilePendingCalls { get; private set; }

    public Task ReconcilePendingAsync(CancellationToken cancellationToken = default)
    {
        ReconcilePendingCalls++;
        return Task.CompletedTask;
    }

    public int ClearLocalGrantCalls { get; private set; }

    public Task ClearLocalGrantAsync(CancellationToken cancellationToken = default)
    {
        ClearLocalGrantCalls++;
        if (CurrentTier != EntitlementTier.Free)
        {
            CurrentTier = EntitlementTier.Free;
            TierChanged?.Invoke(this, EntitlementTier.Free);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Records tracked events in order AND validates them against the frozen contract — a VM emitting
/// an unknown event or missing a required prop fails the test at the Track call site.
/// </summary>
public sealed class RecordingAnalytics : IAnalytics
{
    public List<(string Name, IReadOnlyDictionary<string, string> Props)> Events { get; } = [];

    public int FlushCalls { get; private set; }

    public string UserPseudoId => "test-pseudo-id";

    public void Track(string name, IReadOnlyDictionary<string, string>? props = null)
    {
        if (!FunnelEvents.RequiredProps.TryGetValue(name, out var required))
        {
            throw new InvalidOperationException($"Unknown funnel event '{name}' tracked in test.");
        }

        foreach (var key in required)
        {
            if (props is null || !props.ContainsKey(key))
            {
                throw new InvalidOperationException($"Event '{name}' missing required prop '{key}' in test.");
            }
        }

        Events.Add((name, props ?? new Dictionary<string, string>()));
    }

    public IReadOnlyList<string> Names => [.. Events.Select(e => e.Name)];

    public Task EnsureInstallTrackedAsync(string source, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task EnsureFirstRunCompletedTrackedAsync(CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        FlushCalls++;
        return Task.CompletedTask;
    }
}

/// <summary>
/// A generic <see cref="IDiveSourceConnector"/> that returns canned <see cref="ImportedDive"/>s straight
/// into the seam — proving <c>ImportService.ImportFromConnectorAsync</c> runs the source-agnostic
/// downstream for any connector.
/// </summary>
public sealed class FakeSourceConnector : IDiveSourceConnector
{
    private readonly IReadOnlyList<ImportedDive> _dives;

    public FakeSourceConnector(string connectorId = "fake_source", IReadOnlyList<ImportedDive>? dives = null)
    {
        Descriptor = new ConnectorDescriptor(
            connectorId,
            ConnectorKind.FileImport,
            LegalPosture.UserFile,
            ProfileFidelity.FullProfile,
            AuthModel.None,
            Platforms.Both,
            EnabledByDefault: true);
        _dives = dives ?? DefaultDives();
    }

    public ConnectorDescriptor Descriptor { get; }

    public ConnectorAuthStatus AuthStatus { get; set; } = ConnectorAuthStatus.Granted;

    public int AuthenticateCalls { get; private set; }

    /// <summary>Network-I/O probes: assert these stay 0 when auth is not Granted (guardrail (iii)).</summary>
    public int ListDivesCalls { get; private set; }

    public int FetchProfileCalls { get; private set; }

    public Task<ConnectorAuthResult> AuthenticateAsync(CancellationToken ct = default)
    {
        AuthenticateCalls++;
        return Task.FromResult(new ConnectorAuthResult(AuthStatus));
    }

    public Task<IReadOnlyList<DiveRef>> ListDivesSinceAsync(SyncCursor cursor, CancellationToken ct = default)
    {
        ListDivesCalls++;
        IReadOnlyList<DiveRef> refs = [.. _dives.Select((d, i) => new DiveRef(
            Descriptor.ConnectorId, i.ToString(CultureInfo.InvariantCulture), d.StartUtc, d.DurationSec))];
        return Task.FromResult(refs);
    }

    public Task<ImportedDive> FetchProfileAsync(DiveRef diveRef, CancellationToken ct = default)
    {
        FetchProfileCalls++;
        return Task.FromResult(_dives[int.Parse(diveRef.SourceId, CultureInfo.InvariantCulture)]);
    }

    private static IReadOnlyList<ImportedDive> DefaultDives() =>
    [
        new ImportedDive(
            new DateTime(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc), 2400, 28.5, 16.0, 21.0, "EAN32",
            [new ProfileSampleDto(0, 0, null, null), new ProfileSampleDto(60, 28.5, 21.0, null)]),
        new ImportedDive(
            new DateTime(2026, 6, 2, 9, 30, 0, DateTimeKind.Utc), 1800, 18.0, 12.0, 22.0, "Air",
            [new ProfileSampleDto(0, 0, null, null), new ProfileSampleDto(60, 18.0, 22.0, null)]),
    ];
}

/// <summary>Scripted <see cref="ISyncCoordinator"/> for the sync-session/UI seam tests.</summary>
public sealed class FakeSyncCoordinator : ISyncCoordinator
{
    /// <summary>Static link state; overridden by <see cref="HasLinkedSourcesFunc"/> when set (e.g. to
    /// derive it from a consent store so a granted consent flips the state mid-test).</summary>
    public bool HasLinkedSourcesValue { get; set; }

    public Func<bool>? HasLinkedSourcesFunc { get; set; }

    public bool HasLinkedSources => HasLinkedSourcesFunc?.Invoke() ?? HasLinkedSourcesValue;

    public int SyncCalls { get; private set; }

    /// <summary>Report returned by the next run (defaults to an empty successful run).</summary>
    public SyncReport NextReport { get; set; } = new([], [], [], CapReached: false);

    /// <summary>Progress ticks replayed to the caller before the report returns.</summary>
    public List<SyncProgress> TicksToEmit { get; } = [];

    public Task<SyncReport> SyncNowAsync(
        Func<Task>? onCapReached = null,
        IProgress<SyncProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        SyncCalls++;
        foreach (var tick in TicksToEmit)
        {
            progress?.Report(tick);
        }

        return Task.FromResult(NextReport);
    }
}

public static class AsyncStream
{
    public static async IAsyncEnumerable<T> From<T>(
        IEnumerable<T> items,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return item;
        }
    }
}
