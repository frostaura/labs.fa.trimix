using Trimix.Core.Abstractions;
using Trimix.Core.Connectors.Cloud;
using Trimix.Core.Connectors.Infra;
using Xunit;

namespace Trimix.Core.Tests.Connectors.Cloud;

/// <summary>
/// The Mares personal-export source: MySSI credentials → shared <see cref="MySsiCloudClient"/> → dives
/// filtered to Mares source computers (Mares has no cloud API of its own; the Head/SSI corridor is the
/// researched path). Gating semantics are inherited from <c>GatedCloudSourceConnector</c> and re-proven
/// per-connector in <c>GuardrailTests</c>; here the Mares-specific behaviour is pinned.
/// </summary>
public class MaresCloudSourceConnectorTests
{
    private readonly RecordedHttpTransport _http = new();
    private readonly InMemorySecureTokenStore _tokens = new();
    private readonly InMemoryConnectorConsentStore _consent = new();

    private const string TokenKey = "connector:mares:access_token";
    private const string Password = "myssi-pass-123";

    private MaresCloudSourceConnector Create(CloudSourceOptions? options = null) =>
        new(_http, _tokens, _consent, new MySsiCloudClient(_http, options));

    /// <summary>Mixed logbook: two Mares dives and a ScubaPro dive that the filter must drop.</summary>
    private const string LogbookJson =
        "{\"dives\":[" +
        "{\"dive_id\":201,\"datetime\":\"2026-06-01T08:00:00Z\",\"divetime\":40,\"brand\":\"Mares\"}," +
        "{\"dive_id\":202,\"datetime\":\"2026-06-02T09:30:00Z\",\"divetime\":35,\"brand\":\"SCUBAPRO (LogTRAK)\"}," +
        "{\"dive_id\":203,\"datetime\":\"2026-06-03T10:15:00Z\",\"divetime\":45,\"brand\":\"MARES Puck Pro\"}]}";

    [Fact]
    public void Descriptor_keeps_the_dark_descriptor_invariant_and_full_profile()
    {
        var d = Create().Descriptor;

        Assert.Equal("mares", d.ConnectorId);
        Assert.Equal(ConnectorKind.CloudApi, d.Kind);
        Assert.Equal(LegalPosture.ToSRiskReverseEng, d.Legal);
        Assert.False(d.EnabledByDefault); // the invariant: every ToSRiskReverseEng descriptor ships dark
        Assert.Equal(ProfileFidelity.FullProfile, d.Fidelity);
        Assert.Equal(AuthModel.UserCredential, d.Auth);
    }

    [Fact]
    public async Task Authenticate_without_consent_does_zero_network()
    {
        var connector = Create();
        connector.ProvideCredentials("diver@example.com", Password);

        var result = await connector.AuthenticateAsync();

        Assert.Equal(ConnectorAuthStatus.NeedsInteraction, result.Status);
        Assert.Contains("MySSI", result.Message, StringComparison.Ordinal); // truthful corridor disclosure
        Assert.Empty(_http.Requests);
        Assert.Empty(_tokens.Values);
    }

    [Fact]
    public async Task Authenticate_exchanges_myssi_credentials_and_stores_only_the_token()
    {
        await _consent.GrantAsync("mares");
        _http.EnqueueText("{\"token\":\"myssi-tok-mares\"}");
        var connector = Create();
        connector.ProvideCredentials("diver@example.com", Password);

        var result = await connector.AuthenticateAsync();

        Assert.Equal(ConnectorAuthStatus.Granted, result.Status);
        Assert.Equal("myssi-tok-mares", _tokens.Values[TokenKey]);
        Assert.DoesNotContain(_tokens.Values.Values, v => v.Contains(Password, StringComparison.Ordinal));
    }

    [Fact]
    public async Task List_filters_the_myssi_logbook_to_mares_dives_only()
    {
        await _consent.GrantAsync("mares");
        _http.EnqueueText("{\"token\":\"myssi-tok-mares\"}");
        var connector = Create();
        connector.ProvideCredentials("diver@example.com", Password);
        await connector.AuthenticateAsync();
        _http.EnqueueText(LogbookJson);

        var refs = await connector.ListDivesSinceAsync(SyncCursor.None);

        Assert.Equal(2, refs.Count); // 201 + 203; the ScubaPro dive is not Mares's to import
        Assert.Equal(["201", "203"], refs.Select(r => r.SourceId).ToArray());
        Assert.All(refs, r => Assert.Equal("mares", r.ConnectorId));
        Assert.Equal(40 * 60, refs[0].DurationSec);
    }

    [Fact]
    public async Task Fetch_resolves_a_full_profile_ImportedDive_through_the_shared_client()
    {
        await _consent.GrantAsync("mares");
        _http.EnqueueText("{\"token\":\"myssi-tok-mares\"}");
        var connector = Create();
        connector.ProvideCredentials("diver@example.com", Password);
        await connector.AuthenticateAsync();
        _http.EnqueueText(
            "{\"dive_id\":201,\"datetime\":\"2026-06-01T08:00:00Z\",\"divetime\":40,\"depth_m\":28.5," +
            "\"temp_c\":21.0,\"gasmix\":\"EAN32\",\"profile\":[{\"t\":0,\"depth_m\":0},{\"t\":60,\"depth_m\":28.5}]}");

        var dive = await connector.FetchProfileAsync(
            new DiveRef("mares", "201", new DateTime(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc), 2400));

        Assert.Equal(28.5, dive.MaxDepthM);
        Assert.Equal("EAN32", dive.GasMix);
        Assert.Equal(2, dive.Samples.Count);
        Assert.EndsWith("/logbook/201", _http.Requests[^1].Url, StringComparison.Ordinal);
    }
}
