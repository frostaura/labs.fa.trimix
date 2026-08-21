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
    private readonly Trimix.Core.Storage.InMemoryLocalStore _store = new();
    private readonly Trimix.Core.Platform.InMemoryPreferences _preferences = new();
    private FakeProviderPriority _priority = new();

    public ConnectedAccountsViewModelTests() =>
        _consent = new Trimix.Core.Connectors.Infra.PreferencesConnectorConsentStore(
            new Trimix.Core.Platform.InMemoryPreferences(), _tokens);

    private SyncSessionViewModel NewSession() => new(
        _coordinator, [], _consent, _navigator,
        new Trimix.Core.Platform.InMemoryPreferences(), new FakeClock())
    {
        AutoSyncDelayMs = 0,
        DoneLingerMs = 0,
    };

    private ConnectedAccountsViewModel NewVm(
        IEnumerable<IDiveSourceConnector>? sources = null,
        IEnumerable<IDiveDestinationConnector>? destinations = null)
    {
        var garmin = new GarminCloudSourceConnector(_http, _tokens, _consent);
        var ssi = new SsiPushDestinationConnector(_consent, _tokens, _http, new SsiPushOptions());
        var sourceList = (sources ?? [garmin]).ToList();
        // Seed the priority order with the source ids so reorder/ordering assertions have a defined baseline.
        _priority = new FakeProviderPriority(sourceList.Select(s => s.Descriptor.ConnectorId).ToArray());
        return new ConnectedAccountsViewModel(
            sourceList, destinations ?? [ssi], _consent, NewSession(), _navigator, _dialogs,
            _store, _priority, _preferences)
        {
            NudgeDurationMs = 0,
        };
    }

    /// <summary>Grant consent + a stored token for a connector so LoadAsync reports it linked (Connected group).</summary>
    private async Task LinkAsync(params string[] connectorIds)
    {
        foreach (var id in connectorIds)
        {
            await _consent.GrantAsync(id);
            await _tokens.SetAsync($"connector:{id}:access_token", "live-token");
        }
    }

    [Fact]
    public async Task Load_splits_providers_into_connected_and_disconnected_groups()
    {
        await LinkAsync("garmin_connect"); // Garmin linked; SSI stays unlinked
        var vm = NewVm();

        await vm.LoadAsync();

        var garmin = Assert.Single(vm.ConnectedProviders);
        Assert.Equal("Garmin Dive", garmin.PlatformName);
        Assert.True(garmin.IsLinked);
        var ssi = Assert.Single(vm.DisconnectedProviders);
        Assert.Equal("SSI", ssi.PlatformName);
        Assert.True(vm.HasConnectedProviders);
        Assert.True(vm.HasDisconnectedProviders);
        Assert.Empty(_http.Requests); // status refresh does zero network I/O
    }

    [Fact]
    public async Task Connect_on_an_unlinked_row_pushes_the_provider_sign_in_sheet()
    {
        var vm = NewVm();
        await vm.LoadAsync();

        await vm.ConnectAsync(vm.DisconnectedProviders.First(p => p.ConnectorId == "garmin_connect"));

        var nav = Assert.Single(_navigator.Navigations);
        Assert.Equal(Routes.ProviderOAuth, nav.Route);
        Assert.Equal("garmin_connect", nav.Parameters![Routes.Params.ConnectorId]);
        Assert.False(_consent.HasConsent("garmin_connect")); // consent belongs to the sheet, not here
    }

    [Fact]
    public async Task Linked_row_shows_Linked_and_unlink_revokes_consent_and_severs_the_token()
    {
        await LinkAsync("garmin_connect");
        var vm = NewVm();
        await vm.LoadAsync();
        var row = Assert.Single(vm.ConnectedProviders);
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
        await LinkAsync("garmin_connect");
        var vm = NewVm();
        await vm.LoadAsync();

        _dialogs.ConfirmResult = false;
        await vm.ConnectAsync(vm.ConnectedProviders[0]);

        Assert.True(_consent.HasConsent("garmin_connect"));
        Assert.True(vm.ConnectedProviders[0].IsLinked);
    }

    [Fact]
    public async Task Unlinking_a_source_deletes_its_dives_but_keeps_other_providers()
    {
        await LinkAsync("garmin_connect");
        // Two dives from Garmin (being unlinked) and one from ScubaPro (staying).
        await _store.UpsertDiveAsync(Stats.StatsTestFactory.Dive(sourceVendor: "garmin_connect"));
        await _store.UpsertDiveAsync(Stats.StatsTestFactory.Dive(sourceVendor: "garmin_connect"));
        await _store.UpsertDiveAsync(Stats.StatsTestFactory.Dive(sourceVendor: "scubapro"));
        var vm = NewVm();
        await vm.LoadAsync();
        _dialogs.ConfirmResult = true;

        await vm.ConnectAsync(vm.ConnectedProviders[0]); // Garmin is the only connected row

        var remaining = await _store.GetDivesAsync();
        var kept = Assert.Single(remaining);
        Assert.Equal("scubapro", kept.SourceVendor); // only Garmin's dives were removed
    }

    [Fact]
    public async Task Unlink_confirmation_warns_that_the_providers_dives_are_removed()
    {
        await LinkAsync("garmin_connect");
        var recorder = new RecordingConfirmDialogs { ConfirmResult = false };
        var vm = new ConnectedAccountsViewModel(
            [new GarminCloudSourceConnector(_http, _tokens, _consent)], [], _consent, NewSession(), _navigator,
            recorder, _store, new FakeProviderPriority("garmin_connect"), _preferences);
        await vm.LoadAsync();

        await vm.ConnectAsync(vm.ConnectedProviders[0]);

        var confirm = Assert.Single(recorder.Confirms);
        Assert.Contains("dives", confirm.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Reordering_the_connected_list_persists_the_new_priority_order()
    {
        await LinkAsync("garmin_connect", "scubapro");
        var vm = NewVm([new FakeSourceConnector("garmin_connect"), new FakeSourceConnector("scubapro")]);
        await vm.LoadAsync();
        Assert.Equal("garmin_connect", vm.ConnectedProviders[0].ConnectorId); // default order

        // Simulate the CollectionView's native reorder (it moves the bound item), then the drop commits it.
        var scuba = vm.ConnectedProviders.First(p => p.ConnectorId == "scubapro");
        vm.ConnectedProviders.Move(vm.ConnectedProviders.IndexOf(scuba), 0);
        vm.PersistConnectedOrder();

        Assert.Equal("scubapro", vm.ConnectedProviders[0].ConnectorId);
        Assert.Equal(["scubapro", "garmin_connect"], _priority.Order()); // persisted
    }

    [Fact]
    public async Task Reorder_hint_shows_with_two_connected_sources_and_dismisses_once()
    {
        await LinkAsync("garmin_connect", "scubapro");
        var vm = NewVm([new FakeSourceConnector("garmin_connect"), new FakeSourceConnector("scubapro")]);

        await vm.LoadAsync();
        Assert.True(vm.IsReorderHintVisible); // ≥2 connected sources, not yet seen

        vm.DismissReorderHint();
        Assert.False(vm.IsReorderHintVisible);

        await vm.LoadAsync(); // a later visit: the one-shot flag keeps it hidden
        Assert.False(vm.IsReorderHintVisible);
    }

    [Fact]
    public async Task Reorder_hint_is_hidden_with_a_single_connected_source()
    {
        await LinkAsync("garmin_connect");
        var vm = NewVm(); // one connected source (Garmin)
        await vm.LoadAsync();
        Assert.False(vm.IsReorderHintVisible);
    }

    [Fact]
    public async Task Not_yet_implemented_provider_shows_as_coming_soon_and_disabled()
    {
        // Oceanic+ is registered but not verified live, so it ships "Coming soon".
        var vm = NewVm([new FakeSourceConnector("oceanic_cloud")]);
        await vm.LoadAsync();

        var oceanic = vm.DisconnectedProviders.First(p => p.ConnectorId == "oceanic_cloud");
        Assert.False(oceanic.IsAvailable);
        Assert.True(oceanic.IsComingSoon);
        Assert.Equal("Coming soon", oceanic.StatusText);
        Assert.Equal("Coming soon", oceanic.CtaText);
        Assert.False(oceanic.IsLinked);
    }

    [Fact]
    public async Task Coming_soon_provider_stays_disconnected_even_with_consent_and_token()
    {
        // Even if local consent/token state exists, a not-yet-live provider can never be linked.
        await LinkAsync("oceanic_cloud");
        var vm = NewVm([new FakeSourceConnector("oceanic_cloud")]);
        await vm.LoadAsync();

        Assert.Empty(vm.ConnectedProviders);
        var oceanic = vm.DisconnectedProviders.First(p => p.ConnectorId == "oceanic_cloud");
        Assert.False(oceanic.IsLinked);
        Assert.True(oceanic.IsComingSoon);
    }

    [Fact]
    public async Task Connect_on_a_coming_soon_row_is_a_no_op()
    {
        var vm = NewVm([new FakeSourceConnector("oceanic_cloud")]);
        await vm.LoadAsync();

        await vm.ConnectAsync(vm.DisconnectedProviders.First(p => p.ConnectorId == "oceanic_cloud"));

        Assert.Empty(_navigator.Navigations); // no sign-in sheet pushed
    }

    [Fact]
    public async Task Supported_provider_with_consent_is_connected()
    {
        // Suunto is supported (sign-in + list verified), so with consent + token it links like Garmin.
        await LinkAsync("suunto_cloud");
        var vm = NewVm([new FakeSourceConnector("suunto_cloud")]);
        await vm.LoadAsync();

        var suunto = Assert.Single(vm.ConnectedProviders);
        Assert.True(suunto.IsAvailable);
        Assert.False(suunto.IsComingSoon);
        Assert.True(suunto.IsLinked);
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

    /// <summary>FakeDialogs variant that records confirm calls so the unlink-copy assertion can inspect the message.</summary>
    private sealed class RecordingConfirmDialogs : IUserDialogs
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
