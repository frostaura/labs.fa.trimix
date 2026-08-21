using Trimix.Core.Abstractions;
using Trimix.Core.Connectors.Infra;
using Trimix.Core.Platform;
using Trimix.Core.Storage;
using Trimix.Core.Tests.Fakes;
using Xunit;

namespace Trimix.Core.Tests;

/// <summary>
/// Launch orchestration. Trimix has no accounts and no unlock step, so startup NEVER touches
/// connector state — links survive every restart ("link once — dives flow in on their own").
/// Connector erasure is explicit only, owned by the Settings "Delete my data" / "Reset app" flows
/// (see ConnectorPurgeServiceTests). Startup's only reported signal is the first-run flag.
/// </summary>
public sealed class AppStartupServiceTests
{
    private static AppStartupService NewStartup(
        IPreferences? preferences = null,
        ILocalStore? store = null,
        IConnectorConsentStore? consent = null,
        IEnumerable<IDiveSourceConnector>? sources = null) =>
        new(
            store ?? new InMemoryLocalStore(),
            new FakeEntitlementService(),
            new RecordingAnalytics(),
            preferences ?? new InMemoryPreferences(),
            sources ?? [],
            consent ?? new PreferencesConnectorConsentStore(
                new InMemoryPreferences(), new SecureStoreTokenStore(new InMemorySecureStore())));

    [Fact]
    public async Task Startup_leaves_connector_links_untouched()
    {
        // A linked provider on disk (consent flag + exchanged token), as after any normal restart.
        var preferences = new InMemoryPreferences();
        var tokens = new SecureStoreTokenStore(new InMemorySecureStore());
        var consent = new PreferencesConnectorConsentStore(preferences, tokens);
        await consent.GrantAsync("garmin_connect");
        await tokens.SetAsync(ConnectorTokenKeys.CloudSourceToken("garmin_connect"), "cloud-token");

        var startup = NewStartup(preferences);
        await startup.RunAsync();

        // The link SURVIVES the launch — the whole point of "link once".
        Assert.True(consent.HasConsent("garmin_connect"));
        Assert.NotNull(await tokens.GetAsync(ConnectorTokenKeys.CloudSourceToken("garmin_connect")));
    }

    [Fact]
    public async Task Startup_purges_dives_from_providers_that_are_not_connected()
    {
        var preferences = new InMemoryPreferences();
        var tokens = new SecureStoreTokenStore(new InMemorySecureStore());
        var consent = new PreferencesConnectorConsentStore(preferences, tokens);
        await consent.GrantAsync("garmin_connect"); // Garmin connected; ScubaPro is NOT
        var store = new InMemoryLocalStore();
        await store.UpsertDiveAsync(Stats.StatsTestFactory.Dive(sourceVendor: "garmin_connect"));
        await store.UpsertDiveAsync(Stats.StatsTestFactory.Dive(sourceVendor: "scubapro")); // orphan

        var startup = NewStartup(preferences, store, consent,
            [new FakeSourceConnector("garmin_connect"), new FakeSourceConnector("scubapro")]);
        await startup.RunAsync();

        // The orphan (unconnected ScubaPro) dive is swept on launch; the connected Garmin dive stays.
        var kept = Assert.Single(await store.GetDivesAsync());
        Assert.Equal("garmin_connect", kept.SourceVendor);
    }

    [Fact]
    public async Task First_run_is_reported_until_the_first_run_flag_is_set()
    {
        var preferences = new InMemoryPreferences();
        var startup = NewStartup(preferences);

        Assert.True(await startup.RunAsync());

        preferences.Set(PreferenceKeys.FirstRunTracked, "1");
        Assert.False(await startup.RunAsync());
    }
}
