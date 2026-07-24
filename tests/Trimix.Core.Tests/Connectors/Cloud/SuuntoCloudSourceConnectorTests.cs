using System.Text;
using Trimix.Core.Abstractions;
using Trimix.Core.Connectors.Cloud;
using Xunit;

namespace Trimix.Core.Tests.Connectors.Cloud;

/// <summary>
/// The Suunto private-cloud personal-export (reverse-eng, gated) source connector: ROPC token → list scuba
/// workouts → export FIT. Fidelity is intentionally depth+temp only (cloud FIT strips tank/gas per research).
/// </summary>
public class SuuntoCloudSourceConnectorTests
{
    private readonly RecordedHttpTransport _http = new();
    private readonly InMemorySecureTokenStore _tokens = new();
    private readonly InMemoryConnectorConsentStore _consent = new();
    private readonly FakeFitActivityParser _fit = new();

    private const string TokenKey = "connector:suunto_cloud:access_token";
    private const string Password = "suunto-pass-123";

    private SuuntoCloudSourceConnector Create() => new(_http, _tokens, _consent, _fit);

    [Fact]
    public void Descriptor_ships_dark_depth_temp_only()
    {
        var d = Create().Descriptor;

        Assert.Equal("suunto_cloud", d.ConnectorId);
        Assert.Equal(LegalPosture.ToSRiskReverseEng, d.Legal);
        Assert.False(d.EnabledByDefault);
        Assert.Equal(ProfileFidelity.DepthTempOnly, d.Fidelity);
    }

    [Fact]
    public async Task Authenticate_without_consent_does_zero_network()
    {
        var connector = Create();
        connector.ProvideCredentials("diver@example.com", Password);

        var result = await connector.AuthenticateAsync();

        Assert.Equal(ConnectorAuthStatus.NeedsInteraction, result.Status);
        Assert.Empty(_http.Requests);
        Assert.Empty(_tokens.Values);
    }

    [Fact]
    public async Task Authenticate_password_grant_stores_only_the_token()
    {
        await _consent.GrantAsync("suunto_cloud");
        _http.EnqueueText("{\"access_token\":\"suunto-jwt-abc\",\"token_type\":\"Bearer\",\"expires_in\":86400}");
        var connector = Create();
        connector.ProvideCredentials("diver@example.com", Password);

        var result = await connector.AuthenticateAsync();

        Assert.Equal(ConnectorAuthStatus.Granted, result.Status);
        Assert.Equal("suunto-jwt-abc", _tokens.Values[TokenKey]);
        Assert.DoesNotContain(_tokens.Values.Values, v => v.Contains(Password, StringComparison.Ordinal));
        Assert.Single(_http.Requests);
    }

    [Fact]
    public async Task List_filters_to_diving_workouts_and_fetch_exports_fit()
    {
        await _consent.GrantAsync("suunto_cloud");
        _http.EnqueueText("{\"access_token\":\"suunto-jwt-abc\"}");
        var connector = Create();
        connector.ProvideCredentials("diver@example.com", Password);
        await connector.AuthenticateAsync();

        // startTime is epoch millis (computed here so the parse path is validated, not a hardcoded constant).
        // One dive (activityId 82), one run (activityId 1) that must be filtered out.
        var start = new DateTimeOffset(2026, 6, 13, 8, 0, 0, TimeSpan.Zero);
        var startMs = start.ToUnixTimeMilliseconds();
        _http.EnqueueText(
            "{\"payload\":[" +
            $"{{\"workoutKey\":\"w-dive-1\",\"activityId\":82,\"startTime\":{startMs},\"totalTime\":2100}}," +
            $"{{\"workoutKey\":\"w-run-9\",\"activityId\":1,\"startTime\":{startMs},\"totalTime\":1800}}]}}");
        var fitBytes = Encoding.ASCII.GetBytes("SUUNTO-FIT");
        _http.EnqueueBinary(fitBytes);

        var refs = await connector.ListDivesSinceAsync(SyncCursor.None);
        Assert.Single(refs);
        Assert.Equal("w-dive-1", refs[0].SourceId);
        Assert.Equal(2100, refs[0].DurationSec);
        Assert.Equal(start.UtcDateTime, refs[0].StartUtc);

        var dive = await connector.FetchProfileAsync(refs[0]);
        Assert.Equal(28.5, dive.MaxDepthM);
        Assert.Equal(fitBytes, _fit.ParsedPayloads.Single());
    }

    [Fact]
    public async Task Configured_override_url_replaces_the_default_token_endpoint()
    {
        // Deploy-time override injects the real token host; a null override keeps the researched constant.
        await _consent.GrantAsync("suunto_cloud");
        var options = new CloudSourceOptions { SuuntoTokenUrl = "https://override.example/suunto/token" };
        var connector = new SuuntoCloudSourceConnector(_http, _tokens, _consent, _fit, options);
        _http.EnqueueText("{\"access_token\":\"suunto-jwt-abc\"}");
        connector.ProvideCredentials("diver@example.com", Password);

        await connector.AuthenticateAsync();

        Assert.Equal("https://override.example/suunto/token", _http.Requests[0].Url);
    }
}
