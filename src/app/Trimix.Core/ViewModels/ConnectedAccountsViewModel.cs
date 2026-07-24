using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trimix.Contracts.Dtos;
using Trimix.Core.Abstractions;
using Trimix.Core.Connectors.Cloud;
using Trimix.Core.Connectors.Push;

namespace Trimix.Core.ViewModels;

/// <summary>
/// Settings → "Connected accounts": a registry-driven surface over every self-registered connector
/// (<see cref="IDiveSourceConnector"/> + <see cref="IDiveDestinationConnector"/>). It renders each connector's
/// name, kind and legal-posture badge, and for the reverse-engineered "personal export" connectors
/// (<see cref="LegalPosture.ToSRiskReverseEng"/>) drives the founder-directed activation flow:
///
/// <list type="number">
///   <item><b>Connect</b> shows the connector's own plain-language ToS-risk disclosure and only records
///     consent (<see cref="IConnectorConsentStore.GrantAsync"/>) after the diver explicitly accepts — no
///     network I/O happens before that (the gated bases enforce it).</item>
///   <item>A credential sheet then feeds the username+password once into
///     <see cref="GatedCloudSourceConnector.ProvideCredentials"/> / <see cref="GatedPushConnectorBase.SignInAsync"/>;
///     the password is cleared immediately and <b>never</b> held on the viewmodel or persisted (only the
///     exchanged token is stored, inside the connector).</item>
///   <item><b>Disconnect</b> is <see cref="IConnectorConsentStore.RevokeAsync"/>, which also severs the stored
///     token, so a reconnect forces a fresh sign-in.</item>
/// </list>
///
/// Clean connectors (dive computer / Apple Health / file import / file export) need no account and are shown as
/// read-only informational rows. The SSI card additionally renders the zero-network, diver-scanned MySSI QR
/// export (<see cref="SsiQrPayloadBuilder"/> → <see cref="IQrEncoder"/>). Kept in Core (MAUI-free) so the whole
/// activation flow is unit-tested; the page only binds.
/// </summary>
public partial class ConnectedAccountsViewModel : ObservableObject
{
    public const string Route = "connected-accounts";

    private readonly IConnectorConsentStore _consent;
    private readonly IUserDialogs _dialogs;
    private readonly IQrEncoder _qrEncoder;
    private readonly SsiQrPayloadBuilder _ssiQrPayloadBuilder;
    private readonly Dictionary<string, object> _connectorsById = [];

    public ConnectedAccountsViewModel(
        IEnumerable<IDiveSourceConnector> sources,
        IEnumerable<IDiveDestinationConnector> destinations,
        IConnectorConsentStore consent,
        IUserDialogs dialogs,
        IQrEncoder qrEncoder,
        SsiQrPayloadBuilder ssiQrPayloadBuilder)
    {
        _consent = consent;
        _dialogs = dialogs;
        _qrEncoder = qrEncoder;
        _ssiQrPayloadBuilder = ssiQrPayloadBuilder;

        foreach (var source in sources)
        {
            _connectorsById[source.Descriptor.ConnectorId] = source;
        }

        foreach (var destination in destinations)
        {
            _connectorsById[destination.Descriptor.ConnectorId] = destination;
        }
    }

    /// <summary>All connector rows, sources then destinations, in a stable registry order.</summary>
    public ObservableCollection<ConnectorRow> Connectors { get; } = [];

    /// <summary>The row whose credential sheet is open, or null when no sheet is shown.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCredentialSheetVisible))]
    private ConnectorRow? _activeCredentialRow;

    [ObservableProperty]
    private string _credentialUsername = "";

    [ObservableProperty]
    private string _credentialPassword = "";

    public bool IsCredentialSheetVisible => ActiveCredentialRow is not null;

    /// <summary>The rendered MySSI export QR, or null until <see cref="ShowSsiQrCommand"/> runs with a dive.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSsiQr))]
    private QrMatrix? _ssiQrMatrix;

    [ObservableProperty]
    private string? _ssiQrPayload;

    public bool HasSsiQr => SsiQrMatrix is not null;

