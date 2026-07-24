using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using Trimix.Contracts.Dtos;
using Trimix.Core.Abstractions;

namespace Trimix.Core.Connectors.Cloud;

/// <summary>
/// Reverse-engineered Oceanic+ (Huish Outdoors) personal-export source. Signs in to the diver's own Oceanic+
/// account (email/password → bearer token tied to the user id) and pulls the rich cloud dive records that the
/// HealthKit path cannot see: 15 s waypoints, gas mix, NDL, dive site, notes. This is
/// <see cref="LegalPosture.ToSRiskReverseEng"/> and <b>ships dark</b> (<c>EnabledByDefault=false</c>).
///
/// <para><b>Endpoints unpublished.</b> Research established Huish publishes no developer API — the cloud path
/// hits <em>unpublished</em> token-authenticated endpoints (the "Dive Converter" community tool does this). The
/// host/paths below are placeholders to be confirmed with legal/BD before the flag flips; the connector ships
/// dark so they are never called at runtime meanwhile. The clean Oceanic+ path is Apple HealthKit (iOS,
/// depth+temp only) — a separate branch. Unlike HealthKit, this cloud path also works on Android, hence
/// <see cref="Platforms.Both"/>.</para>
/// </summary>
public sealed class OceanicCloudSourceConnector(
    IHttpTransport http,
    ISecureTokenStore tokens,
    IConnectorConsentStore consent,
    CloudSourceOptions? options = null) : GatedCloudSourceConnector(http, tokens, consent)
{
    public const string Id = "oceanic_cloud";

    // UNPUBLISHED — placeholder host/paths pending legal/BD confirmation (see class remarks). Overridable via
    // CloudSourceOptions so the real endpoints are config-injected at deploy (never guessed in-source); a null
    // override keeps these placeholders, which are never called at runtime while the connector ships dark.
    private const string DefaultLoginUrl = "https://api.oceanicplus.com/v1/auth/login";
    private const string DefaultDivesUrl = "https://api.oceanicplus.com/v1/dives";

    private string LoginUrl => options?.OceanicLoginUrl ?? DefaultLoginUrl;
    private string DivesUrl => options?.OceanicDivesUrl ?? DefaultDivesUrl;

    public override ConnectorDescriptor Descriptor { get; } = new(
        ConnectorId: Id,
        Kind: ConnectorKind.CloudApi,
        Legal: LegalPosture.ToSRiskReverseEng,
        Fidelity: ProfileFidelity.FullProfile, // cloud record carries depth/temp/NDL + gas mix (no tank pressure).
        Auth: AuthModel.UserCredential,
        Platforms: Platforms.Both,
        EnabledByDefault: false);

    public override string DisplayName => "Oceanic+ (personal export)";

    protected override string Disclosure =>
        "This imports your own dives from your personal Oceanic+ account. It signs in with your Oceanic+ email " +
        "and password against Huish's private backend, which is NOT an official Oceanic integration and may " +
        "breach Oceanic+'s Terms of Service. On iPhone, Apple Health is the Terms-clean way to import your " +
        "Apple Watch dives (depth and temperature). Your password is used once to obtain an access token and is " +
        "never stored; only the token is kept, in secure device storage. You can disconnect at any time.";

    protected override async Task<string> ExchangeCredentialsForTokenAsync(ConnectorCredentials credentials, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new { email = credentials.Username, password = credentials.Password });
        var response = await Http.SendAsync(new HttpExchangeRequest(
            "POST", LoginUrl,
            new Dictionary<string, string>
            {
                ["Content-Type"] = "application/json",
                ["Accept"] = "application/json",
            },
            payload), ct).ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            throw new ConnectorAuthException("Oceanic+ sign-in failed — check your email and password.");
        }

        using var doc = JsonDocument.Parse(response.Body);
        if (!doc.RootElement.TryGetProperty("token", out var tokenEl) ||
            tokenEl.GetString() is not { Length: > 0 } token)
        {
            throw new ConnectorAuthException("Oceanic+ sign-in returned no token.");
        }

        return token;
    }

    public override async Task<IReadOnlyList<DiveRef>> ListDivesSinceAsync(SyncCursor cursor, CancellationToken ct = default)
    {
        var token = await RequireTokenAsync(ct).ConfigureAwait(false);
        var url = DivesUrl;
        if (cursor.Since is { } since)
        {
            url += $"?since={Uri.EscapeDataString(since.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))}";
        }

        var response = await Http.SendAsync(new HttpExchangeRequest("GET", url, Bearer(token)), ct).ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            throw new InvalidOperationException($"Oceanic+ dive list failed (HTTP {response.StatusCode}).");
        }

        var refs = new List<DiveRef>();
        using var doc = JsonDocument.Parse(response.Body);
        var dives = doc.RootElement.ValueKind == JsonValueKind.Array
            ? doc.RootElement
            : doc.RootElement.TryGetProperty("dives", out var divesEl) ? divesEl : default;
        if (dives.ValueKind != JsonValueKind.Array)
        {
            return refs;
        }

        foreach (var dive in dives.EnumerateArray())
        {
            var id = dive.TryGetProperty("id", out var idEl)
                ? idEl.ValueKind == JsonValueKind.Number ? idEl.GetInt64().ToString(CultureInfo.InvariantCulture) : idEl.GetString()
                : null;
            if (string.IsNullOrEmpty(id))
            {
                continue;
            }

            refs.Add(new DiveRef(
                Descriptor.ConnectorId,
                id,
                ParseIso(dive, "startTime"),
                dive.TryGetProperty("durationSeconds", out var durEl) && durEl.ValueKind == JsonValueKind.Number
                    ? durEl.GetInt32()
                    : 0));
        }

        return refs;
    }

    public override async Task<ImportedDive> FetchProfileAsync(DiveRef diveRef, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(diveRef);
        var token = await RequireTokenAsync(ct).ConfigureAwait(false);

        var response = await Http.SendAsync(new HttpExchangeRequest(
            "GET", $"{DivesUrl}/{Uri.EscapeDataString(diveRef.SourceId)}", Bearer(token)), ct).ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            throw new InvalidOperationException($"Oceanic+ dive fetch failed for {diveRef.SourceId} (HTTP {response.StatusCode}).");
        }

        using var doc = JsonDocument.Parse(response.Body);
        var dive = doc.RootElement;

        var samples = new List<ProfileSampleDto>();
        if (dive.TryGetProperty("samples", out var samplesEl) && samplesEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var s in samplesEl.EnumerateArray())
            {
                samples.Add(new ProfileSampleDto(
                    TSeconds: ReadInt(s, "offsetSeconds") ?? 0,
                    DepthM: ReadDouble(s, "depthMeters") ?? 0,
                    TempC: ReadDouble(s, "tempC"),
                    PpO2: ReadDouble(s, "ppO2"),
                    Ndl: ReadDouble(s, "ndlMinutes"),
                    CeilingM: ReadDouble(s, "ceilingMeters"),
                    AscentRateMPerMin: ReadDouble(s, "ascentRateMPerMin")));
            }
        }

        var cylinders = new List<CylinderDto>();
        if (dive.TryGetProperty("cylinders", out var cylsEl) && cylsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in cylsEl.EnumerateArray())
            {
                cylinders.Add(new CylinderDto(
                    O2: ReadDouble(c, "o2Percent"),
                    He: ReadDouble(c, "hePercent"),
                    StartBar: ReadDouble(c, "startBar"),
                    EndBar: ReadDouble(c, "endBar"),
                    VolumeL: ReadDouble(c, "volumeL")));
            }
        }

        return new ImportedDive(
            StartUtc: ParseIso(dive, "startTime"),
            DurationSec: ReadInt(dive, "durationSeconds") ?? diveRef.DurationSec,
            MaxDepthM: ReadDouble(dive, "maxDepthMeters") ?? 0,
            AvgDepthM: ReadDouble(dive, "avgDepthMeters"),
            MinTempC: ReadDouble(dive, "minWaterTempC"),
            GasMix: dive.TryGetProperty("gasMix", out var gasEl) ? gasEl.GetString() : null,
            Samples: samples,
            Cylinders: cylinders.Count > 0 ? cylinders : null);
    }

    private static DateTime ParseIso(JsonElement el, string prop)
    {
        if (el.TryGetProperty(prop, out var value) && value.GetString() is { Length: > 0 } s &&
            DateTime.TryParse(s, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
        }

        return default;
    }

    private static double? ReadDouble(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;

    private static int? ReadInt(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;
}
