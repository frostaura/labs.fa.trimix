using Trimix.Core.Abstractions;
using Trimix.Core.Connectors.Cloud;
using Xunit;

namespace Trimix.Core.Tests.Connectors.Cloud;

/// <summary>
/// The Oceanic+ (Huish Outdoors) personal-export (reverse-eng, gated) source connector. Unlike the Garmin/Suunto
/// FIT paths, Oceanic+ exposes a rich token-authenticated JSON cloud record, so this exercises the full
/// login → token → list → fetch flow offline against <see cref="RecordedHttpTransport"/> and asserts the JSON is
/// mapped into an <see cref="ImportedDive"/> with per-sample channels (depth/temp/ppO2/NDL/ceiling/ascent) plus
/// gas cylinders. Guardrails are pinned here and cross-checked for all three cloud connectors in
/// <see cref="GuardrailTests"/>.
/// </summary>
public class OceanicCloudSourceConnectorTests
{
    private readonly RecordedHttpTransport _http = new();
    private readonly InMemorySecureTokenStore _tokens = new();
    private readonly InMemoryConnectorConsentStore _consent = new();

    private const string TokenKey = "connector:oceanic_cloud:access_token";
    private const string Password = "oceanic-pass-321";

    private OceanicCloudSourceConnector Create() => new(_http, _tokens, _consent);

    [Fact]
    public void Descriptor_ships_dark_full_profile_both_platforms()
    {
        var d = Create().Descriptor;

        Assert.Equal("oceanic_cloud", d.ConnectorId);
        Assert.Equal(ConnectorKind.CloudApi, d.Kind);
        Assert.Equal(LegalPosture.ToSRiskReverseEng, d.Legal);
        Assert.False(d.EnabledByDefault);
        Assert.Equal(AuthModel.UserCredential, d.Auth);
        Assert.Equal(ProfileFidelity.FullProfile, d.Fidelity);
        Assert.Equal(Platforms.Both, d.Platforms);
    }

