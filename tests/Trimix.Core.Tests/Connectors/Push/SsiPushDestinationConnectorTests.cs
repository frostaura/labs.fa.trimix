using Trimix.Core.Abstractions;
using Trimix.Core.Connectors.Push;
using Trimix.Core.Tests.Connectors.Cloud;
using Xunit;

namespace Trimix.Core.Tests.Connectors.Push;

/// <summary>
/// Gated SSI (MySSI) personal-export push. Same structural guardrails as PADI (ships dark, consent-gated,
/// token-not-password) plus the SSI specifics: credentials go on the exchange URL as ephemeral l/p query
/// params and the write payload is summary-only (no profile series, no site).
/// </summary>
public class SsiPushDestinationConnectorTests
{
    private const string ConnectorId = "ssi_push_personal";
    private const string TokenKey = "ssi_push_personal.token";
    private const string Password = "ssi-secret-99";

    private readonly RecordedHttpTransport _http = new();
    private readonly InMemorySecureTokenStore _tokens = new();
    private readonly InMemoryConnectorConsentStore _consent = new();
    private readonly SsiPushOptions _options = new();

    private SsiPushDestinationConnector Create() => new(_consent, _tokens, _http, _options);

    [Fact]
    public void Descriptor_ships_dark_tos_risk_summary()
    {
        var d = Create().Descriptor;
        Assert.Equal(ConnectorId, d.ConnectorId);
        Assert.Equal(LegalPosture.ToSRiskReverseEng, d.Legal);
        Assert.False(d.EnabledByDefault);
        Assert.Equal(ProfileFidelity.SummaryOnly, d.Fidelity);
    }

    [Fact]
    public async Task Authenticate_without_consent_discloses_and_does_zero_network()
    {
        var result = await Create().AuthenticateAsync();

        Assert.Equal(ConnectorAuthStatus.NeedsInteraction, result.Status);
        Assert.Contains("NOT an official SSI integration", result.Message);
        Assert.Empty(_http.Requests);
    }

    [Fact]
    public async Task Push_without_consent_is_skipped_with_zero_network()
    {
        var result = await Create().PushDiveAsync(DiveFixture.Trimix());

        Assert.Equal(PushStatus.Skipped, result.Status);
        Assert.Empty(_http.Requests);
    }

    [Fact]
    public async Task SignIn_exchanges_via_lp_query_and_stores_only_the_token()
    {
        await _consent.GrantAsync(ConnectorId);
        _options.AuthUrl = "https://example.test/ssi/login";
        _http.EnqueueText("{\"access_token\":\"ssi-token-abc\"}");

        var result = await Create().SignInAsync("diver@x.test", Password);

        Assert.Equal(ConnectorAuthStatus.Granted, result.Status);
        Assert.Equal("ssi-token-abc", _tokens.Values[TokenKey]);
        Assert.DoesNotContain(_tokens.Values.Values, v => v.Contains(Password, StringComparison.Ordinal));

        var request = Assert.Single(_http.Requests);
        Assert.Equal("GET", request.Method);
        Assert.Contains("l=diver%40x.test", request.Url); // ephemeral credential on the exchange URL only
    }

    [Fact]
    public async Task Push_with_consent_but_no_write_endpoint_is_unsupported()
    {
        await _consent.GrantAsync(ConnectorId);
        await _tokens.SetAsync(TokenKey, "ssi-token-abc");

        var result = await Create().PushDiveAsync(DiveFixture.Trimix());

        Assert.Equal(PushStatus.Unsupported, result.Status);
        Assert.Empty(_http.Requests);
    }

    [Fact]
    public async Task Push_maps_2xx_to_pushed_with_a_summary_only_payload()
    {
        await _consent.GrantAsync(ConnectorId);
        await _tokens.SetAsync(TokenKey, "ssi-token-abc");
        _options.PushUrl = "https://example.test/ssi/dives";
        _http.EnqueueText(string.Empty, 200);

        var result = await Create().PushDiveAsync(DiveFixture.Trimix());

        Assert.Equal(PushStatus.Pushed, result.Status);
        var request = Assert.Single(_http.Requests);
        Assert.Equal("Bearer ssi-token-abc", request.Headers["Authorization"]);
        Assert.Contains("\"depth_m\":28.5", request.Body);
        Assert.Contains("\"divetime\":53", request.Body);
        // summary-only: no profile series and no dive site in the pushed body.
        Assert.DoesNotContain("profile", request.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("site", request.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Push_maps_non_2xx_to_failed()
    {
        await _consent.GrantAsync(ConnectorId);
        await _tokens.SetAsync(TokenKey, "ssi-token-abc");
        _options.PushUrl = "https://example.test/ssi/dives";
        _http.EnqueueText("bad", 503);

        var result = await Create().PushDiveAsync(DiveFixture.Trimix());

        Assert.Equal(PushStatus.Failed, result.Status);
        Assert.Contains("503", result.Message);
    }
}
