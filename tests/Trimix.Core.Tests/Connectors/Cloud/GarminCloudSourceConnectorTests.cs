using Trimix.Core.Abstractions;
using Trimix.Core.Connectors.Cloud;
using Xunit;

namespace Trimix.Core.Tests.Connectors.Cloud;

/// <summary>
/// The Garmin Dive personal-export (reverse-eng, gated) source connector, exercised fully offline against
/// <see cref="RecordedHttpTransport"/>.
///
/// <para><b>Every fixture below is real.</b> The JSON shapes, field names, epoch-millisecond sample
/// timestamps and column schema are taken verbatim from the live capture recorded in
/// <c>docs/reverse-engineering/garmin-dive-api.md</c> — not invented. A fixture that drifts from that document
/// is a bug in the fixture.</para>
/// </summary>
public class GarminCloudSourceConnectorTests
{
    private readonly RecordedHttpTransport _http = new();
    private readonly InMemorySecureTokenStore _tokens = new();
    private readonly InMemoryConnectorConsentStore _consent = new();

    private const string TokenKey = "connector:garmin_connect:access_token";
    private const string Password = "hunter2-secret";
    private const string ConnectorId = "garmin_connect";

    /// <summary>2026-04-17T13:33:06+02:00 — i.e. 11:33:06Z — as epoch milliseconds, per the capture.</summary>
    private const long StartEpochMs = 1776425586000L;

    private GarminCloudSourceConnector Create(CloudSourceOptions? options = null) =>
        new(_http, _tokens, _consent, options);

    // ── fixtures ────────────────────────────────────────────────────────────────────────────────────

    private const string LoginOk = """
        {
          "serviceURL": "https://mobile.integration.garmin.com/dive/ios",
          "serviceTicketId": "ST-0383050-LXbuWGrC5ql8Kl1eHFRC-sso",
          "responseStatus": { "type": "SUCCESSFUL", "message": "" },
          "responseReason": null,
          "customerMfaInfo": null,
          "consentTypeList": null,
          "captchaAlreadyPassed": false,
          "samlResponse": null,
          "authType": "CAS"
        }
        """;

    private const string TokenOk = """
        {
          "access_token": "jwt-access-abc",
          "token_type": "bearer",
          "refresh_token": "refresh-xyz",
          "expires_in": 86399,
          "refresh_token_expires_in": 2591999,
          "scope": "DIVE_API_READ CONNECT_READ",
          "jti": "793b2a68-65ca-45d3-9b79-1209cafb96c9"
        }
        """;

    private const string SummaryOneDive = """
        {
          "totalCount": 1,
          "pageSize": 100,
          "pageNumber": 0,
          "diveActivities": [
            {
              "id": 28533490,
              "connectActivityId": 22721987079,
              "name": "Single-Gas Dive",
              "diveType": "SINGLE_GAS",
              "number": 69,
              "startTime": "2026-04-17T13:33:06+02:00",
              "timezone": "Africa/Johannesburg",
              "totalTime": 6005.03,
              "maxDepth": 14.415,
              "bottomTime": 5704.1,
              "activitySource": "GARMIN_DEVICE",
              "gases": [
                { "gasStatus": "BOTTOM_GAS", "gasType": "EANX", "percentOxygen": 30, "percentHelium": 0 }
              ]
            }
          ]
        }
        """;

    /// <summary>
    /// Detail with the observed column order. <paramref name="oxygen"/> lets a test vary only the gas.
    /// </summary>
    private static string Detail(int oxygen = 30, int helium = 0) => $$"""
        {
          "id": 28533490,
          "startTime": "2026-04-17T13:33:06+02:00",
          "totalTime": 6005.03,
          "profile": { "maxDepth": 14.415, "averageDepth": 11.23, "bottomTime": 5704.1 },
          "environment": { "maxTemperature": 28.0, "minTemperature": 26.0, "waterType": "SALT" },
          "equipment": { "gases": [ { "percentOxygen": {{oxygen}}, "percentHelium": {{helium}} } ] },
          "tankSensors": [
            { "startingPressure": 231.38, "endingPressure": 40.34, "reservePressure": 67.0, "ratedPressure": 200.0 },
            { "startingPressure": 230.21, "endingPressure": 69.99, "reservePressure": 67.0, "ratedPressure": 200.0 }
          ],
          "records": {
            "numRecords": 2,
            "recordMetaDatas": [
              { "position": 0, "key": "TIMESTAMP" },
              { "position": 1, "key": "HR" },
              { "position": 2, "key": "TEMPERATURE" },
              { "position": 3, "key": "DEPTH" },
              { "position": 4, "key": "NEXT_STOP_DEPTH" },
              { "position": 5, "key": "NDL_TIME" },
              { "position": 6, "key": "PERCENT_CNS_LOAD" },
              { "position": 7, "key": "PO2" },
              { "position": 8, "key": "ASCENT_RATE" }
            ],
            "recordData": [
              [{{StartEpochMs}}, null, 28.0, 1.443, null, null, 4.0, 0.35, -0.283],
              [{{StartEpochMs + 120000}}, 82.0, 26.0, 14.415, 3.0, 12.0, 19.0, 0.74, 0.5]
            ]
          }
        }
        """;