    /// <summary>Rebuilds the connector rows and refreshes each managed connector's connected state.</summary>
    [RelayCommand]
    public async Task LoadAsync()
    {
        Connectors.Clear();
        ActiveCredentialRow = null;

        // Sources first, then destinations — the dictionary already de-duplicates by id.
        foreach (var entry in _connectorsById.Values)
        {
            var descriptor = DescriptorOf(entry);
            var row = new ConnectorRow(descriptor, DisplayNameOf(entry, descriptor));
            if (row.IsManaged)
            {
                // Managed (reverse-eng) connectors: reflect any persisted consent + token. AuthenticateAsync
                // never touches the network here — with no pending credentials it only reads local state.
                row.IsConnected = _consent.HasConsent(descriptor.ConnectorId)
                    && (await AuthenticateSafeAsync(entry)).Status == ConnectorAuthStatus.Granted;
                row.StatusMessage = row.IsConnected ? "Connected" : "Not connected";
            }

            Connectors.Add(row);
        }
    }

    /// <summary>Connect: disclosure → consent → reveal the credential sheet (managed connectors only).</summary>
    [RelayCommand]
    public async Task ConnectAsync(ConnectorRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (!row.IsManaged || !_connectorsById.TryGetValue(row.ConnectorId, out var connector))
        {
            return;
        }

        // Show the connector's own ToS-risk disclosure and gain explicit consent before ANY sign-in surface.
        if (!_consent.HasConsent(row.ConnectorId))
        {
            var disclosure = (await AuthenticateSafeAsync(connector)).Message
                ?? "This is an unofficial personal-export connector and may breach the vendor's terms of service.";
            var accepted = await _dialogs.ConfirmAsync(
                "Personal export — please read",
                disclosure,
                accept: "I understand, continue",
                cancel: "Cancel");
            if (!accepted)
            {
                return;
            }

            await _consent.GrantAsync(row.ConnectorId);
        }

        // Reveal the credential sheet (never pre-filled).
        CredentialUsername = "";
        CredentialPassword = "";
        ActiveCredentialRow = row;
    }

    /// <summary>
    /// Submit the sign-in sheet: feed the credentials once into the connector and store ONLY the exchanged
    /// token. The password is cleared from the viewmodel immediately, whatever the outcome.
    /// </summary>
    [RelayCommand]
    public async Task SubmitCredentialsAsync(ConnectorRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (!_connectorsById.TryGetValue(row.ConnectorId, out var connector))
        {
            return;
        }

        var username = CredentialUsername;
        var password = CredentialPassword;
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            await _dialogs.AlertAsync("Sign-in needed", "Enter your username and password to connect.");
            return;
        }

        ConnectorAuthResult result;
        try
        {
            result = connector switch
            {
                GatedCloudSourceConnector cloud => await SignInCloudAsync(cloud, username, password),
                GatedPushConnectorBase push => await push.SignInAsync(username, password),
                _ => new ConnectorAuthResult(ConnectorAuthStatus.Unavailable, "This connector does not support sign-in."),
            };
        }
        finally
        {
            // Guardrail: the raw password never lingers on the viewmodel (nor is it ever persisted).
            CredentialPassword = "";
        }

        if (result.Status == ConnectorAuthStatus.Granted)
        {
            row.IsConnected = true;
            row.StatusMessage = "Connected";
            CredentialUsername = "";
            ActiveCredentialRow = null;
            await _dialogs.ToastAsync($"Connected {row.DisplayName}.");
        }
        else
        {
            row.IsConnected = false;
            row.StatusMessage = "Not connected";
            await _dialogs.AlertAsync(
                "Couldn't connect",
                result.Message ?? "Sign-in failed. Check your details and try again.");
            // Leave the sheet open (username retained, password cleared) so the diver can retry.
        }
    }

    /// <summary>Dismiss the credential sheet without connecting, clearing any entered secret.</summary>
    [RelayCommand]
    public void CancelCredentials()
    {
        CredentialUsername = "";
        CredentialPassword = "";
        ActiveCredentialRow = null;
    }

    /// <summary>Disconnect: revoke consent (which also deletes the exchanged token) so reconnect re-authenticates.</summary>
    [RelayCommand]
    public async Task DisconnectAsync(ConnectorRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        await _consent.RevokeAsync(row.ConnectorId);
        row.IsConnected = false;
        row.StatusMessage = "Not connected";
        if (ReferenceEquals(ActiveCredentialRow, row))
        {
            ActiveCredentialRow = null;
        }

        await _dialogs.ToastAsync($"Disconnected {row.DisplayName}.");
    }

    /// <summary>
    /// Render the diver-scanned MySSI QR for a specific dive (summary-only, contacts no SSI server, handles no
    /// credentials). Exposed as a command so a dive-scoped caller can navigate here with a dive to export.
    /// </summary>
    [RelayCommand]
    public void ShowSsiQr(DiveDto dive)
    {
        ArgumentNullException.ThrowIfNull(dive);
        var payload = _ssiQrPayloadBuilder.Build(dive);
        SsiQrPayload = payload;
        SsiQrMatrix = _qrEncoder.Encode(payload);
    }

    private static async Task<ConnectorAuthResult> SignInCloudAsync(GatedCloudSourceConnector cloud, string username, string password)
    {
        cloud.ProvideCredentials(username, password);
        return await cloud.AuthenticateAsync();
    }

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

    private static ConnectorDescriptor DescriptorOf(object connector) => connector switch
    {
        IDiveSourceConnector source => source.Descriptor,
        IDiveDestinationConnector destination => destination.Descriptor,
        _ => throw new ArgumentException("Unknown connector type.", nameof(connector)),
    };

    private static string DisplayNameOf(object connector, ConnectorDescriptor descriptor) => connector switch
    {
        // Cloud connectors carry a truthful "personal export" display name.
        GatedCloudSourceConnector cloud => cloud.DisplayName,
        _ => descriptor.ConnectorId switch
        {
            "ble_device" => "Dive computer (Bluetooth)",
            "apple_health" => "Apple Health",
            "file_import" => "File import (UDDF / DL7 / FIT)",
            "padi_push_personal" => "PADI (personal export)",
            "ssi_push_personal" => "SSI · MySSI (personal export)",
            "export_uddf" => "UDDF file export",
            "export_dl7" => "DL7 file export",
            "export_pdf" => "PDF file export",
            var id => id.Replace('_', ' '),
        },
    };
}

