using Trimix.Core.Abstractions;
using Trimix.Core.Connectors.Cloud;
using Trimix.Core.Connectors.Infra;
using Trimix.Core.Connectors.Push;
using Trimix.Core.Platform;
using Trimix.Core.Tests.Connectors.Cloud;
using Trimix.Core.Tests.Connectors.Export;
using Trimix.Core.Tests.Fakes;
using Trimix.Core.ViewModels;
using Xunit;

namespace Trimix.Core.Tests.ViewModels;

/// <summary>
/// The Settings → "Connected accounts" activation flow. Pins the founder-directed guardrails at the UI seam:
/// the ToS-risk disclosure is shown and consent recorded BEFORE any sign-in surface (with zero network before
/// consent), credentials reach the connector's own <c>ProvideCredentials</c>/<c>SignInAsync</c> and the raw
/// password is never held on the viewmodel nor written to the token store, and Disconnect revokes consent AND
/// severs the exchanged token. Uses the real gated connectors + the real <see cref="PreferencesConnectorConsentStore"/>
/// (the sole disconnect surface) so the assertions are about production behaviour, not a stub.
/// </summary>
public class ConnectedAccountsViewModelTests
{
    private const string GarminTokenKey = "connector:garmin_connect:access_token";
    private const string Password = "hunter2-secret";

    private static void ScriptGarminLogin(RecordedHttpTransport http) => http
        .EnqueueText("<input type=\"hidden\" name=\"_csrf\" value=\"CSRF-TOKEN-123\" />")
        .EnqueueText("<script>var response_url = \"https://connect.garmin.com/modern?ticket=ST-0-abcDEF123\";</script>")
        .EnqueueText("{\"access_token\":\"garmin-access-xyz\",\"token_type\":\"Bearer\"}");

    private static (ConnectedAccountsViewModel Vm, GarminCloudSourceConnector Garmin, RecordedHttpTransport Http,
        InMemorySecureTokenStore Tokens, PreferencesConnectorConsentStore Consent, RecordingDialogs Dialogs) NewGarminSut()
    {
        var http = new RecordedHttpTransport();
        var tokens = new InMemorySecureTokenStore();
        var consent = new PreferencesConnectorConsentStore(new InMemoryPreferences(), tokens);
        var garmin = new GarminCloudSourceConnector(http, tokens, consent, new FakeFitActivityParser());
        var dialogs = new RecordingDialogs();
        var vm = new ConnectedAccountsViewModel(
            [garmin], [], consent, dialogs, new BasicQrEncoder(), new SsiQrPayloadBuilder());
        return (vm, garmin, http, tokens, consent, dialogs);
    }

    [Fact]
    public async Task Connect_declined_records_no_consent_shows_the_disclosure_and_hits_no_network()
    {
        var (vm, _, http, _, consent, dialogs) = NewGarminSut();
        dialogs.ConfirmResult = false; // diver declines the ToS-risk disclosure
        await vm.LoadAsync();
        var row = Assert.Single(vm.Connectors);

        await vm.ConnectAsync(row);

        var confirm = Assert.Single(dialogs.Confirms);
        Assert.Contains("Terms of Service", confirm.Message); // the real disclosure was surfaced
        Assert.False(consent.HasConsent("garmin_connect"));
        Assert.Null(vm.ActiveCredentialRow);
        Assert.False(vm.IsCredentialSheetVisible);
        Assert.False(row.IsConnected);
        Assert.Empty(http.Requests); // guardrail: zero network before consent
    }

    [Fact]
    public async Task Connect_accepted_grants_consent_and_opens_the_credential_sheet_without_network()
    {
        var (vm, _, http, _, consent, dialogs) = NewGarminSut();
        dialogs.ConfirmResult = true;
        await vm.LoadAsync();
        var row = Assert.Single(vm.Connectors);

        await vm.ConnectAsync(row);

        Assert.True(consent.HasConsent("garmin_connect"));
        Assert.Same(row, vm.ActiveCredentialRow);
        Assert.True(vm.IsCredentialSheetVisible);
        Assert.Empty(http.Requests); // still no network until credentials are submitted
    }

    [Fact]
    public async Task Submit_credentials_exchanges_a_token_connects_and_never_persists_the_password()
    {
        var (vm, _, http, tokens, consent, _) = NewGarminSut();
        await consent.GrantAsync("garmin_connect"); // already opted in
        await vm.LoadAsync();
        var row = Assert.Single(vm.Connectors);
        await vm.ConnectAsync(row); // consent present => opens the sheet directly, no confirm, no network
        ScriptGarminLogin(http);

        vm.CredentialUsername = "diver@example.com";
        vm.CredentialPassword = Password;
        await vm.SubmitCredentialsAsync(row);

        Assert.True(row.IsConnected);
        Assert.Null(vm.ActiveCredentialRow);
        Assert.Equal("", vm.CredentialPassword); // never lingers on the VM
        // Only the exchanged token is persisted — never the password.
        Assert.Equal("garmin-access-xyz", tokens.Values[GarminTokenKey]);
        Assert.DoesNotContain(tokens.Values.Values, v => v.Contains(Password, StringComparison.Ordinal));
        // The password reached the wire exactly once (proving ProvideCredentials fed it into the connector).
        Assert.Single(http.Requests, r => r.Body is not null && r.Body.Contains(Uri.EscapeDataString(Password)));
    }