    /// <summary>
    /// The SAME data with the column schema deliberately reordered. Mapping must be identical, because the
    /// connector resolves columns by <c>key</c> — index-based parsing would silently corrupt when Garmin
    /// reorders or inserts a column, which the self-describing schema exists to allow.
    /// </summary>
    private const string DetailShuffledColumns = """
        {
          "id": 28533490,
          "startTime": "2026-04-17T13:33:06+02:00",
          "totalTime": 6005.03,
          "profile": { "maxDepth": 14.415, "averageDepth": 11.23 },
          "environment": { "minTemperature": 26.0 },
          "equipment": { "gases": [ { "percentOxygen": 30, "percentHelium": 0 } ] },
          "records": {
            "numRecords": 2,
            "recordMetaDatas": [
              { "position": 0, "key": "DEPTH" },
              { "position": 1, "key": "PO2" },
              { "position": 2, "key": "SOME_FUTURE_COLUMN" },
              { "position": 3, "key": "TIMESTAMP" },
              { "position": 4, "key": "TEMPERATURE" }
            ],
            "recordData": [
              [1.443, 0.35, 999.0, 1776425586000, 28.0],
              [14.415, 0.74, 999.0, 1776425706000, 26.0]
            ]
          }
        }
        """;

    private void ScriptAuth() => _http.EnqueueText(LoginOk).EnqueueText(TokenOk);

    private async Task<GarminCloudSourceConnector> AuthenticatedAsync()
    {
        await _consent.GrantAsync(ConnectorId);
        ScriptAuth();
        var connector = Create();
        connector.ProvideCredentials("diver@example.com", Password);
        var result = await connector.AuthenticateAsync();
        Assert.Equal(ConnectorAuthStatus.Granted, result.Status);
        return connector;
    }

    // ── descriptor + guardrails ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Descriptor_ships_dark_with_tos_risk_posture()
    {
        var d = Create().Descriptor;

        Assert.Equal(ConnectorId, d.ConnectorId);
        Assert.Equal(ConnectorKind.CloudApi, d.Kind);
        Assert.Equal(LegalPosture.ToSRiskReverseEng, d.Legal);
        Assert.False(d.EnabledByDefault);
        Assert.Equal(AuthModel.UserCredential, d.Auth);
        // The JSON diving API carries the full 1 Hz profile, so fidelity is not degraded by dropping FIT.
        Assert.Equal(ProfileFidelity.FullProfile, d.Fidelity);
    }

