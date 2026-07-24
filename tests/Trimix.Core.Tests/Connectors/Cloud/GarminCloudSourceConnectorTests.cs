using System.Text;
using Trimix.Core.Abstractions;
using Trimix.Core.Connectors.Cloud;
using Xunit;

namespace Trimix.Core.Tests.Connectors.Cloud;

/// <summary>
/// The Garmin Connect personal-export (reverse-eng, gated) source connector. Exercises the researched
/// garth-style flow (GET signin → POST signin → token exchange → list diving activities → download ORIGINAL
/// FIT) fully offline against <see cref="RecordedHttpTransport"/>, and pins the founder-directed guardrails.
/// </summary>
public class GarminCloudSourceConnectorTests
{
    private readonly RecordedHttpTransport _http = new();
    private readonly InMemorySecureTokenStore _tokens = new();
    private readonly InMemoryConnectorConsentStore _consent = new();
    private readonly FakeFitActivityParser _fit = new();

    private const string TokenKey = "connector:garmin_connect:access_token";
    private const string Password = "hunter2-secret";

    private GarminCloudSourceConnector Create() => new(_http, _tokens, _consent, _fit);

    private void ScriptLogin() => _http
        .EnqueueText("<input type=\"hidden\" name=\"_csrf\" value=\"CSRF-TOKEN-123\" />")
        .EnqueueText("<script>var response_url = \"https://connect.garmin.com/modern?ticket=ST-0-abcDEF123\";</script>")
        .EnqueueText("{\"access_token\":\"garmin-access-xyz\",\"token_type\":\"Bearer\",\"expires_in\":3599}");

    [Fact]
    public void Descriptor_ships_dark_with_tos_risk_posture()
    {
        var d = Create().Descriptor;

        Assert.Equal("garmin_connect", d.ConnectorId);
        Assert.Equal(ConnectorKind.CloudApi, d.Kind);
        Assert.Equal(LegalPosture.ToSRiskReverseEng, d.Legal);
        Assert.False(d.EnabledByDefault);
        Assert.Equal(AuthModel.UserCredential, d.Auth);
    }

    [Fact]
    public async Task Authenticate_without_consent_does_zero_network_and_returns_disclosure()
    {
        var connector = Create();
        connector.ProvideCredentials("diver@example.com", Password);

        var result = await connector.AuthenticateAsync();

        Assert.Equal(ConnectorAuthStatus.NeedsInteraction, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
        Assert.Contains("Terms of Service", result.Message);
        Assert.Empty(_http.Requests); // guardrail (iii): no I/O before consent
        Assert.Empty(_tokens.Values);
    }

    [Fact]
    public async Task Authenticate_with_consent_but_no_credentials_needs_interaction_no_network()
    {
        await _consent.GrantAsync("garmin_connect");
        var connector = Create();

        var result = await connector.AuthenticateAsync();

        Assert.Equal(ConnectorAuthStatus.NeedsInteraction, result.Status);
        Assert.Empty(_http.Requests);
    }

    [Fact]
    public async Task Authenticate_exchanges_credentials_for_token_and_never_stores_the_password()
    {
        await _consent.GrantAsync("garmin_connect");
        ScriptLogin();
        var connector = Create();
        connector.ProvideCredentials("diver@example.com", Password);

        var result = await connector.AuthenticateAsync();

        Assert.Equal(ConnectorAuthStatus.Granted, result.Status);
        // Only the exchanged token is persisted.
        Assert.Equal("garmin-access-xyz", _tokens.Values[TokenKey]);
        // Guardrail (ii): the raw password is NEVER written to the secure token store.
        Assert.DoesNotContain(_tokens.Values.Values, v => v.Contains(Password, StringComparison.Ordinal));
        // The password was sent once, only in the sign-in POST body, and nowhere else.
        Assert.Equal(3, _http.Requests.Count);
        Assert.Single(_http.Requests, r => r.Body is not null && r.Body.Contains(Uri.EscapeDataString(Password)));
    }

    [Fact]
    public async Task Second_authenticate_reuses_stored_token_without_re_login()
    {
        await _consent.GrantAsync("garmin_connect");
        ScriptLogin();
        var connector = Create();
        connector.ProvideCredentials("diver@example.com", Password);
        await connector.AuthenticateAsync();
        var afterFirst = _http.Requests.Count;

        // No new credentials, no new scripted responses — a re-auth must not hit the network.
        var result = await connector.AuthenticateAsync();

        Assert.Equal(ConnectorAuthStatus.Granted, result.Status);
        Assert.Equal(afterFirst, _http.Requests.Count);
    }

    [Fact]
    public async Task List_then_fetch_maps_activities_and_decodes_the_downloaded_fit()
    {
        await _consent.GrantAsync("garmin_connect");
        ScriptLogin();
        var connector = Create();
        connector.ProvideCredentials("diver@example.com", Password);
        await connector.AuthenticateAsync();

        _http.EnqueueText(
            "[{\"activityId\":123456,\"activityName\":\"Reef dive\",\"startTimeGMT\":\"2026-06-01 08:00:00\"," +
            "\"duration\":2400.0,\"activityType\":{\"typeKey\":\"diving\"}}]");
        var fitBytes = Encoding.ASCII.GetBytes("FIT-BYTES-PLACEHOLDER");
        _http.EnqueueBinary(fitBytes);

        var refs = await connector.ListDivesSinceAsync(SyncCursor.None);
        Assert.Single(refs);
        Assert.Equal("123456", refs[0].SourceId);
        Assert.Equal(new DateTime(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc), refs[0].StartUtc);
        Assert.Equal(2400, refs[0].DurationSec);

        var dive = await connector.FetchProfileAsync(refs[0]);
        Assert.Equal(28.5, dive.MaxDepthM);
        Assert.Equal("EAN32", dive.GasMix);
        // The connector Base64-decoded the download body back to the exact FIT bytes before parsing.
        Assert.Equal(fitBytes, _fit.ParsedPayloads.Single());
    }

    [Fact]
    public async Task List_before_authenticate_throws()
    {
        await _consent.GrantAsync("garmin_connect");
        var connector = Create();

        await Assert.ThrowsAsync<InvalidOperationException>(() => connector.ListDivesSinceAsync(SyncCursor.None));
        Assert.Empty(_http.Requests);
    }

    [Fact]
    public async Task Configured_override_url_replaces_the_default_signin_endpoint()
    {
        // A deploy-time CloudSourceOptions override injects the real endpoint without touching code; a null
        // override (the default) keeps the in-source researched constant, so every other test is unchanged.
        await _consent.GrantAsync("garmin_connect");
        var options = new CloudSourceOptions { GarminSigninUrl = "https://override.example/garmin/signin" };
        var connector = new GarminCloudSourceConnector(_http, _tokens, _consent, _fit, options);
        ScriptLogin();
        connector.ProvideCredentials("diver@example.com", Password);

        await connector.AuthenticateAsync();

        Assert.Equal("https://override.example/garmin/signin", _http.Requests[0].Url);
    }
}
