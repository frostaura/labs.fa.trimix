using Microsoft.Extensions.Logging.Abstractions;
using Trimix.Core.Abstractions;
using Trimix.Core.Auth;
using Trimix.Core.Connectors.Infra;
using Trimix.Core.Platform;
using Trimix.Core.Privacy;
using Trimix.Core.Storage;
using Trimix.Core.Tests.Fakes;
using Xunit;

namespace Trimix.Core.Tests;

/// <summary>
/// Launch-time connector-purge reconciliation (Finding 2b). An interrupted / failed sign-out purge can strand a
/// vendor consent flag / exchanged token in the secure enclave (which survives uninstall on the iOS Keychain).
/// AppStartupService re-runs the purge once on any UNAUTHENTICATED launch so a stranded link is cleared on the
/// next open, and never runs it on the authenticated path (which syncs instead).
/// </summary>
public sealed class AppStartupServiceTests
{
    private static AppStartupService NewStartup(
        IAuthSession session,
        ISyncService sync,
        IConnectorPurgeService purge,
        IPreferences? preferences = null) =>
        new(
            new InMemoryLocalStore(),
            session,
            new FakeEntitlementService(),
            new RecordingAnalytics(),
            sync,
            preferences ?? new InMemoryPreferences(),
            purge);

    [Fact]
    public async Task Unauthenticated_startup_reconciles_by_purging_connectors()
    {
        var sync = new FakeSyncService();
        var purge = new RecordingConnectorPurgeService();
        var startup = NewStartup(new FakeAuthSession { IsAuthenticated = false }, sync, purge);

        await startup.RunAsync();

        Assert.Equal(1, purge.PurgeAllCalls); // belt-and-suspenders reconciliation ran
        Assert.Equal(0, sync.SyncCalls);      // …and the authenticated sync path did not
    }

    [Fact]
    public async Task Authenticated_startup_syncs_and_does_not_purge()
    {
        var sync = new FakeSyncService();
        var purge = new RecordingConnectorPurgeService();
        var startup = NewStartup(new FakeAuthSession { IsAuthenticated = true }, sync, purge);

        await startup.RunAsync();

        Assert.Equal(0, purge.PurgeAllCalls); // the reconciliation never disrupts a signed-in launch
        Assert.Equal(1, sync.SyncCalls);
    }

    [Fact]
    public async Task Interrupted_purge_is_retried_on_next_unauthenticated_startup()
    {
        // Shared in-memory secure enclave; the token store is flaky and throws on its FIRST RemoveAsync,
        // modelling a purge interrupted mid-sweep.
        var secureStore = new InMemorySecureStore();
        var preferences = new InMemoryPreferences();
        var tokens = new FlakyTokenStore(new SecureStoreTokenStore(secureStore));
        var consent = new PreferencesConnectorConsentStore(preferences, tokens);

        // A connected vendor: consent flag + an exchanged token.
        await consent.GrantAsync("garmin_connect");
        await tokens.SetAsync(ConnectorTokenKeys.CloudSourceToken("garmin_connect"), "cloud-token");

        var session = new AuthSession(secureStore); // empty store => unauthenticated after InitializeAsync
        var purge = new ConnectorPurgeService(
            session,
            [new FakeSourceConnector("garmin_connect")],
            [],
            consent,
            NullLogger<ConnectorPurgeService>.Instance);
        var startup = NewStartup(session, new FakeSyncService(), purge, preferences);

        // First unauthenticated launch: the flaky store throws, the per-connector failure is swallowed inside
        // PurgeAllAsync (RunAsync does not throw), and the token is stranded.
        await startup.RunAsync();
        Assert.NotNull(await tokens.GetAsync(ConnectorTokenKeys.CloudSourceToken("garmin_connect")));

        // Next unauthenticated launch: the store no longer throws and the reconciliation clears the stranded token.
        await startup.RunAsync();
        Assert.Null(await tokens.GetAsync(ConnectorTokenKeys.CloudSourceToken("garmin_connect")));
        Assert.False(consent.HasConsent("garmin_connect"));
    }

    /// <summary>Delegating <see cref="ISecureTokenStore"/> that throws on its FIRST RemoveAsync, then behaves.</summary>
    private sealed class FlakyTokenStore(ISecureTokenStore inner) : ISecureTokenStore
    {
        private bool _thrown;

        public Task<string?> GetAsync(string key, CancellationToken ct = default) => inner.GetAsync(key, ct);

        public Task SetAsync(string key, string token, CancellationToken ct = default) => inner.SetAsync(key, token, ct);

        public Task RemoveAsync(string key, CancellationToken ct = default)
        {
            if (!_thrown)
            {
                _thrown = true;
                throw new InvalidOperationException("secure enclave unavailable (interrupted purge)");
            }

            return inner.RemoveAsync(key, ct);
        }
    }
}
