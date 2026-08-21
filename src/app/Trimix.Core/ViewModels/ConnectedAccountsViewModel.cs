using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trimix.Core.Abstractions;
using Trimix.Core.Connectors;

namespace Trimix.Core.ViewModels;

/// <summary>
/// The "Providers" screen (prototype S5): one row per registered connector — the five cloud sources
/// (Garmin Dive · Suunto · ScubaPro · Mares · Oceanic+) then the two cert-agency destinations
/// (PADI · SSI) — with the states Not connected → Linked ✓ / Syncing…. "Your phone syncs each
/// platform directly — links stay on-device."
///
/// Connect pushes the in-app provider sign-in sheet (<see cref="ProviderOAuthViewModel"/>), which
/// owns the disclosure → consent → credential-exchange guardrail chain. Tapping a linked row offers
/// unlink ("revoke anytime"): <see cref="IConnectorConsentStore.RevokeAsync"/> also severs the
/// exchanged token, so a reconnect forces a fresh sign-in. The header hosts the radar "Sync now"
/// button (via the shared <see cref="SyncSessionViewModel"/>); arriving with the nudge parameter
/// (Sync now tapped while unlinked) flashes the Connect buttons red.
/// </summary>
public partial class ConnectedAccountsViewModel : ObservableObject
{
    /// <summary>Shell route (kept on the VM for call-site compatibility; value = <see cref="Routes.Providers"/>).</summary>
    public const string Route = Routes.Providers;

    /// <summary>Nudge flash duration (prototype ~1.15s). Tests set 0 to assert synchronously.</summary>
    public int NudgeDurationMs { get; set; } = 1150;

    private readonly Dictionary<string, object> _connectorsById = [];
    private readonly IConnectorConsentStore _consent;
    private readonly INavigator _navigator;
    private readonly IUserDialogs _dialogs;
    private readonly ILocalStore _store;
    private readonly IProviderPriority _priority;
    private readonly IPreferences _preferences;

    public ConnectedAccountsViewModel(
        IEnumerable<IDiveSourceConnector> sources,
        IEnumerable<IDiveDestinationConnector> destinations,
        IConnectorConsentStore consent,
        SyncSessionViewModel syncSession,
        INavigator navigator,
        IUserDialogs dialogs,
        ILocalStore store,
        IProviderPriority priority,
        IPreferences preferences)
    {
        _consent = consent;
        Sync = syncSession;
        _navigator = navigator;
        _dialogs = dialogs;
        _store = store;
        _priority = priority;
        _preferences = preferences;

        foreach (var source in sources)
        {
            _connectorsById[source.Descriptor.ConnectorId] = source;
        }

        foreach (var destination in destinations)
        {
            _connectorsById[destination.Descriptor.ConnectorId] = destination;
        }

        // Row statuses flip to "Syncing…" while the shared session runs.
        Sync.PropertyChanged += OnSyncPropertyChanged;
    }

    /// <summary>The shared sync session — radar button, last-sync label, syncing states.</summary>
    public SyncSessionViewModel Sync { get; }

    /// <summary>
    /// Linked providers, sources first in the user's priority order (the order that decides overlapping-dive
    /// merges) then any linked destinations. This is the DRAGGABLE group — reordering it persists priority.
    /// </summary>
    public ObservableCollection<ProviderRow> ConnectedProviders { get; } = [];

    /// <summary>Unlinked providers — the "connect me" group. Not sortable (priority is meaningless until a
    /// provider is linked and has dives).</summary>
    public ObservableCollection<ProviderRow> DisconnectedProviders { get; } = [];

    /// <summary>Section-visibility flags so an empty group hides its header.</summary>
    [ObservableProperty]
    private bool _hasConnectedProviders;

    [ObservableProperty]
    private bool _hasDisconnectedProviders;

    /// <summary>Bounded height for the connected (draggable) list — a reorderable CollectionView must not
    /// sit inside a ScrollView (the drag would fight it), so it is sized to its content and scrolls
    /// internally past a handful of rows.</summary>
    [ObservableProperty]
    private double _connectedListHeight;

    private const double RowHeightEstimate = 88;

    /// <summary>True briefly after arriving via an unlinked "Sync now" — flashes the Connect buttons.</summary>
    [ObservableProperty]
    private bool _isNudging;

    /// <summary>
    /// True when the "drag to reorder — the top computer wins overlapping dives" hint should show — only with
    /// ≥2 connected source providers (so priority is meaningful) and only until the diver has seen it once.
    /// </summary>
    [ObservableProperty]
    private bool _isReorderHintVisible;

