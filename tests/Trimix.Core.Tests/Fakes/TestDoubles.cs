using System.Globalization;
using System.Runtime.CompilerServices;
using Trimix.Contracts;
using Trimix.Contracts.Dtos;
using Trimix.Core.Abstractions;

namespace Trimix.Core.Tests.Fakes;

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

public sealed class FakePermissions : IPermissions
{
    public bool GrantBluetooth { get; set; } = true;

    public int Requests { get; private set; }

    public Task<bool> RequestBluetoothAsync(CancellationToken cancellationToken = default)
    {
        Requests++;
        return Task.FromResult(GrantBluetooth);
    }
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

public sealed class FakeAuthSession : IAuthSession
{
    private string? _lastAccountId;

    public bool IsAuthenticated { get; set; }

    public UserDto? CurrentUser { get; set; }

    public event EventHandler<bool>? AuthStateChanged;

    public event EventHandler? IdentityChanged;

    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task SignInAsync(AuthResponse auth, CancellationToken cancellationToken = default)
    {
        // Mirror AuthSession: raise IdentityChanged only on a genuine account→different-account switch, never
        // on the guest→account upgrade (no prior established account).
        var incomingAccountId = auth.User.Id.ToString();
        var isIdentityChange = _lastAccountId is not null
            && !string.Equals(_lastAccountId, incomingAccountId, StringComparison.Ordinal);
        _lastAccountId = incomingAccountId;
        IsAuthenticated = true;
        CurrentUser = auth.User;
        if (isIdentityChange)
        {
            IdentityChanged?.Invoke(this, EventArgs.Empty);
        }

        AuthStateChanged?.Invoke(this, true);
        return Task.CompletedTask;
    }

    public Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        IsAuthenticated = false;
        CurrentUser = null;
        AuthStateChanged?.Invoke(this, false);
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
}

public sealed class FakeSyncService : ISyncService
{
    public int SyncCalls { get; private set; }

    public Task SyncAsync(CancellationToken cancellationToken = default)
    {
        SyncCalls++;
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

/// <summary>Configurable <see cref="IApiClient"/> recording every call; unconfigured routes throw.</summary>
public sealed class FakeApiClient : IApiClient
{
    public List<ValidatePurchaseRequest> ValidatePurchaseRequests { get; } = [];

    public Func<ValidatePurchaseRequest, ValidatePurchaseResponse>? ValidatePurchaseHandler { get; set; }

    public Func<EntitlementResponse>? EntitlementHandler { get; set; }

    public List<DiveSyncRequest> SyncRequests { get; } = [];

    public Func<DiveSyncRequest, DiveSyncResponse>? SyncHandler { get; set; }

    public Func<SiteSearchResponse>? SitesHandler { get; set; }

    public List<AnalyticsBatchRequest> AnalyticsBatches { get; } = [];

    public Func<AnalyticsBatchRequest, AnalyticsBatchResponse>? AnalyticsHandler { get; set; }

    public Func<Guid, SiteDetailDto>? SiteDetailHandler { get; set; }

    public Func<SiteContributionRequest, SiteContributionResponse>? ContributionHandler { get; set; }

    public Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Not configured.");

    public Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Not configured.");

    public Task<PasskeyRegisterBeginResponse> PasskeyRegisterBeginAsync(PasskeyRegisterBeginRequest request, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Not configured.");

    public Task<AuthResponse> PasskeyRegisterFinishAsync(PasskeyRegisterFinishRequest request, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Not configured.");

    public Task<PasskeyLoginBeginResponse> PasskeyLoginBeginAsync(CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Not configured.");

    public Task<AuthResponse> PasskeyLoginFinishAsync(PasskeyLoginFinishRequest request, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Not configured.");

    public Task<EntitlementResponse> GetMyEntitlementAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult((EntitlementHandler ?? throw new InvalidOperationException("Not configured."))());

    public Task<ValidatePurchaseResponse> ValidatePurchaseAsync(ValidatePurchaseRequest request, CancellationToken cancellationToken = default)
    {
        ValidatePurchaseRequests.Add(request);
        return Task.FromResult((ValidatePurchaseHandler ?? throw new InvalidOperationException("Not configured."))(request));
    }

    public Task<DiveSyncResponse> SyncDivesAsync(DiveSyncRequest request, CancellationToken cancellationToken = default)
    {
        SyncRequests.Add(request);
        return Task.FromResult((SyncHandler ?? throw new InvalidOperationException("Not configured."))(request));
    }

    public Task<SiteSearchResponse> SearchSitesAsync(string? query, double? lat, double? lng, int? limit, CancellationToken cancellationToken = default) =>
        Task.FromResult((SitesHandler ?? throw new InvalidOperationException("Not configured."))());

    public Task<SiteDetailDto> GetSiteAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult((SiteDetailHandler ?? throw new InvalidOperationException("Not configured."))(id));

    public Task<SiteContributionResponse> SubmitContributionAsync(SiteContributionRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult((ContributionHandler ?? throw new InvalidOperationException("Not configured."))(request));

    public Task<ModerationQueueResponse> GetModerationQueueAsync(string status, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Not configured.");

    public Task<ResolveContributionResponse> ResolveContributionAsync(Guid contributionId, ResolveContributionRequest request, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Not configured.");

    public Task<AnalyticsBatchResponse> SendAnalyticsAsync(AnalyticsBatchRequest request, CancellationToken cancellationToken = default)
    {
        AnalyticsBatches.Add(request);
        return Task.FromResult((AnalyticsHandler ?? (r => new AnalyticsBatchResponse(r.Events.Count)))(request));
    }

    /// <summary>Number of times <see cref="DeleteMyDataAsync"/> was invoked (GDPR erasure).</summary>
    public int DeleteMyDataCalls { get; private set; }

    /// <summary>When set, <see cref="DeleteMyDataAsync"/> throws it (offline / server-error erasure taps).</summary>
    public Exception? DeleteMyDataThrows { get; set; }

    public Task<PrivacyDeleteResponse> DeleteMyDataAsync(CancellationToken cancellationToken = default)
    {
        DeleteMyDataCalls++;
        if (DeleteMyDataThrows is { } ex)
        {
            throw ex;
        }

        return Task.FromResult(new PrivacyDeleteResponse(0, 0, 0));
    }
}

/// <summary>
/// A non-BLE <see cref="IDiveSourceConnector"/> (a stand-in for a future HealthKit / file / cloud
/// source) that returns canned <see cref="ImportedDive"/>s straight into the seam — proving
/// <c>ImportService.ImportFromConnectorAsync</c> runs the source-agnostic downstream for any connector.
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
