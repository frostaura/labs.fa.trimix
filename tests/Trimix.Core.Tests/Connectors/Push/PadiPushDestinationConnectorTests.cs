using Trimix.Core.Abstractions;
using Trimix.Core.Connectors.Push;
using Trimix.Core.Tests.Connectors.Cloud;
using Xunit;

namespace Trimix.Core.Tests.Connectors.Push;

/// <summary>
/// Gated PADI personal-export push. Proves the structural guardrails hold end-to-end: ships dark
/// (ToS-risk + not-default), does ZERO network before consent, persists only the exchanged token (never the
/// password), and maps HTTP status to <see cref="PushResult"/>. Reuses the Cloud-test HTTP/token/consent
/// doubles so the flow is fully offline.
/// </summary>
public class PadiPushDestinationConnectorTests
{
    private const string ConnectorId = "padi_push_personal";
    private const string TokenKey = "padi_push_personal.token";
    private const string Password = "padi-secret-42";

    private readonly RecordedHttpTransport _http = new();
    private readonly InMemorySecureTokenStore _tokens = new();
    private readonly InMemoryConnectorConsentStore _consent = new();
    private readonly PadiPushOptions _options = new();

    private PadiPushDestinationConnector Create() =>
        new(_consent, _tokens, _http, _options, new Dl7DiveExporter());

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
        Assert.Contains("NOT an official PADI integration", result.Message);
        Assert.Empty(_http.Requests);
        Assert.Empty(_tokens.Values);
    }

    [Fact]
    public async Task Push_without_consent_is_skipped_with_zero_network()
    {
        var result = await Create().PushDiveAsync(DiveFixture.Trimix());

        Assert.Equal(PushStatus.Skipped, result.Status);
        Assert.Empty(_http.Requests);
    }

    [Fact]
    public async Task SignIn_without_consent_does_zero_network()
    {
        var result = await Create().SignInAsync("diver", Password);

        Assert.Equal(ConnectorAuthStatus.NeedsInteraction, result.Status);
        Assert.Empty(_http.Requests);
        Assert.Empty(_tokens.Values);
    }

    [Fact]
    public async Task SignIn_with_consent_but_no_configured_endpoint_is_unavailable()
    {
        await _consent.GrantAsync(ConnectorId);

        var result = await Create().SignInAsync("diver", Password);

        Assert.Equal(ConnectorAuthStatus.Unavailable, result.Status);
        Assert.Empty(_http.Requests); // AuthUrl null => stay dark, no exchange attempted
        Assert.Empty(_tokens.Values);
    }

    [Fact]
    public async Task SignIn_stores_only_the_exchanged_token_never_the_password()
    {
        await _consent.GrantAsync(ConnectorId);
        _options.AuthUrl = "https://example.test/padi/auth";
        _http.EnqueueText("{\"token\":\"padi-token-xyz\"}");

        var result = await Create().SignInAsync("diver", Password);

        Assert.Equal(ConnectorAuthStatus.Granted, result.Status);
        Assert.Equal("padi-token-xyz", _tokens.Values[TokenKey]);
        Assert.DoesNotContain(_tokens.Values.Values, v => v.Contains(Password, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Push_with_consent_but_no_write_endpoint_is_unsupported()
    {
        await _consent.GrantAsync(ConnectorId);
        await _tokens.SetAsync(TokenKey, "padi-token-xyz");

        var result = await Create().PushDiveAsync(DiveFixture.Trimix());

        Assert.Equal(PushStatus.Unsupported, result.Status);
        Assert.Empty(_http.Requests);
    }

    [Fact]
    public async Task Push_maps_2xx_to_pushed_and_sends_a_bearer_dl7_payload()
    {
        await _consent.GrantAsync(ConnectorId);
        await _tokens.SetAsync(TokenKey, "padi-token-xyz");
        _options.PushUrl = "https://example.test/padi/dives";
        _http.EnqueueText(string.Empty, 201);

        var result = await Create().PushDiveAsync(DiveFixture.Trimix());

        Assert.Equal(PushStatus.Pushed, result.Status);
        var request = Assert.Single(_http.Requests);
        Assert.Equal("https://example.test/padi/dives", request.Url);
        Assert.Equal("Bearer padi-token-xyz", request.Headers["Authorization"]);
        Assert.StartsWith("FSH|", request.Body); // DL7 rendering (summary fidelity)
    }

    [Fact]
    public async Task Push_maps_non_2xx_to_failed()
    {
        await _consent.GrantAsync(ConnectorId);
        await _tokens.SetAsync(TokenKey, "padi-token-xyz");
        _options.PushUrl = "https://example.test/padi/dives";
        _http.EnqueueText("nope", 500);

        var result = await Create().PushDiveAsync(DiveFixture.Trimix());

        Assert.Equal(PushStatus.Failed, result.Status);
        Assert.Contains("500", result.Message);
    }
}
