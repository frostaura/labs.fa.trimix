using Trimix.Core.Abstractions;
using Trimix.Core.Connectors.Cloud;
using Xunit;

namespace Trimix.Core.Tests.Connectors.Cloud;

/// <summary>
/// The ScubaPro / Uwatec personal-export source, reading LogTRAK's Firebase backend (project
/// <c>logtrak2-production</c>): Firebase email/password sign-in → Firestore <c>dives</c> collection queried by
/// the diver's own <c>userId</c>. Fixtures are copied from the live capture recorded in
/// <c>docs/reverse-engineering/scubapro-logtrak-api.md</c> (dive <c>QcT3…</c>, Sodwana, 02 Nov 2024, air on a
/// Galileo T). Gating semantics are re-proven for every connector in <c>GuardrailTests</c>.
/// </summary>
public class ScubaProCloudSourceConnectorTests
{
    private readonly RecordedHttpTransport _http = new();
    private readonly InMemorySecureTokenStore _tokens = new();
    private readonly InMemoryConnectorConsentStore _consent = new();

    private const string TokenKey = "connector:scubapro:access_token";
    private const string Password = "logtrak-pass-456";
    private const string Uid = "5Pi2iolsrhNpP5WlOtzgJlbVQ6n1";

    private ScubaProCloudSourceConnector Create() => new(_http, _tokens, _consent);

    /// <summary>Firebase verifyPassword response — id token, rotating refresh token, and the uid the query filters on.</summary>
    private static string SignInResponse(string expiresIn = "3600") =>
        $"{{\"localId\":\"{Uid}\",\"idToken\":\"logtrak-id-xyz\",\"refreshToken\":\"logtrak-refresh-xyz\",\"expiresIn\":\"{expiresIn}\"}}";

    /// <summary>A Firestore <c>:runQuery</c> stream: two dives (real field shapes, arrays truncated to 3
    /// samples) plus a trailing document-less frame the reader must skip.</summary>
    private const string LogbookQueryResponse =
        "[" +
        "{\"document\":{\"name\":\"projects/logtrak2-production/databases/(default)/documents/dives/QcT3yo7GSR4gPBPz4BoM\",\"fields\":{" +
            "\"userId\":{\"stringValue\":\"" + Uid + "\"}," +
            "\"startTime\":{\"stringValue\":\"2024-11-02T07:37:50.000Z\"}," +
            "\"endTime\":{\"stringValue\":\"2024-11-02T08:33:50.000Z\"}," +
            "\"depthMetersMax\":{\"doubleValue\":17.33}," +
            "\"depthMetersAvg\":{\"doubleValue\":11.02}," +
            "\"waterTempCelsiusMin\":{\"integerValue\":\"22\"}," +
            "\"waterTempCelsiusMax\":{\"doubleValue\":27.2}," +
            "\"diveMode\":{\"stringValue\":\"scuba\"}," +
            "\"saltwaterCalibrated\":{\"booleanValue\":true}," +
            "\"cnsO2Before\":{\"integerValue\":\"0\"},\"cnsO2After\":{\"integerValue\":\"0\"}," +
            "\"depthMetersValues\":{\"stringValue\":\"[0,2.595,3.707]\"}," +
            "\"waterTempCelsiusValues\":{\"stringValue\":\"[27.2,27.2,27.2]\"}," +
            "\"heartRateBpmValues\":{\"stringValue\":\"[]\"}," +
            "\"tankData\":{\"mapValue\":{\"fields\":{" +
                "\"tank1\":{\"mapValue\":{\"fields\":{\"o2Mixture\":{\"stringValue\":\"21\"},\"heMixture\":{\"nullValue\":null}," +
                    "\"startPressure\":{\"doubleValue\":212.84},\"endPressure\":{\"doubleValue\":89.67},\"size\":{\"nullValue\":null}}}}," +
                "\"tank2\":{\"nullValue\":null}}}}," +
            "\"tankPressureValues\":{\"mapValue\":{\"fields\":{\"tank1\":{\"stringValue\":\"[212.75,212.5,211.75]\"}}}}" +
        "}}}," +
        "{\"document\":{\"name\":\"projects/logtrak2-production/databases/(default)/documents/dives/EsqWD9k04ShBAN62OFiP\",\"fields\":{" +
            "\"userId\":{\"stringValue\":\"" + Uid + "\"}," +
            "\"startTime\":{\"stringValue\":\"2024-10-15T06:00:00.000Z\"}," +
            "\"endTime\":{\"stringValue\":\"2024-10-15T06:45:00.000Z\"}," +
            "\"depthMetersMax\":{\"doubleValue\":30.5}," +
            "\"diveMode\":{\"stringValue\":\"scuba\"}," +
            "\"depthMetersValues\":{\"stringValue\":\"[0,15,30.5]\"}}}}," +
        "{\"readTime\":\"2026-08-07T09:00:00Z\"}" +
        "]";

