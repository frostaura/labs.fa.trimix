using Trimix.Core.Abstractions;
using Trimix.Core.Connectors.Cloud;
using Trimix.Core.Connectors.Infra;
using Xunit;

namespace Trimix.Core.Tests.Connectors.Cloud;

/// <summary>
/// The ScubaPro personal-export source: MySSI credentials → shared <see cref="MySsiCloudClient"/> → dives
/// filtered to ScubaPro/Uwatec/LogTRAK source computers (ScubaPro has no cloud API; LogTRAK→MySSI is the
/// researched corridor). Gating semantics are re-proven per-connector in <c>GuardrailTests</c>.
/// </summary>
public class ScubaProCloudSourceConnectorTests
{
    private readonly RecordedHttpTransport _http = new();
    private readonly InMemorySecureTokenStore _tokens = new();
    private readonly InMemoryConnectorConsentStore _consent = new();

    private const string TokenKey = "connector:scubapro:access_token";
    private const string Password = "myssi-pass-456";

    private ScubaProCloudSourceConnector Create() =>
        new(_http, _tokens, _consent, new MySsiCloudClient(_http));

    /// <summary>Mixed logbook: a ScubaPro dive, a legacy Uwatec dive, and a Mares dive the filter must drop.</summary>
    private const string LogbookJson =
        "{\"dives\":[" +
        "{\"dive_id\":301,\"datetime\":\"2026-06-01T08:00:00Z\",\"divetime\":40,\"brand\":\"SCUBAPRO G2 (LogTRAK)\"}," +
        "{\"dive_id\":302,\"datetime\":\"2026-06-02T09:30:00Z\",\"divetime\":30,\"brand\":\"Uwatec Galileo\"}," +
        "{\"dive_id\":303,\"datetime\":\"2026-06-03T10:15:00Z\",\"divetime\":45,\"brand\":\"Mares\"}]}";

    [Fact]
    public void Descriptor_keeps_the_dark_descriptor_invariant_and_full_profile()
    {
        var d = Create().Descriptor;

        Assert.Equal("scubapro", d.ConnectorId);
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
        Assert.Contains("LogTRAK", result.Message, StringComparison.Ordinal); // truthful corridor disclosure
        Assert.Empty(_http.Requests);
        Assert.Empty(_tokens.Values);
    }

    [Fact]
    public async Task Authenticate_exchanges_myssi_credentials_and_stores_only_the_token()
    {
        await _consent.GrantAsync("scubapro");
        _http.EnqueueText("{\"token\":\"myssi-tok-sp\"}");
        var connector = Create();
        connector.ProvideCredentials("diver@example.com", Password);

        var result = await connector.AuthenticateAsync();

        Assert.Equal(ConnectorAuthStatus.Granted, result.Status);
        Assert.Equal("myssi-tok-sp", _tokens.Values[TokenKey]);
        Assert.DoesNotContain(_tokens.Values.Values, v => v.Contains(Password, StringComparison.Ordinal));
    }

    [Fact]
    public async Task List_filters_the_myssi_logbook_to_scubapro_family_dives()
    {
        await _consent.GrantAsync("scubapro");
        _http.EnqueueText("{\"token\":\"myssi-tok-sp\"}");
        var connector = Create();
        connector.ProvideCredentials("diver@example.com", Password);
        await connector.AuthenticateAsync();
        _http.EnqueueText(LogbookJson);

        var refs = await connector.ListDivesSinceAsync(SyncCursor.None);

        // ScubaPro-branded AND legacy Uwatec dives belong to this source; the Mares dive does not.
        Assert.Equal(["301", "302"], refs.Select(r => r.SourceId).ToArray());
        Assert.All(refs, r => Assert.Equal("scubapro", r.ConnectorId));
    }

    [Fact]
    public async Task Fetch_resolves_a_full_profile_ImportedDive_through_the_shared_client()
    {
        await _consent.GrantAsync("scubapro");
        _http.EnqueueText("{\"token\":\"myssi-tok-sp\"}");
        var connector = Create();
        connector.ProvideCredentials("diver@example.com", Password);
        await connector.AuthenticateAsync();
        _http.EnqueueText(
            "{\"dive_id\":301,\"datetime\":\"2026-06-01T08:00:00Z\",\"divetime\":40,\"depth_m\":31.2," +
            "\"temp_c\":19.5,\"profile\":[{\"t\":0,\"depth_m\":0},{\"t\":90,\"depth_m\":31.2,\"temp_c\":19.5}]}");

        var dive = await connector.FetchProfileAsync(
            new DiveRef("scubapro", "301", new DateTime(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc), 2400));

        Assert.Equal(31.2, dive.MaxDepthM);
        Assert.Equal(19.5, dive.MinTempC);
        Assert.Equal(2, dive.Samples.Count);
        Assert.EndsWith("/logbook/301", _http.Requests[^1].Url, StringComparison.Ordinal);
    }
}