    /// <summary>Rebuilds the provider rows from persisted consent + token state (zero network I/O), split into
    /// the Connected (draggable, priority-ordered sources first) and Not-connected groups.</summary>
    [RelayCommand]
    public async Task LoadAsync()
    {
        var rows = new List<ProviderRow>();
        foreach (var entry in _connectorsById.Values)
        {
            var descriptor = DescriptorOf(entry);
            var row = new ProviderRow(descriptor.ConnectorId, entry is IDiveSourceConnector);

            // Linked = implemented/live AND consented AND a stored token answers Granted. A "Coming soon"
            // provider can never be linked, and short-circuiting here also skips its AuthenticateAsync (its
            // endpoints are placeholders). For the live gated connectors this reads only local state —
            // AuthenticateAsync performs no network call without pending credentials (and none before consent).
            row.IsLinked = row.IsAvailable
                && _consent.HasConsent(descriptor.ConnectorId)
                && (await AuthenticateSafeAsync(entry)).Status == ConnectorAuthStatus.Granted;
            row.RefreshStatus(Sync.IsSyncing);
            rows.Add(row);
        }

        // Connected sources render in the user's priority order (a new/unordered one sorts last); linked
        // destinations follow. Disconnected rows keep registry order (sources then destinations).
        var order = _priority.Order().ToList();
        var connected = rows
            .Where(r => r.IsLinked)
            .OrderByDescending(r => r.IsSource) // sources before destinations
            .ThenBy(r => order.IndexOf(r.ConnectorId) is var i && i >= 0 ? i : int.MaxValue)
            .ToList();
        var disconnected = rows.Where(r => !r.IsLinked).ToList();

        Replace(ConnectedProviders, connected);
        Replace(DisconnectedProviders, disconnected);
        HasConnectedProviders = connected.Count > 0;
        HasDisconnectedProviders = disconnected.Count > 0;
        // Size the draggable list to content, capped so it never eats the whole screen (it scrolls past ~6).
        ConnectedListHeight = Math.Min(connected.Count, 6) * RowHeightEstimate;

        IsReorderHintVisible = connected.Count(r => r.IsSource) >= 2
            && _preferences.Get(PreferenceKeys.ProviderReorderHintSeen) is null;

        Sync.RefreshLinkState();
        Sync.RefreshLastSyncLabel();
    }

    private static void Replace(ObservableCollection<ProviderRow> target, List<ProviderRow> rows)
    {
        target.Clear();
        foreach (var row in rows)
        {
            target.Add(row);
        }
    }

    /// <summary>
    /// Persist the current <see cref="ConnectedProviders"/> order as the merge priority. Called after the
    /// draggable list finishes a reorder (the CollectionView has already moved the item in place). Only
    /// source providers carry dives, so only their order is stored.
    /// </summary>
    public void PersistConnectedOrder() =>
        _priority.Set(ConnectedProviders.Where(r => r.IsSource).Select(r => r.ConnectorId).ToList());

    /// <summary>Dismisses the reorder hint and remembers it (one-shot).</summary>
    [RelayCommand]
    public void DismissReorderHint()
    {
        _preferences.Set(PreferenceKeys.ProviderReorderHintSeen, "1");
        IsReorderHintVisible = false;
    }

    /// <summary>
    /// Row CTA: unlinked → push the provider's in-app sign-in sheet; linked → offer unlink
    /// ("Read-only access · revoke anytime" is a promise this fulfils).
    /// </summary>
    [RelayCommand]
    public async Task ConnectAsync(ProviderRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        // "Coming soon" providers are inert — the row shows a pill, not a Connect button, but guard the
        // command too so nothing can link a provider that isn't implemented yet.
        if (!row.IsAvailable)
        {
            return;
        }

        if (!row.IsLinked)
        {
            await _navigator.GoToAsync(Routes.ProviderOAuth, new Dictionary<string, object>
            {
                [Routes.Params.ConnectorId] = row.ConnectorId,
            });
            return;
        }

        // Sources import dives; unlinking one removes that provider's dives from this device. Push
        // destinations (PADI/SSI) carry no dives, so their unlink copy stays about the link + token only.
        var message = row.IsSource
            ? $"Trimix will forget the link, delete the stored access token, and remove {row.PlatformName}'s "
                + "dives from this device. Dives another linked provider also recorded stay in your logbook."
            : "Trimix will forget the link and delete the stored access token.";
        var unlink = await _dialogs.ConfirmAsync(
            $"Unlink {row.PlatformName}?",
            message,
            accept: "Unlink",
            cancel: "Cancel");
        if (!unlink)
        {
            return;
        }

        // Revoke also severs the exchanged token, so a reconnect re-authenticates from scratch.
        // No toast (the design has none): the row itself animating back to "Not connected" /
        // "Connect" IS the confirmation.
        await _consent.RevokeAsync(row.ConnectorId);

        // Remove this provider's imported dives (source connectors only). SourceVendor == connector id, so a
        // dive another still-linked provider also recorded survives as that provider's own row. The logbook
        // and "You" stats recompute from the store on their next load, so totals fall accordingly.
        if (row.IsSource)
        {
            await _store.DeleteDivesByVendorAsync(row.ConnectorId);
        }

        row.IsLinked = false;
        row.RefreshStatus(Sync.IsSyncing);
        Sync.RefreshLinkState();
    }