    private async Task GrantAndAuthenticateAsync()
    {
        await _consent.GrantAsync("scubapro");
        _http.EnqueueText(SignInResponse());
        var connector = Create();
        connector.ProvideCredentials("diver@example.com", Password);
        await connector.AuthenticateAsync();
    }

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
        Assert.Contains("LogTRAK", result.Message, StringComparison.Ordinal); // truthful backend disclosure
        Assert.Empty(_http.Requests);
        Assert.Empty(_tokens.Values);
    }

    [Fact]
    public async Task Authenticate_exchanges_firebase_credentials_and_stores_only_the_token_and_uid()
    {
        await GrantAndAuthenticateAsync();

        // The stored envelope carries the id token AND the uid (the query filter) but never the password.
        var stored = _tokens.Values[TokenKey];
        Assert.Contains("logtrak-id-xyz", stored, StringComparison.Ordinal);
        Assert.Contains(Uid, stored, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, stored, StringComparison.Ordinal);

        // The sign-in call must carry the bundle header that unblocks the app-restricted API key.
        Assert.Contains("verifyPassword", _http.Requests[0].Url, StringComparison.Ordinal);
        Assert.Equal("com.uwatec.LogTrak2", _http.Requests[0].Headers["X-Ios-Bundle-Identifier"]);
    }

    [Fact]
    public async Task List_returns_the_divers_dives_via_a_userId_filtered_firestore_query()
    {
        await GrantAndAuthenticateAsync();
        _http.EnqueueText(LogbookQueryResponse);
        var connector = Create();
        // (a fresh instance would re-auth; reuse via the token already in the store)

        var refs = await connector.ListDivesSinceAsync(SyncCursor.None);

        Assert.Equal(["QcT3yo7GSR4gPBPz4BoM", "EsqWD9k04ShBAN62OFiP"], refs.Select(r => r.SourceId).ToArray());
        Assert.All(refs, r => Assert.Equal("scubapro", r.ConnectorId));

        // The query is a Firestore runQuery filtered to the diver's own userId (an unfiltered list is denied).
        var query = _http.Requests[^1];
        Assert.EndsWith(":runQuery", query.Url, StringComparison.Ordinal);
        Assert.Contains("\"fieldPath\":\"userId\"", query.Body, StringComparison.Ordinal);
        Assert.Contains(Uid, query.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_applies_the_since_cursor_client_side()
    {
        await GrantAndAuthenticateAsync();
        _http.EnqueueText(LogbookQueryResponse);
        var connector = Create();

        // Only the November dive is newer than this cursor; the October dive must be dropped.
        var refs = await connector.ListDivesSinceAsync(new SyncCursor(new DateTime(2024, 11, 1, 0, 0, 0, DateTimeKind.Utc)));

        Assert.Equal(["QcT3yo7GSR4gPBPz4BoM"], refs.Select(r => r.SourceId).ToArray());
    }

    [Fact]
    public async Task Fetch_maps_the_full_profile_cylinders_and_gas_from_the_cached_document()
    {
        await GrantAndAuthenticateAsync();
        _http.EnqueueText(LogbookQueryResponse);
        var connector = Create();
        var refs = await connector.ListDivesSinceAsync(SyncCursor.None);
        var requestsAfterList = _http.Requests.Count;

        var dive = await connector.FetchProfileAsync(refs[0]);

        // Summary
        Assert.Equal(17.33, dive.MaxDepthM);
        Assert.Equal(11.02, dive.AvgDepthM);
        Assert.Equal(22, dive.MinTempC);
        Assert.Equal(3360, dive.DurationSec); // 07:37:50 → 08:33:50
        Assert.Equal("AIR", dive.GasMix);     // o2Mixture "21" reads as AIR, not EAN21
        Assert.Equal("Scuba", dive.DiveTypeLabel);
        Assert.Equal("Salt", dive.WaterType);

        // Profile: three samples, cadence derived from duration/(n-1) = 1680 s.
        Assert.Equal(3, dive.Samples.Count);
        Assert.Equal([0, 1680, 3360], dive.Samples.Select(s => s.TSeconds).ToArray());
        Assert.Equal(3.707, dive.Samples[2].DepthM); // last of the truncated fixture profile
        Assert.Equal(27.2, dive.Samples[0].TempC);
        Assert.Equal(212.75, dive.Samples[0].TankPressureBar); // transmitter tank pressure, per sample
        Assert.Null(dive.Samples[0].HeartRate);                // "[]" channel → no HR

        // Cylinder
        var cylinder = Assert.Single(dive.Cylinders!);
        Assert.Equal(21, cylinder.O2);
        Assert.Equal(212.84, cylinder.StartBar);
        Assert.Equal(89.67, cylinder.EndBar);

        // Environment + exposure the connector could populate.
        Assert.Equal(27.2, dive.Environment!.MaxTempC);
        Assert.Equal(0, dive.Exposure!.CnsEndPercent);

        // The profile was already in the list document — Fetch must NOT make another network call.
        Assert.Equal(requestsAfterList, _http.Requests.Count);
    }

    [Fact]
    public async Task Expired_session_is_refreshed_before_listing()
    {
        await _consent.GrantAsync("scubapro");
        _http.EnqueueText(SignInResponse(expiresIn: "0")); // already expired on arrival
        var connector = Create();
        connector.ProvideCredentials("diver@example.com", Password);
        await connector.AuthenticateAsync();

        // Next read must refresh via securetoken, then run the query with the fresh token.
        _http.EnqueueText("{\"id_token\":\"logtrak-id-2\",\"refresh_token\":\"logtrak-refresh-2\",\"expires_in\":\"3600\",\"user_id\":\"" + Uid + "\"}");
        _http.EnqueueText(LogbookQueryResponse);

        var refs = await connector.ListDivesSinceAsync(SyncCursor.None);

        Assert.Equal(2, refs.Count);
        Assert.Contains(_http.Requests, r => r.Url.Contains("securetoken", StringComparison.Ordinal));
        // The rotated refresh token was persisted back.
        Assert.Contains("logtrak-refresh-2", _tokens.Values[TokenKey], StringComparison.Ordinal);
        // The query used the refreshed id token.
        Assert.Equal("Bearer logtrak-id-2", _http.Requests[^1].Headers["Authorization"]);
    }
}
