using Trimix.Core.Abstractions;
using Trimix.Core.Connectors.Cloud;
using Trimix.Core.Connectors.Infra;
using Trimix.Core.Platform;
using Xunit;

namespace Trimix.Core.Tests.Connectors.Cloud;

/// <summary>
/// Cross-cutting proof that the founder-directed legal guardrails hold for <b>every</b> reverse-engineered
/// cloud-pull connector (Garmin, Suunto, Oceanic+, Mares, ScubaPro), not just per-connector discipline. These are the structural
/// invariants from <c>docs/known-gaps.md</c>:
/// <list type="bullet">
///   <item>(a) <b>Zero network I/O before consent</b> — <see cref="GatedCloudSourceConnector.AuthenticateAsync"/>
///     performs no HTTP exchange until <c>IConnectorConsentStore.HasConsent(id)</c> is true.</item>
///   <item>(b) <b>The password is never persisted</b> — only the exchanged token reaches
///     <c>ISecureTokenStore</c>; no stored value equals or contains the raw password.</item>
///   <item>Ships dark + truthfully labelled — <c>ToSRiskReverseEng</c>, <c>EnabledByDefault=false</c>,
///     "personal export" (never "official &lt;Vendor&gt;").</item>
/// </list>
/// Parameterised by connector id so each connector is an independent, named test case. Reuses the shared
/// <see cref="RecordedHttpTransport"/> / in-memory stores from <c>CloudTestDoubles</c>.
/// </summary>
public class GuardrailTests
{
    private const string Password = "p@ssw0rd-DO-NOT-PERSIST";

    private static GatedCloudSourceConnector CreateConnector(
        string id,
        RecordedHttpTransport http,
        InMemorySecureTokenStore tokens,
        IConnectorConsentStore consent) => id switch
        {
            OceanicCloudSourceConnector.Id => new OceanicCloudSourceConnector(http, tokens, consent),
            GarminCloudSourceConnector.Id => new GarminCloudSourceConnector(http, tokens, consent, new FakeFitActivityParser()),
            SuuntoCloudSourceConnector.Id => new SuuntoCloudSourceConnector(http, tokens, consent, new FakeFitActivityParser()),
            MaresCloudSourceConnector.Id => new MaresCloudSourceConnector(http, tokens, consent, new MySsiCloudClient(http)),
            ScubaProCloudSourceConnector.Id => new ScubaProCloudSourceConnector(http, tokens, consent, new MySsiCloudClient(http)),
            _ => throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown cloud connector."),
        };

    /// <summary>Scripts each connector's researched happy-path credential→token exchange so the login actually
    /// runs to completion (proving the password reaches the wire yet never the token store).</summary>
    private static void ScriptLogin(string id, RecordedHttpTransport http)
    {
        switch (id)
        {
            case GarminCloudSourceConnector.Id:
                http.EnqueueText("<input type=\"hidden\" name=\"_csrf\" value=\"CSRF-TOKEN-123\" />")
                    .EnqueueText("<script>var response_url = \"https://connect.garmin.com/modern?ticket=ST-0-abcDEF123\";</script>")
                    .EnqueueText("{\"access_token\":\"garmin-access-xyz\",\"token_type\":\"Bearer\"}");
                break;
            case SuuntoCloudSourceConnector.Id:
                http.EnqueueText("{\"access_token\":\"suunto-jwt-abc\"}");
                break;
            case OceanicCloudSourceConnector.Id:
                http.EnqueueText("{\"token\":\"oceanic-tok-xyz\"}");
                break;
            case MaresCloudSourceConnector.Id:
            case ScubaProCloudSourceConnector.Id:
                // Both route through the shared MySSI corridor (one login shape for the pair).
                http.EnqueueText("{\"token\":\"myssi-tok-xyz\"}");
                break;
        }
    }

