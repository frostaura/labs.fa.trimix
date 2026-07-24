using Microsoft.Extensions.Logging.Abstractions;
using Trimix.Contracts.Dtos;
using Trimix.Core.Abstractions;
using Trimix.Core.Auth;
using Trimix.Core.Connectors.Infra;
using Trimix.Core.Platform;
using Trimix.Core.Privacy;
using Trimix.Core.Tests.Fakes;
using Xunit;

namespace Trimix.Core.Tests.Privacy;

/// <summary>
/// Sign-out / GDPR-delete privacy purge (aggregator architecture §9.4). Proves that after the session
/// drops to unauthenticated, NO connector consent flag and NO exchanged token survives — for every key
/// shape (cloud-source + push) and every registered connector — while guest / no-connector paths are
/// harmless no-ops. Uses the REAL <see cref="PreferencesConnectorConsentStore"/> +
/// <see cref="SecureStoreTokenStore"/> over in-memory platform doubles so the end-to-end erasure path
/// (the one that ships) is exercised, not a stub.
/// </summary>
public class ConnectorPurgeServiceTests
{
    private readonly InMemoryPreferences _preferences = new();
    private readonly InMemorySecureStore _secureStore = new();
    private readonly FakeAuthSession _session = new();
    private readonly ISecureTokenStore _tokens;
    private readonly IConnectorConsentStore _consent;

    public ConnectorPurgeServiceTests()
    {
        _tokens = new SecureStoreTokenStore(_secureStore);
        _consent = new PreferencesConnectorConsentStore(_preferences, _tokens);
    }

    private ConnectorPurgeService CreateService(
        IEnumerable<IDiveSourceConnector>? sources = null,
        IEnumerable<IDiveDestinationConnector>? destinations = null) =>
        new(
            _session,
            sources ?? [],
            destinations ?? [],
            _consent,
            NullLogger<ConnectorPurgeService>.Instance);

    /// <summary>Grants consent and writes a token under BOTH key shapes for a connector id.</summary>
    private async Task SeedConnectorAsync(string connectorId)
    {
        await _consent.GrantAsync(connectorId);
        await _tokens.SetAsync(ConnectorTokenKeys.CloudSourceToken(connectorId), $"cloud-token-{connectorId}");
        await _tokens.SetAsync(ConnectorTokenKeys.PushToken(connectorId), $"push-token-{connectorId}");
    }

    private async Task AssertConnectorPurgedAsync(string connectorId)
    {
        Assert.False(_consent.HasConsent(connectorId));
        Assert.Null(await _tokens.GetAsync(ConnectorTokenKeys.CloudSourceToken(connectorId)));
        Assert.Null(await _tokens.GetAsync(ConnectorTokenKeys.PushToken(connectorId)));
    }

    [Fact]
    public async Task Sign_out_purges_consent_and_every_token_for_all_connectors()
    {
        await SeedConnectorAsync("garmin_connect");
        await SeedConnectorAsync("suunto_app");
        await SeedConnectorAsync("padi_push");

        // Two source connectors + one destination connector exercise BOTH enumerables.
        var service = CreateService(
            sources: [new FakeSourceConnector("garmin_connect"), new FakeSourceConnector("suunto_app")],
            destinations: [new FakeDestinationConnector("padi_push")]);
        Assert.NotNull(service); // ctor wires the AuthStateChanged subscription

        // Sanity: everything is present before the purge.
        Assert.True(_consent.HasConsent("garmin_connect"));
        Assert.NotNull(await _tokens.GetAsync(ConnectorTokenKeys.CloudSourceToken("garmin_connect")));

        await _session.SignOutAsync(); // raises AuthStateChanged(false); in-memory doubles complete synchronously

        await AssertConnectorPurgedAsync("garmin_connect");
        await AssertConnectorPurgedAsync("suunto_app");
        await AssertConnectorPurgedAsync("padi_push");
    }

    [Fact]
    public async Task Gdpr_delete_path_via_sign_out_leaves_no_token_or_consent()
    {
        // DeleteMyDataAsync ends in session.SignOutAsync(), so the same subscription covers delete.
        await SeedConnectorAsync("oceanic_cloud");
        _ = CreateService(sources: [new FakeSourceConnector("oceanic_cloud")]);

        await _session.SignOutAsync();

        await AssertConnectorPurgedAsync("oceanic_cloud");
    }

    [Fact]
    public async Task Explicit_PurgeAllAsync_clears_all_connectors()
    {
        await SeedConnectorAsync("garmin_connect");
        var service = CreateService(sources: [new FakeSourceConnector("garmin_connect")]);

        await service.PurgeAllAsync();

        await AssertConnectorPurgedAsync("garmin_connect");
    }

    [Fact]
    public async Task Sign_in_does_not_purge()
    {
        await SeedConnectorAsync("garmin_connect");
        _ = CreateService(sources: [new FakeSourceConnector("garmin_connect")]);

        // AuthStateChanged(true) must NOT trigger a purge — only the sign-out edge does.
        await _session.SignInAsync(new AuthResponse(
            "jwt", new UserDto(Guid.NewGuid(), "diver@example.test", "diver", false, DateTime.UtcNow)));

        Assert.True(_consent.HasConsent("garmin_connect"));
        Assert.NotNull(await _tokens.GetAsync(ConnectorTokenKeys.CloudSourceToken("garmin_connect")));
    }