    [Fact]
    public async Task Push_connector_credentials_reach_SignInAsync_and_report_unconfigured_without_persisting()
    {
        var http = new RecordedHttpTransport();
        var tokens = new InMemorySecureTokenStore();
        var consent = new PreferencesConnectorConsentStore(new InMemoryPreferences(), tokens);
        var ssi = new SsiPushDestinationConnector(consent, tokens, http, new SsiPushOptions()); // endpoints null (dark)
        var dialogs = new RecordingDialogs { ConfirmResult = true };
        var vm = new ConnectedAccountsViewModel(
            [], [ssi], consent, dialogs, new BasicQrEncoder(), new SsiQrPayloadBuilder());
        await vm.LoadAsync();
        var row = Assert.Single(vm.Connectors);

        await vm.ConnectAsync(row); // accepts disclosure + grants consent + opens sheet
        Assert.True(consent.HasConsent("ssi_push_personal"));
        vm.CredentialUsername = "diver";
        vm.CredentialPassword = "secret-pass";
        await vm.SubmitCredentialsAsync(row);

        // SignInAsync was reached (consent present) but the endpoint is unconfigured => Unavailable => alert.
        Assert.False(row.IsConnected);
        Assert.Single(dialogs.Alerts);
        Assert.Equal("", vm.CredentialPassword);
        Assert.Empty(tokens.Values); // nothing persisted
        Assert.Empty(http.Requests); // a null AuthUrl short-circuits before any HTTP
    }

    [Fact]
    public async Task Disconnect_revokes_consent_and_severs_the_stored_token()
    {
        var (vm, _, _, tokens, consent, _) = NewGarminSut();
        await consent.GrantAsync("garmin_connect");
        await tokens.SetAsync(GarminTokenKey, "live-token");
        await vm.LoadAsync();
        var row = Assert.Single(vm.Connectors);
        Assert.True(row.IsConnected); // load reflected the persisted consent + token

        await vm.DisconnectAsync(row);

        Assert.False(consent.HasConsent("garmin_connect"));
        Assert.False(row.IsConnected);
        Assert.Empty(tokens.Values); // revoke also deleted the exchanged token
    }

    [Fact]
    public async Task Clean_connector_row_is_unmanaged_and_connecting_it_is_a_no_op()
    {
        var http = new RecordedHttpTransport();
        var tokens = new InMemorySecureTokenStore();
        var consent = new PreferencesConnectorConsentStore(new InMemoryPreferences(), tokens);
        var dialogs = new RecordingDialogs();
        var fileConnector = new FakeSourceConnector("file_import"); // Legal=UserFile, Auth=None
        var vm = new ConnectedAccountsViewModel(
            [fileConnector], [], consent, dialogs, new BasicQrEncoder(), new SsiQrPayloadBuilder());
        await vm.LoadAsync();
        var row = Assert.Single(vm.Connectors);

        Assert.False(row.IsManaged);
        Assert.False(row.IsReverseEng);
        Assert.Equal("Personal file", row.LegalBadge);

        await vm.ConnectAsync(row); // clean connectors have no account flow

        Assert.Null(vm.ActiveCredentialRow);
        Assert.Empty(dialogs.Confirms);
    }

    [Fact]
    public void ShowSsiQr_renders_a_summary_qr_for_a_dive()
    {
        var (vm, _, _, _, _, _) = NewGarminSut();

        vm.ShowSsiQr(DiveFixture.Trimix());

        Assert.True(vm.HasSsiQr);
        Assert.NotNull(vm.SsiQrMatrix);
        Assert.True(vm.SsiQrMatrix!.Size >= 21);
        Assert.NotNull(vm.SsiQrPayload);
        Assert.Contains("dive", vm.SsiQrPayload!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reverse_eng_row_carries_the_personal_export_badge()
    {
        var (vm, _, _, _, _, _) = NewGarminSut();
        await vm.LoadAsync();
        var row = Assert.Single(vm.Connectors);

        Assert.True(row.IsReverseEng);
        Assert.True(row.IsManaged);
        Assert.Equal("Unofficial · personal export", row.LegalBadge);
        Assert.Contains("personal export", row.DisplayName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Recording <see cref="IUserDialogs"/> so tests can assert the disclosure/alert content and control confirms.</summary>
    private sealed class RecordingDialogs : IUserDialogs
    {
        public List<(string Title, string Message)> Confirms { get; } = [];

        public List<(string Title, string Message)> Alerts { get; } = [];

        public List<string> Toasts { get; } = [];

        public bool ConfirmResult { get; set; } = true;

        public Task AlertAsync(string title, string message, string cancel = "OK")
        {
            Alerts.Add((title, message));
            return Task.CompletedTask;
        }

        public Task<bool> ConfirmAsync(string title, string message, string accept = "OK", string cancel = "Cancel")
        {
            Confirms.Add((title, message));
            return Task.FromResult(ConfirmResult);
        }

        public Task ToastAsync(string message)
        {
            Toasts.Add(message);
            return Task.CompletedTask;
        }
    }
}