/// <summary>
/// A single connector's display row (aggregator registry). Immutable identity + display fields, with the
/// connected state observable so the credential flow can update the UI. <see cref="IsManaged"/> is true only
/// for the reverse-engineered "personal export" connectors that have a connect/disconnect account flow.
/// </summary>
public partial class ConnectorRow : ObservableObject
{
    public ConnectorRow(ConnectorDescriptor descriptor, string displayName)
    {
        ConnectorId = descriptor.ConnectorId;
        DisplayName = displayName;
        Kind = descriptor.Kind;
        Legal = descriptor.Legal;
        LegalBadge = BadgeFor(descriptor.Legal);
        IsReverseEng = descriptor.Legal == LegalPosture.ToSRiskReverseEng;
        IsManaged = descriptor.Legal == LegalPosture.ToSRiskReverseEng
            && descriptor.Auth == AuthModel.UserCredential;
        FidelityCaption = CaptionFor(descriptor.Fidelity);
        StatusMessage = IsManaged ? "Not connected" : "Available";
    }

    public string ConnectorId { get; }

    public string DisplayName { get; }

    public ConnectorKind Kind { get; }

    public LegalPosture Legal { get; }

    /// <summary>Short legal-posture badge label; the ToS-risk one is clearly flagged "personal export".</summary>
    public string LegalBadge { get; }

    /// <summary>True for the unofficial reverse-engineered connectors (shows the ToS-risk disclosure on connect).</summary>
    public bool IsReverseEng { get; }

    /// <summary>True when the row has an interactive connect/disconnect account flow (credential connectors).</summary>
    public bool IsManaged { get; }

    public string FidelityCaption { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConnect))]
    [NotifyPropertyChangedFor(nameof(CanDisconnect))]
    private bool _isConnected;

    [ObservableProperty]
    private string _statusMessage = "";

    /// <summary>The Connect action is offered only for a managed connector that is not yet connected.</summary>
    public bool CanConnect => IsManaged && !IsConnected;

    /// <summary>The Disconnect action is offered only for a managed connector that is currently connected.</summary>
    public bool CanDisconnect => IsManaged && IsConnected;

    private static string BadgeFor(LegalPosture posture) => posture switch
    {
        LegalPosture.OwnedHardware => "Your device",
        LegalPosture.CleanOfficial => "Official",
        LegalPosture.UserFile => "Personal file",
        LegalPosture.ToSRiskReverseEng => "Unofficial · personal export",
        LegalPosture.PartnerRequired => "Partner",
        _ => "",
    };

    private static string CaptionFor(ProfileFidelity fidelity) => fidelity switch
    {
        ProfileFidelity.FullProfile => "Full profile (depth, gas, tank, deco readouts)",
        ProfileFidelity.DepthTempOnly => "Depth and temperature only",
        ProfileFidelity.SummaryOnly => "Summary only (date, duration, max depth)",
        _ => "",
    };
}
