using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trimix.Core.Abstractions;
using Trimix.Core.Connectors;
using Trimix.Core.Connectors.Cloud;
using Trimix.Core.Connectors.Push;

namespace Trimix.Core.ViewModels;

/// <summary>
/// The in-app provider sign-in sheet (prototype S6): browser-style chrome (lock + <c>{provider}.com</c>),
/// "This is {Provider}'s own sign-in page. Trimix only asks to read your dive log.", the credential
/// fields, "Sign in &amp; authorize", and the "Read-only access · revoke anytime." footer.
///
/// The founder guardrails are enforced IN ORDER on authorize:
/// <list type="number">
/// <item>the connector's own plain-language ToS-risk disclosure is shown FIRST and consent
/// (<see cref="IConnectorConsentStore.GrantAsync"/>) is recorded only on explicit acceptance —
/// zero network I/O before that (the gated bases enforce it structurally);</item>
/// <item>credentials feed once into <see cref="GatedCloudSourceConnector.ProvideCredentials"/> /
/// <see cref="GatedPushConnectorBase.SignInAsync"/>; the password is cleared from this VM in a
/// <c>finally</c> and only the exchanged token is ever persisted (inside the connector);</item>
/// <item>on the FIRST successful link a sync auto-starts (~900ms) via the shared
/// <see cref="SyncSessionViewModel"/>.</item>
/// </list>
/// Kept in Core (MAUI-free) so the whole authorize flow is unit-tested; the page only binds.
/// </summary>
public partial class ProviderOAuthViewModel : ObservableObject
{
    private readonly Dictionary<string, object> _connectorsById = [];
    private readonly IConnectorConsentStore _consent;
    private readonly SyncSessionViewModel _syncSession;
    private readonly INavigator _navigator;
    private readonly IUserDialogs _dialogs;

    public ProviderOAuthViewModel(
        IEnumerable<IDiveSourceConnector> sources,
        IEnumerable<IDiveDestinationConnector> destinations,
        IConnectorConsentStore consent,
        SyncSessionViewModel syncSession,
        INavigator navigator,
        IUserDialogs dialogs)
    {
        _consent = consent;
        _syncSession = syncSession;
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
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProviderName))]
    [NotifyPropertyChangedFor(nameof(Domain))]
    [NotifyPropertyChangedFor(nameof(Monogram))]
    [NotifyPropertyChangedFor(nameof(SignInHeading))]
    [NotifyPropertyChangedFor(nameof(OwnPageCaption))]
    private string _connectorId = "";

    public string ProviderName => ProviderCatalog.NameFor(ConnectorId);

    public string Domain => ProviderCatalog.DomainFor(ConnectorId);

    public string Monogram => ProviderCatalog.MonogramFor(ConnectorId);

    public string SignInHeading => $"Sign in to {ProviderName}";

    public string OwnPageCaption =>
        $"This is {ProviderName}'s own sign-in page. Trimix only asks to read your dive log.";

    [ObservableProperty]
    private string _username = "";

    [ObservableProperty]
    private string _password = "";

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>The first-link auto-sync task — fire-and-forget in production, awaitable by tests.</summary>
    public Task? AutoSyncTask { get; private set; }

    /// <summary>Dismiss without authorizing ("Done" in the browser chrome).</summary>
    [RelayCommand]
    private Task Close() => _navigator.GoBackAsync();

    /// <summary>"Sign in &amp; authorize" — disclosure → consent → credential exchange → first-link auto-sync.</summary>
    [RelayCommand]
    private async Task SignInAndAuthorizeAsync()
    {
        if (IsBusy || !_connectorsById.TryGetValue(ConnectorId, out var connector))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        {
            await _dialogs.AlertAsync("Sign-in needed", "Enter your username and password to continue.");
            return;
        }

        IsBusy = true;
        try
        {
            // Captured BEFORE consent is granted: linking state is consent-derived, so reading it
            // after GrantAsync would always report "not the first link".
            var wasFirstLink = !_syncSession.HasLinkedSources;

            // Guardrail order: the connector's own ToS-risk disclosure comes BEFORE consent, and
            // consent comes before any network I/O. With no consent recorded, AuthenticateAsync is a
            // zero-I/O local call that returns NeedsInteraction + the disclosure text.
            if (!_consent.HasConsent(ConnectorId))
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

                await _consent.GrantAsync(ConnectorId);
            }

            ConnectorAuthResult result;
            try
            {
                result = connector switch
                {
                    GatedCloudSourceConnector cloud => await SignInCloudAsync(cloud, Username, Password),
                    GatedPushConnectorBase push => await push.SignInAsync(Username, Password),
                    _ => new ConnectorAuthResult(ConnectorAuthStatus.Unavailable, "This provider does not support sign-in."),
                };
            }
            finally
            {
                // Guardrail: the raw password never lingers on the viewmodel (nor is it ever persisted).
                Password = "";
            }

            if (result.Status != ConnectorAuthStatus.Granted)
            {
                await _dialogs.AlertAsync(
                    "Couldn't connect",
                    result.Message ?? "Sign-in failed. Check your details and try again.");
                return;
            }

            Username = "";
            _syncSession.RefreshLinkState();
            // No toast (the design removed them): the state change is the provider row flipping to
            // "Connected" plus the auto-started first sync's streaming sheet.
            await _navigator.GoBackAsync();

            if (wasFirstLink && _syncSession.HasLinkedSources)
            {
                // Prototype: the first link auto-starts a sync ~900ms later. Owned by the singleton
                // session so it survives this transient VM; the task handle is kept for tests.
                AutoSyncTask = _syncSession.AutoSyncAfterFirstLinkAsync();
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static async Task<ConnectorAuthResult> SignInCloudAsync(
        GatedCloudSourceConnector cloud, string username, string password)
    {
        cloud.ProvideCredentials(username, password);
        return await cloud.AuthenticateAsync();
    }

    /// <summary>AuthenticateAsync that swallows connector faults so the disclosure fetch never crashes the sheet.</summary>
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
