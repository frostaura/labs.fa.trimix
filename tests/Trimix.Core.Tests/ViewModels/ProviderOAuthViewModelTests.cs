using Trimix.Core.Connectors.Cloud;
using Trimix.Core.Connectors.Infra;
using Trimix.Core.Connectors.Push;
using Trimix.Core.Platform;
using Trimix.Core.Tests.Connectors.Cloud;
using Trimix.Core.Tests.Fakes;
using Trimix.Core.ViewModels;
using Xunit;

namespace Trimix.Core.Tests.ViewModels;

/// <summary>
/// The in-app provider sign-in sheet (prototype S6) — the guardrail chain at the UI seam, in order:
/// the connector's own ToS-risk disclosure is shown and consent recorded BEFORE any sign-in (zero
/// network before consent), credentials feed once into the connector and the raw password is never
/// held on the viewmodel nor written to the token store, and the FIRST successful link auto-starts
/// a sync via the shared session. Uses the real gated connectors + the real consent store so the
/// assertions are about production behaviour, not a stub.
/// </summary>
public class ProviderOAuthViewModelTests
{
    private const string GarminTokenKey = "connector:garmin_connect:access_token";
    private const string Password = "hunter2-secret";

    private readonly RecordedHttpTransport _http = new();
    private readonly InMemorySecureTokenStore _tokens = new();
    private readonly PreferencesConnectorConsentStore _consent;
    private readonly FakeNavigator _navigator = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly FakeSyncCoordinator _coordinator = new();

    public ProviderOAuthViewModelTests()
    {
        _consent = new PreferencesConnectorConsentStore(new InMemoryPreferences(), _tokens);
        // Link state derives from consent so a granted consent flips HasLinkedSources mid-test,
        // exactly as the real SyncCoordinator computes it.
        _coordinator.HasLinkedSourcesFunc = () => _consent.HasConsent("garmin_connect");
    }

    private SyncSessionViewModel NewSession() => new(
        _coordinator, [], _consent, _navigator, new InMemoryPreferences(), new FakeClock())
    {
        AutoSyncDelayMs = 0,
        DoneLingerMs = 0,
    };

    private ProviderOAuthViewModel NewGarminVm()
    {
        var garmin = new GarminCloudSourceConnector(_http, _tokens, _consent);
        return new ProviderOAuthViewModel([garmin], [], _consent, NewSession(), _navigator, _dialogs)
        {
            ConnectorId = "garmin_connect",
        };
    }

    // Observed Garmin Dive flow: JSON login returning a service ticket, then the ticket exchange.
    // See docs/reverse-engineering/garmin-dive-api.md.
    private static void ScriptGarminLogin(RecordedHttpTransport http) => http
        .EnqueueText("{\"serviceTicketId\":\"ST-0383050-abc-sso\",\"responseStatus\":{\"type\":\"SUCCESSFUL\"},\"customerMfaInfo\":null}")
        .EnqueueText("{\"access_token\":\"garmin-access-xyz\",\"token_type\":\"bearer\",\"refresh_token\":\"garmin-refresh-xyz\",\"expires_in\":86399}");

    [Fact]
    public void Chrome_renders_the_provider_identity()
    {
        var vm = NewGarminVm();

        Assert.Equal("Garmin Dive", vm.ProviderName);
        Assert.Equal("garmin.com", vm.Domain);
        Assert.Equal("Sign in to Garmin Dive", vm.SignInHeading);
        // The sheet chrome names the app doing the asking, not a website.
        Assert.Contains("TRIMIX", vm.SheetTitle, StringComparison.Ordinal);
        Assert.Contains("Garmin Dive", vm.SheetTitle, StringComparison.Ordinal);
    }