    /// <summary>Flashes the Connect buttons (arrival via an unlinked "Sync now" tap).</summary>
    public async Task BeginNudgeAsync()
    {
        IsNudging = true;
        if (NudgeDurationMs > 0)
        {
            await Task.Delay(NudgeDurationMs);
        }

        IsNudging = false;
    }

    private void OnSyncPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SyncSessionViewModel.IsSyncing))
        {
            return;
        }

        foreach (var row in ConnectedProviders)
        {
            row.RefreshStatus(Sync.IsSyncing);
        }

        foreach (var row in DisconnectedProviders)
        {
            row.RefreshStatus(Sync.IsSyncing);
        }
    }

    private static ConnectorDescriptor DescriptorOf(object connector) => connector switch
    {
        IDiveSourceConnector source => source.Descriptor,
        IDiveDestinationConnector destination => destination.Descriptor,
        _ => throw new ArgumentException("Unknown connector type.", nameof(connector)),
    };

    /// <summary>AuthenticateAsync that swallows connector faults so a status refresh never crashes the page.</summary>
    private static async Task<ConnectorAuthResult> AuthenticateSafeAsync(object connector)
    {
        try
        {
            return connector switch
            {
                IDiveSourceConnector source => await source.AuthenticateAsync(),
                IDiveDestinationConnector destination => await destination.AuthenticateAsync(),
                _ => new ConnectorAuthResult(ConnectorAuthStatus.Unavailable),
            };
        }
        catch (Exception)
        {
            return new ConnectorAuthResult(ConnectorAuthStatus.Unavailable);
        }
    }
}

/// <summary>
/// One provider row (prototype S5): platform name, monogram "logo", link state, status line and CTA.
/// <see cref="IsSource"/> distinguishes the pull providers from the PADI/SSI push destinations.
/// </summary>
public partial class ProviderRow(string connectorId, bool isSource) : ObservableObject
{
    public string ConnectorId { get; } = connectorId;

    public bool IsSource { get; } = isSource;

    /// <summary>
    /// True when this provider is implemented + verified live and can actually be connected today
    /// (see <see cref="ProviderCatalog.IsAvailable"/>). False ⇒ the row renders disabled with a
    /// "Coming soon" pill and cannot be linked.
    /// </summary>
    public bool IsAvailable { get; } = ProviderCatalog.IsAvailable(connectorId);

    /// <summary>Inverse of <see cref="IsAvailable"/> — drives the "Coming soon" pill visibility.</summary>
    public bool IsComingSoon => !IsAvailable;

    /// <summary>Row dimming: full for a live provider, faded for a "Coming soon" one.</summary>
    public double RowOpacity => IsAvailable ? 1d : 0.55d;

    /// <summary>Short platform name ("Garmin Dive", "PADI").</summary>
    public string PlatformName { get; } = ProviderCatalog.NameFor(connectorId);

    /// <summary>Single-letter monogram for the row's logo box.</summary>
    public string Monogram { get; } = ProviderCatalog.MonogramFor(connectorId);

    [ObservableProperty]
    private bool _isLinked;

    [ObservableProperty]
    private string _statusText = "Not connected";

    [ObservableProperty]
    private string _ctaText = "Connect";

    /// <summary>Recomputes the status/CTA strings (prototype: Not connected → Linked ✓ / Syncing…).
    /// A not-yet-implemented provider is pinned to the "Coming soon" state regardless of link/sync.</summary>
    public void RefreshStatus(bool isSyncing)
    {
        if (!IsAvailable)
        {
            StatusText = "Coming soon";
            CtaText = "Coming soon";
            return;
        }

        StatusText = IsLinked ? (isSyncing ? "Syncing…" : "Linked · synced") : "Not connected";
        CtaText = IsLinked ? "Linked ✓" : "Connect";
    }
}
