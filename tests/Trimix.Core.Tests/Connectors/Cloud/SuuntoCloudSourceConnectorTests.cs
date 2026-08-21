using Trimix.Core.Abstractions;
using Trimix.Core.Connectors.Cloud;
using Xunit;

namespace Trimix.Core.Tests.Connectors.Cloud;

/// <summary>
/// The Suunto private-cloud personal-export (reverse-eng, gated) source connector. Backend is Sports Tracker
/// (<c>api.sports-tracker.com</c>, verified 2026-08-07): <c>POST /login2</c> (form <c>l/p/version</c>) →
/// <c>sessionkey</c> carried as <c>STTAuthorization</c>; <c>GET /workouts</c> lists. Fidelity is depth+temp
/// only. The diving-activity filter and the profile-fetch endpoint remain UNVERIFIED (no dive in the captured
/// account) — Suunto ships "Coming soon" until a real dive confirms them.
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
    public async Task Login2_stores_only_the_session_key_never_the_password()
    {
        await _consent.GrantAsync("suunto_cloud");
        // Sports Tracker login2 returns { sessionkey, userKey, … } — the session key becomes the token.
        _http.EnqueueText("{\"username\":\"dean\",\"userKey\":\"stt-session-abc\",\"sessionkey\":\"stt-session-abc\"}");
        var connector = Create();
        connector.ProvideCredentials("diver@example.com", Password);

        var result = await connector.AuthenticateAsync();

        Assert.Equal(ConnectorAuthStatus.Granted, result.Status);
        Assert.Equal("stt-session-abc", _tokens.Values[TokenKey]);
        Assert.DoesNotContain(_tokens.Values.Values, v => v.Contains(Password, StringComparison.Ordinal));
        Assert.Single(_http.Requests);
        // Verified corridor: form-encoded login2 with l/p/version — not an OAuth token grant.
        Assert.Contains("login2", _http.Requests[0].Url);
        Assert.Contains("l=diver%40example.com", _http.Requests[0].Body);
        Assert.Contains("version=2", _http.Requests[0].Body);
    }

    [Fact]
    public async Task List_parses_the_workouts_payload_and_filters_by_activity()
    {
        await _consent.GrantAsync("suunto_cloud");
        _http.EnqueueText("{\"sessionkey\":\"stt-session-abc\"}");
        var connector = Create();
        connector.ProvideCredentials("diver@example.com", Password);
        await connector.AuthenticateAsync();

        // startTime is epoch millis (computed here so the parse path is validated, not a hardcoded constant).
        // Real Sports Tracker schema: workoutKey, activityId, startTime (ms), totalTime (sec, may be float).
        // One "dive" (activityId 82 — the UNVERIFIED diving-id placeholder), one run (activityId 1) filtered out.
        var start = new DateTimeOffset(2026, 6, 13, 8, 0, 0, TimeSpan.Zero);
        var startMs = start.ToUnixTimeMilliseconds();
        _http.EnqueueText(
            "{\"error\":null,\"payload\":[" +
            $"{{\"workoutKey\":\"6a75ebc7\",\"activityId\":82,\"startTime\":{startMs},\"stopTime\":{startMs + 2_100_000},\"totalTime\":2100.0}}," +
            $"{{\"workoutKey\":\"6a75ec0f\",\"activityId\":1,\"startTime\":{startMs},\"totalTime\":1800.0}}]," +
            "\"metadata\":{\"workoutcount\":\"2\",\"until\":\"1786112971226\"}}");

        var refs = await connector.ListDivesSinceAsync(SyncCursor.None);

        var dive = Assert.Single(refs);
        Assert.Equal("6a75ebc7", dive.SourceId);
        Assert.Equal(2100, dive.DurationSec);
        Assert.Equal(start.UtcDateTime, dive.StartUtc);
        // Verified corridor: the list call carries the session key in STTAuthorization and hits /workouts.
        Assert.Contains("/workouts?since=0&offset=0&limit=50", _http.Requests[1].Url);
        Assert.Equal("stt-session-abc", _http.Requests[1].Headers["STTAuthorization"]);
    }

    [Fact]
    public async Task Fetch_degrades_to_a_summary_dive_when_the_profile_endpoint_fails()
    {
        // "Support what we have": the profile endpoint is an unverified guess, so a non-2xx must NOT drop the
        // dive — FetchProfile returns a summary-only dive (start + duration from the list) with no samples.
        await _consent.GrantAsync("suunto_cloud");
        _http.EnqueueText("{\"sessionkey\":\"stt-session-abc\"}");
        var connector = Create();
        connector.ProvideCredentials("diver@example.com", Password);
        await connector.AuthenticateAsync();

        var start = new DateTimeOffset(2026, 6, 13, 8, 0, 0, TimeSpan.Zero);
        var diveRef = new DiveRef("suunto_cloud", "6a75ebc7", start.UtcDateTime, 2100);
        _http.EnqueueText("{\"error\":\"not found\"}", status: 404); // the guessed profile endpoint 404s

        var dive = await connector.FetchProfileAsync(diveRef);

        Assert.Equal(start.UtcDateTime, dive.StartUtc);
        Assert.Equal(2100, dive.DurationSec);
        Assert.Equal(0, dive.MaxDepthM);
        Assert.Empty(dive.Samples); // profile absent — imported as a summary, not dropped
    }

    [Fact]
    public async Task Configured_override_url_replaces_the_default_login_endpoint()
    {
        // Deploy-time override injects the real login host; a null override keeps the verified constant.
        await _consent.GrantAsync("suunto_cloud");
        var options = new CloudSourceOptions { SuuntoTokenUrl = "https://override.example/suunto/login2" };
        var connector = new SuuntoCloudSourceConnector(_http, _tokens, _consent, _fit, options);
        _http.EnqueueText("{\"sessionkey\":\"stt-session-abc\"}");
        connector.ProvideCredentials("diver@example.com", Password);

        await connector.AuthenticateAsync();

        Assert.Equal("https://override.example/suunto/login2", _http.Requests[0].Url);
    }
}