    [Fact]
    public void The_sign_in_caption_never_claims_to_be_the_vendors_own_page()
    {
        // This page used to caption itself "This is {Provider}'s own sign-in page" while capturing the diver's
        // provider password into Trimix's own process. That is a misrepresentation of who receives the
        // credentials — prohibited by both app stores, and the sort of claim that reads as bad faith later.
        // Pinning it here means the impersonation cannot quietly return.
        var vm = NewGarminVm();

        Assert.DoesNotContain("own sign-in page", vm.SignInCaption, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Trimix signs in", vm.SignInCaption, StringComparison.Ordinal);

        // And it must keep making the three promises the implementation actually keeps.
        Assert.Contains("read-only", vm.SignInCaption, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("never saved", vm.SignInCaption, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("disconnect", vm.SignInCaption, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Declining_the_disclosure_records_no_consent_and_hits_no_network()
    {
        var vm = NewGarminVm();
        _dialogs.ConfirmResult = false; // diver declines the ToS-risk disclosure
        vm.Username = "diver@example.com";
        vm.Password = Password;

        await vm.SignInAndAuthorizeCommand.ExecuteAsync(null);

        Assert.False(_consent.HasConsent("garmin_connect"));
        Assert.Empty(_http.Requests); // guardrail: zero network before consent
        Assert.Equal(0, _navigator.BackCount); // stays on the sheet
        Assert.Equal(0, _coordinator.SyncCalls);
    }

    [Fact]
    public async Task Authorize_shows_the_real_disclosure_before_consent()
    {
        var recorder = new RecordingConfirmDialogs { ConfirmResult = false };
        var garmin = new GarminCloudSourceConnector(_http, _tokens, _consent);
        var vm2 = new ProviderOAuthViewModel([garmin], [], _consent, NewSession(), _navigator, recorder)
        {
            ConnectorId = "garmin_connect",
            Username = "diver@example.com",
            Password = Password,
        };
        await vm2.SignInAndAuthorizeCommand.ExecuteAsync(null);

        var confirm = Assert.Single(recorder.Confirms);
        Assert.Contains("Terms of Service", confirm.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Accepting_authorizes_stores_only_the_token_and_auto_starts_the_first_sync()
    {
        var vm = NewGarminVm();
        _dialogs.ConfirmResult = true;
        ScriptGarminLogin(_http);
        vm.Username = "diver@example.com";
        vm.Password = Password;

        await vm.SignInAndAuthorizeCommand.ExecuteAsync(null);

        Assert.True(_consent.HasConsent("garmin_connect"));
        // Only the exchanged token is persisted — never the password; the VM's copy is cleared.
        // Garmin stores a token envelope (access + 30-day refresh + expiry), not a bare access token.
        Assert.Contains("garmin-access-xyz", _tokens.Values[GarminTokenKey], StringComparison.Ordinal);
        Assert.DoesNotContain(_tokens.Values.Values, v => v.Contains(Password, StringComparison.Ordinal));
        Assert.Equal("", vm.Password);
        // The password reached the wire exactly once (ProvideCredentials fed it into the connector).
        Assert.Single(_http.Requests, r => r.Body is not null && r.Body.Contains(Uri.EscapeDataString(Password)));
        // Sheet dismissed; NO toast (the design removed them — the provider row's inline status
        // and the auto-started sync sheet are the state change).
        Assert.Equal(1, _navigator.BackCount);
        Assert.Empty(_dialogs.Toasts);

        // A successful link → the session auto-starts a sync (prototype ~900ms; 0 in tests).
        Assert.NotNull(vm.AutoSyncTask);
        await vm.AutoSyncTask!;
        Assert.Equal(1, _coordinator.SyncCalls);
    }

    [Fact]
    public async Task Second_link_also_auto_starts_a_sync()
    {
        // A diver who links a SECOND provider expects to see its dives too — the auto-sync must fire for
        // every link, not just the first (founder-reported: adding ScubaPro after Garmin synced nothing).
        await _consent.GrantAsync("garmin_connect"); // a provider is already linked
        var vm = NewGarminVm();
        _dialogs.ConfirmResult = true;
        ScriptGarminLogin(_http);
        vm.Username = "diver@example.com";
        vm.Password = Password;

        await vm.SignInAndAuthorizeCommand.ExecuteAsync(null);

        Assert.NotNull(vm.AutoSyncTask);
        await vm.AutoSyncTask!;
        Assert.Equal(1, _coordinator.SyncCalls);
    }

    [Fact]
    public async Task Push_connector_credentials_reach_SignInAsync_and_unconfigured_reports_without_persisting()
    {
        var ssi = new SsiPushDestinationConnector(_consent, _tokens, _http, new SsiPushOptions()); // endpoints null (dark)
        var vm = new ProviderOAuthViewModel([], [ssi], _consent, NewSession(), _navigator, _dialogs)
        {
            ConnectorId = "ssi_push_personal",
            Username = "diver",
            Password = "secret-pass",
        };
        _dialogs.ConfirmResult = true;

        await vm.SignInAndAuthorizeCommand.ExecuteAsync(null);

        Assert.True(_consent.HasConsent("ssi_push_personal")); // disclosure accepted, consent recorded
        Assert.Single(_dialogs.Alerts); // Unavailable => "Couldn't connect" alert
        Assert.Equal("", vm.Password);
        Assert.Empty(_tokens.Values); // nothing persisted
        Assert.Empty(_http.Requests); // a null AuthUrl short-circuits before any HTTP
        Assert.Equal(0, _navigator.BackCount); // stays for a retry
    }

    [Fact]
    public async Task Missing_credentials_prompt_instead_of_consenting()
    {
        var vm = NewGarminVm();

        await vm.SignInAndAuthorizeCommand.ExecuteAsync(null);

        Assert.Single(_dialogs.Alerts);
        Assert.False(_consent.HasConsent("garmin_connect"));
        Assert.Empty(_http.Requests);
    }

    /// <summary>FakeDialogs variant that records confirm calls (the disclosure-content assertion).</summary>
    private sealed class RecordingConfirmDialogs : Trimix.Core.Abstractions.IUserDialogs
    {
        public List<(string Title, string Message)> Confirms { get; } = [];

        public bool ConfirmResult { get; set; } = true;

        public Task AlertAsync(string title, string message, string cancel = "OK") => Task.CompletedTask;

        public Task<bool> ConfirmAsync(string title, string message, string accept = "OK", string cancel = "Cancel")
        {
            Confirms.Add((title, message));
            return Task.FromResult(ConfirmResult);
        }

        public Task ToastAsync(string message) => Task.CompletedTask;
    }
}
