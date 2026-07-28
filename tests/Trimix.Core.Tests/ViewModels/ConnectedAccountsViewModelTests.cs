using Trimix.Core.Abstractions;
using Trimix.Core.Connectors.Cloud;
using Trimix.Core.Connectors.Push;
using Trimix.Core.Tests.Connectors.Cloud;
using Trimix.Core.Tests.Fakes;
using Trimix.Core.ViewModels;
using Xunit;

namespace Trimix.Core.Tests.ViewModels;

/// <summary>
/// The Providers screen (prototype S5): rows for every registered connector with the
/// Not connected → Linked ✓ / Syncing… states, Connect pushing the in-app sign-in sheet, and
/// unlink revoking consent AND severing the exchanged token. Uses the real gated connectors + the
/// real consent-store semantics so the assertions are about production behaviour, not a stub.
/// </summary>
public class ConnectedAccountsViewModelTests
{
    private const string GarminTokenKey = "connector:garmin_connect:access_token";

    private readonly RecordedHttpTransport _http = new();
    private readonly InMemorySecureTokenStore _tokens = new();
    private readonly Trimix.Core.Connectors.Infra.PreferencesConnectorConsentStore _consent;
    private readonly FakeNavigator _navigator = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly FakeSyncCoordinator _coordinator = new();

    public ConnectedAccountsViewModelTests() =>
        _consent = new Trimix.Core.Connectors.Infra.PreferencesConnectorConsentStore(
            new Trimix.Core.Platform.InMemoryPreferences(), _tokens);

    private SyncSessionViewModel NewSession() => new(
        _coordinator, [], _consent, _navigator, _dialogs,
        new Trimix.Core.Platform.InMemoryPreferences(), new FakeClock())
    {
        AutoSyncDelayMs = 0,
        DoneLingerMs = 0,
    };

    private ConnectedAccountsViewModel NewVm(
        IEnumerable<IDiveSourceConnector>? sources = null,
        IEnumerable<IDiveDestinationConnector>? destinations = null)
    {
        var garmin = new GarminCloudSourceConnector(_http, _tokens, _consent, new FakeFitActivityParser());
        var ssi = new SsiPushDestinationConnector(_consent, _tokens, _http, new SsiPushOptions());
        return new ConnectedAccountsViewModel(
            sources ?? [garmin], destinations ?? [ssi], _consent, NewSession(), _navigator, _dialogs)
        {
            NudgeDurationMs = 0,
        };
    }

    [Fact]
    public async Task Load_lists_sources_then_destinations_with_platform_names_and_states()
    {
        var vm = NewVm();

        await vm.LoadAsync();

        Assert.Equal(2, vm.Providers.Count);
        var garmin = vm.Providers[0];
        Assert.Equal("Garmin Dive", garmin.PlatformName);
        Assert.True(garmin.IsSource);
        Assert.False(garmin.IsLinked);
        Assert.Equal("Not connected", garmin.StatusText);
        Assert.Equal("Connect", garmin.CtaText);
        var ssi = vm.Providers[1];
        Assert.Equal("SSI", ssi.PlatformName);
        Assert.False(ssi.IsSource);
        Assert.Empty(_http.Requests); // status refresh does zero network I/O
    }

    [Fact]
    public async Task Connect_on_an_unlinked_row_pushes_the_provider_sign_in_sheet()
    {
        var vm = NewVm();
        await vm.LoadAsync();

        await vm.ConnectAsync(vm.Providers[0]);

        var nav = Assert.Single(_navigator.Navigations);
        Assert.Equal(Routes.ProviderOAuth, nav.Route);
        Assert.Equal("garmin_connect", nav.Parameters![Routes.Params.ConnectorId]);
        Assert.False(_consent.HasConsent("garmin_connect")); // consent belongs to the sheet, not here
    }

    [Fact]
    public async Task Linked_row_shows_Linked_and_unlink_revokes_consent_and_severs_the_token()
    {
        await _consent.GrantAsync("garmin_connect");
        await _tokens.SetAsync(GarminTokenKey, "live-token");
        var vm = NewVm();
        await vm.LoadAsync();
        var row = vm.Providers[0];
        Assert.True(row.IsLinked);
        Assert.Equal("Linked ✓", row.CtaText);
        Assert.Equal("Linked · synced", row.StatusText);

        _dialogs.ConfirmResult = true;
        await vm.ConnectAsync(row);

        Assert.False(_consent.HasConsent("garmin_connect"));
        Assert.False(row.IsLinked);
        Assert.Equal("Not connected", row.StatusText);
        Assert.Empty(_tokens.Values); // revoke also severed the exchanged token
    }

    [Fact]
    public async Task Unlink_declined_keeps_the_link()
    {
        await _consent.GrantAsync("garmin_connect");
        await _tokens.SetAsync(GarminTokenKey, "live-token");
        var vm = NewVm();
        await vm.LoadAsync();

        _dialogs.ConfirmResult = false;
        await vm.ConnectAsync(vm.Providers[0]);

        Assert.True(_consent.HasConsent("garmin_connect"));
        Assert.True(vm.Providers[0].IsLinked);
    }

    [Fact]
    public async Task Nudge_flag_raises_and_clears()
    {
        var vm = NewVm();
        var observed = new List<bool>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.IsNudging))
            {
                observed.Add(vm.IsNudging);
            }
        };

        await vm.BeginNudgeAsync();

        Assert.Equal([true, false], observed);
        Assert.False(vm.IsNudging);
    }
}