    [Theory]
    [InlineData(GarminCloudSourceConnector.Id)]
    [InlineData(SuuntoCloudSourceConnector.Id)]
    [InlineData(OceanicCloudSourceConnector.Id)]
    [InlineData(MaresCloudSourceConnector.Id)]
    [InlineData(ScubaProCloudSourceConnector.Id)]
    public async Task No_http_exchange_occurs_before_consent_is_granted(string id)
    {
        var http = new RecordedHttpTransport();
        var tokens = new InMemorySecureTokenStore();
        var consent = new InMemoryConnectorConsentStore(); // starts denied
        var connector = CreateConnector(id, http, tokens, consent);
        // Even with credentials queued, no consent => no network, no persistence.
        connector.ProvideCredentials("diver@example.com", Password);

        var result = await connector.AuthenticateAsync();

        Assert.Equal(ConnectorAuthStatus.NeedsInteraction, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.Message)); // the disclosure is surfaced
        Assert.Empty(http.Requests); // (a): guardrail (iii) — zero I/O before consent
        Assert.Empty(tokens.Values);
    }

    [Theory]
    [InlineData(GarminCloudSourceConnector.Id)]
    [InlineData(SuuntoCloudSourceConnector.Id)]
    [InlineData(OceanicCloudSourceConnector.Id)]
    [InlineData(MaresCloudSourceConnector.Id)]
    [InlineData(ScubaProCloudSourceConnector.Id)]
    public async Task Password_is_never_written_to_the_token_store(string id)
    {
        var http = new RecordedHttpTransport();
        var tokens = new InMemorySecureTokenStore();
        var consent = new InMemoryConnectorConsentStore();
        await consent.GrantAsync(id);
        ScriptLogin(id, http);
        var connector = CreateConnector(id, http, tokens, consent);
        connector.ProvideCredentials("diver@example.com", Password);

        var result = await connector.AuthenticateAsync();

        Assert.Equal(ConnectorAuthStatus.Granted, result.Status);
        // A token WAS exchanged and stored — so the guardrail assertions below are not vacuously true.
        Assert.NotEmpty(tokens.Values);
        // (b): no stored value equals the raw password, and none merely contains it either.
        Assert.DoesNotContain(Password, tokens.Values.Values);
        Assert.DoesNotContain(tokens.Values.Values, v => v.Contains(Password, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(GarminCloudSourceConnector.Id)]
    [InlineData(SuuntoCloudSourceConnector.Id)]
    [InlineData(OceanicCloudSourceConnector.Id)]
    [InlineData(MaresCloudSourceConnector.Id)]
    [InlineData(ScubaProCloudSourceConnector.Id)]
    public void Every_cloud_connector_ships_dark_and_is_truthfully_labelled(string id)
    {
        var connector = CreateConnector(id, new RecordedHttpTransport(), new InMemorySecureTokenStore(), new InMemoryConnectorConsentStore());
        var d = connector.Descriptor;

        Assert.Equal(id, d.ConnectorId);
        Assert.Equal(ConnectorKind.CloudApi, d.Kind);
        Assert.Equal(LegalPosture.ToSRiskReverseEng, d.Legal); // guardrail (i)
        Assert.False(d.EnabledByDefault); // guardrail (i): ships dark
        Assert.Contains("personal export", connector.DisplayName, StringComparison.OrdinalIgnoreCase); // (iv)
        Assert.DoesNotContain("official", connector.DisplayName, StringComparison.OrdinalIgnoreCase); // (iv)
    }

    [Theory]
    [InlineData(GarminCloudSourceConnector.Id)]
    [InlineData(SuuntoCloudSourceConnector.Id)]
    [InlineData(OceanicCloudSourceConnector.Id)]
    [InlineData(MaresCloudSourceConnector.Id)]
    [InlineData(ScubaProCloudSourceConnector.Id)]
    public async Task Revoke_disconnect_clears_the_exchanged_token_so_reconnect_forces_reauth(string id)
    {
        var http = new RecordedHttpTransport();
        var tokens = new InMemorySecureTokenStore();
        // The REAL default consent store — the sole "disconnect" surface. Its revoke must also sever the token.
        var consent = new PreferencesConnectorConsentStore(new InMemoryPreferences(), tokens);
        await consent.GrantAsync(id);
        ScriptLogin(id, http);
        var connector = CreateConnector(id, http, tokens, consent);
        connector.ProvideCredentials("diver@example.com", Password);

        // Sign in → a token is exchanged and persisted in the secure enclave.
        Assert.Equal(ConnectorAuthStatus.Granted, (await connector.AuthenticateAsync()).Status);
        Assert.NotEmpty(tokens.Values);

        // Disconnect: consent AND the exchanged token must both be gone (the shipped disclosure promises it).
        await consent.RevokeAsync(id);
        Assert.False(consent.HasConsent(id));
        Assert.Empty(tokens.Values); // the live vendor token no longer survives in the enclave

        // Re-grant WITHOUT re-supplying credentials must NOT silently resurrect a Granted auth from a stale token.
        await consent.GrantAsync(id);
        Assert.Equal(ConnectorAuthStatus.NeedsInteraction, (await connector.AuthenticateAsync()).Status);
    }

    [Theory]
    [InlineData(GarminCloudSourceConnector.Id)]
    [InlineData(SuuntoCloudSourceConnector.Id)]
    [InlineData(OceanicCloudSourceConnector.Id)]
    [InlineData(MaresCloudSourceConnector.Id)]
    [InlineData(ScubaProCloudSourceConnector.Id)]
    public async Task Pending_password_does_not_linger_when_an_existing_token_short_circuits_authenticate(string id)
    {
        var http = new RecordedHttpTransport();
        var tokens = new InMemorySecureTokenStore();
        var consent = new PreferencesConnectorConsentStore(new InMemoryPreferences(), tokens);
        await consent.GrantAsync(id);
        ScriptLogin(id, http);
        var connector = CreateConnector(id, http, tokens, consent);

        // First sign-in exchanges + stores a token.
        connector.ProvideCredentials("diver@example.com", Password);
        Assert.Equal(ConnectorAuthStatus.Granted, (await connector.AuthenticateAsync()).Status);

        // Submit credentials AGAIN for the already-connected connector: AuthenticateAsync returns Granted via
        // the existing-token short-circuit and must NOT leave the plaintext password resident on the singleton.
        connector.ProvideCredentials("diver@example.com", Password);
        Assert.Equal(ConnectorAuthStatus.Granted, (await connector.AuthenticateAsync()).Status);

        // Sever ONLY the token (not consent), then authenticate again WITHOUT re-supplying credentials. If the
        // second submit had lingered, this would exchange the stale password and Grant; with the fix the pending
        // credentials were cleared at entry, so it correctly needs a fresh sign-in.
        foreach (var tokenKey in ConnectorTokenKeys.AllFor(id))
        {
            await tokens.RemoveAsync(tokenKey);
        }

        Assert.Equal(ConnectorAuthStatus.NeedsInteraction, (await connector.AuthenticateAsync()).Status);
    }
}