    [Fact]
    public async Task Authenticate_without_consent_does_zero_network_and_returns_disclosure()
    {
        var connector = Create();
        connector.ProvideCredentials("diver@example.com", Password);

        var result = await connector.AuthenticateAsync();

        Assert.Equal(ConnectorAuthStatus.NeedsInteraction, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
        Assert.Contains("Terms of Service", result.Message); // truthful ToS-risk disclosure
        Assert.Empty(_http.Requests); // guardrail (iii): no I/O before consent
        Assert.Empty(_tokens.Values);
    }

    [Fact]
    public async Task Authenticate_exchanges_credentials_for_token_and_never_stores_the_password()
    {
        await _consent.GrantAsync("oceanic_cloud");
        _http.EnqueueText("{\"token\":\"oceanic-tok-xyz\",\"userId\":\"u-42\"}");
        var connector = Create();
        connector.ProvideCredentials("diver@example.com", Password);

        var result = await connector.AuthenticateAsync();

        Assert.Equal(ConnectorAuthStatus.Granted, result.Status);
        // Only the exchanged token is persisted.
        Assert.Equal("oceanic-tok-xyz", _tokens.Values[TokenKey]);
        // Guardrail (ii): the raw password is NEVER written to the secure token store.
        Assert.DoesNotContain(_tokens.Values.Values, v => v.Contains(Password, StringComparison.Ordinal));
        // The password was sent exactly once — only in the sign-in POST body.
        Assert.Single(_http.Requests);
        Assert.Single(_http.Requests, r => r.Body is not null && r.Body.Contains(Password, StringComparison.Ordinal));
    }

    [Fact]
    public async Task List_then_fetch_maps_rich_samples_and_cylinders_from_the_cloud_json()
    {
        await _consent.GrantAsync("oceanic_cloud");
        _http.EnqueueText("{\"token\":\"oceanic-tok-xyz\"}");
        var connector = Create();
        connector.ProvideCredentials("diver@example.com", Password);
        await connector.AuthenticateAsync();

        // List: numeric id (must stringify), ISO start, duration seconds. Wrapped in { "dives": [...] }.
        _http.EnqueueText(
            "{\"dives\":[{\"id\":9981,\"startTime\":\"2026-06-14T09:30:00Z\",\"durationSeconds\":3120}]}");
        // Fetch: rich single-dive record — summary + per-sample channels + a gas cylinder.
        _http.EnqueueText(
            "{\"startTime\":\"2026-06-14T09:30:00Z\",\"durationSeconds\":3120,\"maxDepthMeters\":32.4," +
            "\"avgDepthMeters\":18.7,\"minWaterTempC\":14.2,\"gasMix\":\"EAN32\"," +
            "\"samples\":[" +
            "{\"offsetSeconds\":0,\"depthMeters\":0.0,\"tempC\":21.0,\"ppO2\":0.21,\"ndlMinutes\":99," +
            "\"ceilingMeters\":0,\"ascentRateMPerMin\":0}," +
            "{\"offsetSeconds\":900,\"depthMeters\":32.4,\"tempC\":14.2,\"ppO2\":1.05,\"ndlMinutes\":8," +
            "\"ceilingMeters\":0,\"ascentRateMPerMin\":9.5}]," +
            "\"cylinders\":[{\"o2Percent\":32,\"hePercent\":0,\"startBar\":230,\"endBar\":80,\"volumeL\":12}]}");

        var refs = await connector.ListDivesSinceAsync(SyncCursor.None);
        Assert.Single(refs);
        Assert.Equal("9981", refs[0].SourceId);
        Assert.Equal(new DateTime(2026, 6, 14, 9, 30, 0, DateTimeKind.Utc), refs[0].StartUtc);
        Assert.Equal(3120, refs[0].DurationSec);

        var dive = await connector.FetchProfileAsync(refs[0]);

        // Dive summary.
        Assert.Equal(new DateTime(2026, 6, 14, 9, 30, 0, DateTimeKind.Utc), dive.StartUtc);
        Assert.Equal(3120, dive.DurationSec);
        Assert.Equal(32.4, dive.MaxDepthM);
        Assert.Equal(18.7, dive.AvgDepthM);
        Assert.Equal(14.2, dive.MinTempC);
        Assert.Equal("EAN32", dive.GasMix);

        // Per-sample rich channels mapped; channels the Oceanic+ record does not carry stay null.
        Assert.Equal(2, dive.Samples.Count);
        var bottom = dive.Samples[1];
        Assert.Equal(900, bottom.TSeconds);
        Assert.Equal(32.4, bottom.DepthM);
        Assert.Equal(14.2, bottom.TempC);
        Assert.Equal(1.05, bottom.PpO2);
        Assert.Equal(8, bottom.Ndl);
        Assert.Equal(9.5, bottom.AscentRateMPerMin);
        Assert.Null(bottom.TankPressureBar); // Oceanic+ cloud record carries no transmitter tank pressure.

        // Gas cylinder mapped.
        Assert.NotNull(dive.Cylinders);
        var cyl = Assert.Single(dive.Cylinders!);
        Assert.Equal(32, cyl.O2);
        Assert.Equal(0, cyl.He);
        Assert.Equal(230, cyl.StartBar);
        Assert.Equal(80, cyl.EndBar);
        Assert.Equal(12, cyl.VolumeL);
    }

    [Fact]
    public async Task List_before_authenticate_throws_and_hits_no_network()
    {
        await _consent.GrantAsync("oceanic_cloud");
        var connector = Create();

        await Assert.ThrowsAsync<InvalidOperationException>(() => connector.ListDivesSinceAsync(SyncCursor.None));
        Assert.Empty(_http.Requests);
    }

    [Fact]
    public async Task Configured_override_url_replaces_the_default_login_endpoint()
    {
        // Deploy-time override injects the (currently unpublished) real login host; a null override keeps the
        // placeholder, which is never called at runtime while the connector ships dark.
        await _consent.GrantAsync("oceanic_cloud");
        var options = new CloudSourceOptions { OceanicLoginUrl = "https://override.example/oceanic/login" };
        var connector = new OceanicCloudSourceConnector(_http, _tokens, _consent, options);
        _http.EnqueueText("{\"token\":\"oceanic-tok-xyz\"}");
        connector.ProvideCredentials("diver@example.com", Password);

        await connector.AuthenticateAsync();

        Assert.Equal("https://override.example/oceanic/login", _http.Requests[0].Url);
    }
}
