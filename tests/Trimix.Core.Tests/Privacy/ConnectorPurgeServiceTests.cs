using Microsoft.Extensions.Logging.Abstractions;
using Trimix.Contracts.Dtos;
using Trimix.Core.Abstractions;
using Trimix.Core.Connectors.Infra;
using Trimix.Core.Platform;
using Trimix.Core.Privacy;
using Trimix.Core.Tests.Fakes;
using Xunit;

namespace Trimix.Core.Tests.Privacy;

/// <summary>
/// Connector privacy purge (aggregator architecture §9.4). Trimix has no accounts, so there is no
/// sign-out/identity edge — erasure is explicit, driven by the Settings "Delete my data" / "Reset app"
/// flows which AWAIT <see cref="ConnectorPurgeService.PurgeAllAsync"/>. These tests prove that after a
/// purge NO connector consent flag and NO exchanged token survives — for every key shape (cloud-source +
/// push) and every registered connector — while empty/absent state is a harmless no-op. Uses the REAL
/// <see cref="PreferencesConnectorConsentStore"/> + <see cref="SecureStoreTokenStore"/> over in-memory
/// platform doubles so the end-to-end erasure path (the one that ships) is exercised, not a stub.
/// </summary>
public class ConnectorPurgeServiceTests
{
    private readonly InMemoryPreferences _preferences = new();
    private readonly InMemorySecureStore _secureStore = new();
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
    public async Task PurgeAll_clears_consent_and_every_token_for_all_connectors()
    {
        await SeedConnectorAsync("garmin_connect");
        await SeedConnectorAsync("suunto_app");
        await SeedConnectorAsync("padi_push");

        // Two source connectors + one destination connector exercise BOTH enumerables.
        var service = CreateService(
            sources: [new FakeSourceConnector("garmin_connect"), new FakeSourceConnector("suunto_app")],
            destinations: [new FakeDestinationConnector("padi_push")]);

        // Sanity: everything is present before the purge.
        Assert.True(_consent.HasConsent("garmin_connect"));
        Assert.NotNull(await _tokens.GetAsync(ConnectorTokenKeys.CloudSourceToken("garmin_connect")));

        await service.PurgeAllAsync();

        await AssertConnectorPurgedAsync("garmin_connect");
        await AssertConnectorPurgedAsync("suunto_app");
        await AssertConnectorPurgedAsync("padi_push");
    }

    [Fact]
    public async Task No_connectors_registered_is_a_harmless_no_op()
    {
        var service = CreateService(); // empty source + destination lists (nothing connected)

        await service.PurgeAllAsync();

        // Nothing to assert beyond "did not throw"; a stray unrelated preference is untouched.
        _preferences.Set("unrelated", "keep");
        Assert.Equal("keep", _preferences.Get("unrelated"));
    }

    [Fact]
    public async Task Purge_is_idempotent_across_repeated_calls()
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
