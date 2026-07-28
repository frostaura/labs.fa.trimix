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

    public ConnectedAccountsViewModel(
        IEnumerable<IDiveSourceConnector> sources,
        IEnumerable<IDiveDestinationConnector> destinations,
        IConnectorConsentStore consent,
        SyncSessionViewModel syncSession,
        INavigator navigator,
        IUserDialogs dialogs)
    {
        _consent = consent;
        Sync = syncSession;
        _navigator = navigator;
        _dialogs = dialogs;

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

    /// <summary>All provider rows, sources first then destinations, in stable registry order.</summary>
    public ObservableCollection<ProviderRow> Providers { get; } = [];

    /// <summary>True briefly after arriving via an unlinked "Sync now" — flashes the Connect buttons.</summary>
    [ObservableProperty]
    private bool _isNudging;

    /// <summary>Rebuilds the provider rows from persisted consent + token state (zero network I/O).</summary>
    [RelayCommand]
    public async Task LoadAsync()
    {
        Providers.Clear();
        foreach (var entry in _connectorsById.Values)
        {
            var descriptor = DescriptorOf(entry);
            var row = new ProviderRow(descriptor.ConnectorId, entry is IDiveSourceConnector);

            // Linked = consented AND a stored token answers Granted. For the gated connectors this
            // reads only local state — AuthenticateAsync performs no network call without pending
            // credentials (and none at all before consent).
            row.IsLinked = _consent.HasConsent(descriptor.ConnectorId)
                && (await AuthenticateSafeAsync(entry)).Status == ConnectorAuthStatus.Granted;
            row.RefreshStatus(Sync.IsSyncing);
            Providers.Add(row);
        }

        Sync.RefreshLinkState();
        Sync.RefreshLastSyncLabel();
    }

    /// <summary>
    /// Row CTA: unlinked → push the provider's in-app sign-in sheet; linked → offer unlink
    /// ("Read-only access · revoke anytime" is a promise this fulfils).
    /// </summary>
    [RelayCommand]
    public async Task ConnectAsync(ProviderRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (!row.IsLinked)
        {
            await _navigator.GoToAsync(Routes.ProviderOAuth, new Dictionary<string, object>
            {
                [Routes.Params.ConnectorId] = row.ConnectorId,
            });
            return;
        }

        var unlink = await _dialogs.ConfirmAsync(
            $"Unlink {row.PlatformName}?",
            "Trimix will forget the link and delete the stored access token. Your dives stay in your logbook.",
            accept: "Unlink",
            cancel: "Cancel");
        if (!unlink)
        {
            return;
        }

        // Revoke also severs the exchanged token, so a reconnect re-authenticates from scratch.
        await _consent.RevokeAsync(row.ConnectorId);
        row.IsLinked = false;
        row.RefreshStatus(Sync.IsSyncing);
        Sync.RefreshLinkState();
        await _dialogs.ToastAsync($"{row.PlatformName} unlinked.");
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

        foreach (var row in Providers)
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

    /// <summary>Recomputes the status/CTA strings (prototype: Not connected → Linked ✓ / Syncing…).</summary>
    public void RefreshStatus(bool isSyncing)
    {
        StatusText = IsLinked ? (isSyncing ? "Syncing…" : "Linked · synced") : "Not connected";
        CtaText = IsLinked ? "Linked ✓" : "Connect";
    }
}