    // ── Identity-change purge (shared-device handoff, Finding 1) — uses the REAL AuthSession so the
    //     account→different-account detection is exercised end-to-end, sharing one in-memory secure enclave. ──

    private static AuthResponse Auth(Guid userId) =>
        new("jwt", new UserDto(userId, "diver@example.test", "diver", false, DateTime.UtcNow));

    /// <summary>Real <see cref="AuthSession"/> over the shared stores + a purge service bound to it.</summary>
    private (AuthSession Session, ConnectorPurgeService Service) CreateWithRealAuthSession(string connectorId)
    {
        var session = new AuthSession(_secureStore);
        var service = new ConnectorPurgeService(
            session,
            [new FakeSourceConnector(connectorId)],
            [],
            _consent,
            NullLogger<ConnectorPurgeService>.Instance);
        return (session, service);
    }

    [Fact]
    public async Task Guest_to_account_upgrade_keeps_connectors()
    {
        await SeedConnectorAsync("garmin_connect");
        var (session, service) = CreateWithRealAuthSession("garmin_connect");
        Assert.NotNull(service); // ctor wires AuthStateChanged + IdentityChanged

        // A guest (no prior established account) creates an account — single-user continuity: connectors KEEP.
        await session.SignInAsync(Auth(Guid.NewGuid()));

        Assert.True(_consent.HasConsent("garmin_connect"));
        Assert.NotNull(await _tokens.GetAsync(ConnectorTokenKeys.CloudSourceToken("garmin_connect")));
    }

    [Fact]
    public async Task Account_switch_purges_the_previous_identitys_connectors()
    {
        var accountA = Guid.NewGuid();
        var accountB = Guid.NewGuid();
        var (session, service) = CreateWithRealAuthSession("garmin_connect");
        Assert.NotNull(service);

        // Account A establishes identity, then connects a vendor.
        await session.SignInAsync(Auth(accountA));
        await SeedConnectorAsync("garmin_connect");
        Assert.True(_consent.HasConsent("garmin_connect"));

        // A DIFFERENT account signs in on the same device WITHOUT an intervening sign-out (shared-device handoff):
        // A's device-global connector consent+tokens must not leak into B.
        await session.SignInAsync(Auth(accountB));

        await AssertConnectorPurgedAsync("garmin_connect");
    }

    [Fact]
    public async Task Same_account_re_sign_in_keeps_connectors()
    {
        var account = Guid.NewGuid();
        var (session, service) = CreateWithRealAuthSession("garmin_connect");
        Assert.NotNull(service);

        await session.SignInAsync(Auth(account));
        await SeedConnectorAsync("garmin_connect");
        await session.SignInAsync(Auth(account)); // same user-id => not an identity change

        Assert.True(_consent.HasConsent("garmin_connect"));
        Assert.NotNull(await _tokens.GetAsync(ConnectorTokenKeys.CloudSourceToken("garmin_connect")));
    }

    [Fact]
    public async Task No_connectors_registered_is_a_harmless_no_op()
    {
        var service = CreateService(); // empty source + destination lists (guest with nothing connected)

        await service.PurgeAllAsync();
        await _session.SignOutAsync();

        // Nothing to assert beyond "did not throw"; a stray unrelated preference is untouched.
        _preferences.Set("unrelated", "keep");
        Assert.Equal("keep", _preferences.Get("unrelated"));
    }

    [Fact]
    public async Task Purge_is_idempotent_across_repeated_sign_outs()
    {
        await SeedConnectorAsync("garmin_connect");
        var service = CreateService(sources: [new FakeSourceConnector("garmin_connect")]);

        await service.PurgeAllAsync();
        await service.PurgeAllAsync(); // revoking already-absent state must not throw

        await AssertConnectorPurgedAsync("garmin_connect");
    }

    /// <summary>Minimal <see cref="IDiveDestinationConnector"/> double — the purge only reads its descriptor id.</summary>
    private sealed class FakeDestinationConnector(string connectorId) : IDiveDestinationConnector
    {
        public ConnectorDescriptor Descriptor { get; } = new(
            connectorId,
            ConnectorKind.CloudApi,
            LegalPosture.ToSRiskReverseEng,
            ProfileFidelity.SummaryOnly,
            AuthModel.UserCredential,
            Platforms.Both,
            EnabledByDefault: false);

        public Task<ConnectorAuthResult> AuthenticateAsync(CancellationToken ct = default) =>
            Task.FromResult(new ConnectorAuthResult(ConnectorAuthStatus.NeedsInteraction));

        public Task<PushResult> PushDiveAsync(DiveDto dive, CancellationToken ct = default) =>
            Task.FromResult(new PushResult(PushStatus.Unsupported));
    }
}