    [Fact]
    public void DisplayName_is_truthful_and_never_claims_to_be_official()
    {
        var name = Create().DisplayName;

        Assert.Contains("personal export", name, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("official", name, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Authenticate_without_consent_does_zero_network_and_returns_disclosure()
    {
        var connector = Create();
        connector.ProvideCredentials("diver@example.com", Password);

        var result = await connector.AuthenticateAsync();

        Assert.Equal(ConnectorAuthStatus.NeedsInteraction, result.Status);
        Assert.Contains("Terms of Service", result.Message);
        Assert.Empty(_http.Requests); // guardrail (iii): no I/O before consent
        Assert.Empty(_tokens.Values);
    }

    [Fact]
    public async Task Authenticate_with_consent_but_no_credentials_needs_interaction_no_network()
    {
        await _consent.GrantAsync(ConnectorId);

        var result = await Create().AuthenticateAsync();

        Assert.Equal(ConnectorAuthStatus.NeedsInteraction, result.Status);
        Assert.Empty(_http.Requests);
    }

    // ── auth flow ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Authenticate_posts_credentials_as_json_then_exchanges_the_service_ticket()
    {
        await AuthenticatedAsync();

        // Exactly two calls: the JSON login, then the ticket→token exchange. No CSRF page fetch, no FIT.
        Assert.Equal(2, _http.Requests.Count);

        var login = _http.Requests[0];
        Assert.Equal("POST", login.Method);
        Assert.Contains("sso.garmin.com/mobile/api/login", login.Url);
        Assert.Contains("clientId=DIVE_IOS_DARK", login.Url);
        Assert.Equal("application/json", login.Headers["Content-Type"]);
        Assert.Contains("\"rememberMe\":false", login.Body);

        var token = _http.Requests[1];
        Assert.Equal("POST", token.Method);
        Assert.Contains("diauth.garmin.com/di-oauth2-service/oauth/token", token.Url);
        Assert.Contains("service_ticket=ST-0383050", token.Body);
        Assert.Contains("client_id=DIVE_MOBILE_IOS_DI", token.Body);
        // Basic auth is the client id with an empty secret.
        Assert.Equal("Basic RElWRV9NT0JJTEVfSU9TX0RJOg==", token.Headers["Authorization"]);
    }

    [Fact]
    public async Task Authenticate_stores_access_and_refresh_tokens_but_never_the_password()
    {
        await AuthenticatedAsync();

        var stored = _tokens.Values[TokenKey];

        // Guardrail (ii): the raw password is NEVER written to the secure token store.
        Assert.DoesNotContain(_tokens.Values.Values, v => v.Contains(Password, StringComparison.Ordinal));

        // The 30-day refresh token must survive alongside the 24-hour access token, or the diver is forced
        // to re-enter their password daily.
        Assert.Contains("jwt-access-abc", stored);
        Assert.Contains("refresh-xyz", stored);

        // The password was sent exactly once, in the login body, and nowhere else.
        Assert.Single(_http.Requests, r => r.Body is not null && r.Body.Contains(Password, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Authenticate_reports_mfa_accounts_as_unsupported_rather_than_failing_opaquely()
    {
        await _consent.GrantAsync(ConnectorId);
        _http.EnqueueText("""
            {
              "serviceTicketId": null,
              "responseStatus": { "type": "SUCCESSFUL", "message": "" },
              "customerMfaInfo": { "mfaTargets": ["EMAIL"] }
            }
            """);
        var connector = Create();
        connector.ProvideCredentials("diver@example.com", Password);

        var result = await connector.AuthenticateAsync();

        Assert.Equal(ConnectorAuthStatus.Denied, result.Status);
        Assert.Contains("multi-factor", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_tokens.Values);
        // It must not blunder on to the token exchange with no ticket.
        Assert.Single(_http.Requests);
    }

    [Fact]
    public async Task Authenticate_surfaces_a_rejected_sign_in_message()
    {
        await _consent.GrantAsync(ConnectorId);
        _http.EnqueueText("""
            {
              "serviceTicketId": null,
              "responseStatus": { "type": "INVALID_USERNAME_PASSWORD", "message": "Bad credentials" }
            }
            """);
        var connector = Create();
        connector.ProvideCredentials("diver@example.com", Password);

        var result = await connector.AuthenticateAsync();

        Assert.Equal(ConnectorAuthStatus.Denied, result.Status);
        Assert.Contains("Bad credentials", result.Message);
        Assert.Empty(_tokens.Values);
    }

    [Theory]
    [InlineData(429, "rate-limiting")]
    [InlineData(401, "username or password")]
    [InlineData(503, "unavailable")]
    public async Task Authenticate_distinguishes_throttling_from_bad_credentials(int status, string expected)
    {
        // Observed live: Garmin returns 429 for repeated sign-ins from one address. Reporting that as
        // "wrong password" sends the diver off changing a password that was never the problem.
        await _consent.GrantAsync(ConnectorId);
        _http.EnqueueText("{}", status);
        var connector = Create();
        connector.ProvideCredentials("diver@example.com", Password);

        var result = await connector.AuthenticateAsync();

        Assert.Equal(ConnectorAuthStatus.Denied, result.Status);
        Assert.Contains(expected, result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_tokens.Values);
    }

    [Fact]
    public async Task Second_authenticate_reuses_the_stored_token_without_re_login()
    {
        var connector = await AuthenticatedAsync();
        var afterFirst = _http.Requests.Count;

        var result = await connector.AuthenticateAsync();

        Assert.Equal(ConnectorAuthStatus.Granted, result.Status);
        Assert.Equal(afterFirst, _http.Requests.Count);
    }

    // ── listing ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_maps_the_dive_summary_page()
    {
        var connector = await AuthenticatedAsync();
        _http.EnqueueText(SummaryOneDive);

        var refs = await connector.ListDivesSinceAsync(SyncCursor.None);

        var dive = Assert.Single(refs);
        Assert.Equal("28533490", dive.SourceId);
        Assert.Equal("Single-Gas Dive", dive.Label);
        Assert.Equal(6005, dive.DurationSec);
        // startTime carries a +02:00 offset and must be normalised to UTC, not truncated.
        Assert.Equal(new DateTime(2026, 4, 17, 11, 33, 6, DateTimeKind.Utc), dive.StartUtc);

        Assert.Contains("requestedPage=0", _http.Requests[^1].Url);
        Assert.Contains("resultsPerPage=100", _http.Requests[^1].Url);
    }

    [Fact]
    public async Task List_applies_the_cursor_client_side_because_the_api_has_no_since_parameter()
    {
        var connector = await AuthenticatedAsync();
        _http.EnqueueText(SummaryOneDive);

        var refs = await connector.ListDivesSinceAsync(new SyncCursor(new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc)));

        Assert.Empty(refs); // the April dive predates the cursor
        // No "since"/"startDate" may be invented on the query string — no such parameter was ever observed.
        Assert.DoesNotContain("startDate", _http.Requests[^1].Url, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("since", _http.Requests[^1].Url, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task List_before_authenticate_throws_and_does_no_network()
    {
        await _consent.GrantAsync(ConnectorId);
        var connector = Create();

        await Assert.ThrowsAsync<InvalidOperationException>(() => connector.ListDivesSinceAsync(SyncCursor.None));
        Assert.Empty(_http.Requests);
    }

    // ── fetching the profile ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Fetch_maps_the_columnar_records_block_to_samples()
    {
        var connector = await AuthenticatedAsync();
        _http.EnqueueText(SummaryOneDive).EnqueueText(Detail());
        var refs = await connector.ListDivesSinceAsync(SyncCursor.None);

        var dive = await connector.FetchProfileAsync(refs[0]);

        Assert.Equal(new DateTime(2026, 4, 17, 11, 33, 6, DateTimeKind.Utc), dive.StartUtc);
        Assert.Equal(14.415, dive.MaxDepthM);
        Assert.Equal(11.23, dive.AvgDepthM);
        Assert.Equal(26.0, dive.MinTempC);
        Assert.Equal("EAN30", dive.GasMix);
        Assert.Equal(2, dive.Samples.Count);

        // Sample 0: TIMESTAMP is epoch ms and must become seconds elapsed from the dive start.
        var first = dive.Samples[0];
        Assert.Equal(0, first.TSeconds);
        Assert.Equal(1.443, first.DepthM);
        Assert.Equal(28.0, first.TempC);
        Assert.Equal(0.35, first.PpO2);
        Assert.Null(first.HeartRate); // null in the row stays null, not zero
        // ASCENT_RATE is m/s on the wire, m/min in the DTO.
        Assert.Equal(-0.283 * 60, first.AscentRateMPerMin!.Value, 5);

        var second = dive.Samples[1];
        Assert.Equal(120, second.TSeconds);
        Assert.Equal(14.415, second.DepthM);
        Assert.Equal(82.0, second.HeartRate);
        Assert.Equal(0.2, second.Ndl!.Value, 5); // NDL_TIME 12 s → 0.2 min
        Assert.Equal(3.0, second.CeilingM);
        Assert.Equal(19.0, second.Cns);

        // Fetching a dive must not download a FIT file — one detail GET and nothing more.
        Assert.Contains("/diving/v1/dive/detail/28533490", _http.Requests[^1].Url);
    }

    [Fact]
    public async Task Fetch_converts_ndl_from_seconds_to_minutes()
    {
        // NDL_TIME is seconds on the wire; ProfileSampleDto.Ndl is minutes (the profile table renders it as
        // " min"). Confirmed live: the minimum on a 14.4 m EAN30 dive was 2234 → 37.2 min. Passing the raw
        // value through overstated every NDL readout by 60×.
        var connector = await AuthenticatedAsync();
        _http.EnqueueText(SummaryOneDive).EnqueueText(Detail());
        var refs = await connector.ListDivesSinceAsync(SyncCursor.None);

        var dive = await connector.FetchProfileAsync(refs[0]);

        // The fixture's second row carries NDL_TIME = 12.0 seconds → 0.2 minutes.
        Assert.Equal(0.2, dive.Samples[1].Ndl!.Value, 5);
    }

    [Fact]
    public async Task Fetch_resolves_columns_by_key_so_a_reordered_schema_still_maps_correctly()
    {
        var connector = await AuthenticatedAsync();
        _http.EnqueueText(SummaryOneDive).EnqueueText(DetailShuffledColumns);
        var refs = await connector.ListDivesSinceAsync(SyncCursor.None);

        var dive = await connector.FetchProfileAsync(refs[0]);

        // Identical results despite a shuffled schema and an unknown extra column.
        Assert.Equal(2, dive.Samples.Count);
        Assert.Equal(0, dive.Samples[0].TSeconds);
        Assert.Equal(1.443, dive.Samples[0].DepthM);
        Assert.Equal(28.0, dive.Samples[0].TempC);
        Assert.Equal(0.35, dive.Samples[0].PpO2);
        Assert.Equal(120, dive.Samples[1].TSeconds);
        Assert.Equal(14.415, dive.Samples[1].DepthM);
    }

    [Fact]
    public async Task Fetch_maps_cylinders_from_gases_and_tank_sensors()
    {
        var connector = await AuthenticatedAsync();
        _http.EnqueueText(SummaryOneDive).EnqueueText(Detail());
        var refs = await connector.ListDivesSinceAsync(SyncCursor.None);

        var dive = await connector.FetchProfileAsync(refs[0]);

        // One gas across two tanks (sidemount): both tanks must survive, carrying the single gas.
        Assert.Equal(2, dive.Cylinders!.Count);

        var first = dive.Cylinders[0];
        Assert.Equal(30, first.O2);
        Assert.Equal(0, first.He);
        Assert.Equal(231.38, first.StartBar);
        // endingPressure, NOT reservePressure (67) — the latter is a configured threshold, and reporting it
        // as the end pressure would overstate the gas the diver actually surfaced with.
        Assert.Equal(40.34, first.EndBar);
        Assert.Equal(200.0, first.WorkingPressureBar);

        var second = dive.Cylinders[1];
        Assert.Equal(30, second.O2);
        Assert.Equal(230.21, second.StartBar);
        Assert.Equal(69.99, second.EndBar);
    }

    [Theory]
    [InlineData(21, 0, "AIR")]      // 21% is AIR, never "EAN21" — matches the app's own labelling
    [InlineData(30, 0, "EAN30")]
    [InlineData(32, 0, "EAN32")]
    [InlineData(18, 45, "TX18/45")]
    public async Task Fetch_describes_the_gas_mix(int oxygen, int helium, string expected)
    {
        var connector = await AuthenticatedAsync();
        _http.EnqueueText(SummaryOneDive).EnqueueText(Detail(oxygen, helium));
        var refs = await connector.ListDivesSinceAsync(SyncCursor.None);

        var dive = await connector.FetchProfileAsync(refs[0]);

        Assert.Equal(expected, dive.GasMix);
    }

    [Fact]
    public async Task Fetch_survives_a_detail_response_with_no_records_block()
    {
        var connector = await AuthenticatedAsync();
        _http.EnqueueText(SummaryOneDive).EnqueueText("""
            {
              "id": 28533490,
              "startTime": "2026-04-17T13:33:06+02:00",
              "totalTime": 6005.03,
              "profile": { "maxDepth": 14.415 }
            }
            """);
        var refs = await connector.ListDivesSinceAsync(SyncCursor.None);

        var dive = await connector.FetchProfileAsync(refs[0]);

        Assert.Empty(dive.Samples);
        Assert.Equal(14.415, dive.MaxDepthM);
        Assert.Null(dive.GasMix);
    }

    // ── configuration ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Configured_override_replaces_the_default_login_endpoint()
    {
        // A deploy-time override absorbs a Garmin-side change without a code release; a null override keeps
        // the observed constant, so every other test exercises the real default.
        await _consent.GrantAsync(ConnectorId);
        var connector = Create(new CloudSourceOptions { GarminLoginUrl = "https://override.example/login" });
        ScriptAuth();
        connector.ProvideCredentials("diver@example.com", Password);

        await connector.AuthenticateAsync();

        Assert.StartsWith("https://override.example/login?", _http.Requests[0].Url);
    }
}
