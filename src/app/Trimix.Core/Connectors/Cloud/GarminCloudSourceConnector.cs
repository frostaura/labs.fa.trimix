using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Trimix.Contracts.Dtos;
using Trimix.Core.Abstractions;

namespace Trimix.Core.Connectors.Cloud;

/// <summary>
/// Reverse-engineered Garmin Dive personal-export source. Reads the diver's own dives from their Garmin
/// account by replicating the Garmin Dive iOS app's private API, observed on the wire and verified live
/// (see <c>docs/reverse-engineering/garmin-dive-api.md</c> — that document is the contract this class implements).
/// This is <see cref="LegalPosture.ToSRiskReverseEng"/>: it holds the diver's Garmin credentials for a single
/// token exchange, breaches Garmin's ToS, and is reachable only behind explicit per-user consent + disclosure.
///
/// <para><b>Flow.</b> <c>POST sso.garmin.com/mobile/api/login</c> (JSON) → <c>serviceTicketId</c> →
/// <c>POST diauth.garmin.com/di-oauth2-service/oauth/token</c> (ticket grant) → bearer JWT →
/// <c>gcs.garmin.com/diving/v1/dive/summary</c> for the list → <c>/diving/v1/dive/detail/{id}</c> for the
/// full 1 Hz sample profile.</para>
///
/// <para><b>No FIT decoding.</b> Garmin's diving API returns the per-sample profile as JSON, so this connector
/// does not download or parse <c>.FIT</c> at all — <see cref="ProfileFidelity.FullProfile"/> comes straight
/// from the <c>records</c> block. (<see cref="IFitActivityParser"/> remains in use by other connectors.)</para>
///
/// <para><b>MFA is not supported on this path.</b> Garmin signals a required second factor via
/// <c>customerMfaInfo</c> on the login response; a pure-HTTP client cannot satisfy it. Such an account is
/// reported as needing interaction, pending the planned WebView login leg.</para>
/// </summary>
public sealed class GarminCloudSourceConnector(
    IHttpTransport http,
    ISecureTokenStore tokens,
    IConnectorConsentStore consent,
    CloudSourceOptions? options = null) : GatedCloudSourceConnector(http, tokens, consent)
{
    public const string Id = "garmin_connect";

    // Observed verbatim from Garmin Dive 3.6.1 (iOS 27.0). Client ids differ per leg by design.
    private const string SsoClientId = "DIVE_IOS_DARK";
    private const string OAuthClientId = "DIVE_MOBILE_IOS_DI";
    private const string DefaultServiceUrl = "https://mobile.integration.garmin.com/dive/ios";
    private const string TicketGrantType =
        "https://connectapi.garmin.com/di-oauth2-service/oauth/grant/service_ticket";

    private const string DefaultLoginUrl = "https://sso.garmin.com/mobile/api/login";
    private const string DefaultTokenUrl = "https://diauth.garmin.com/di-oauth2-service/oauth/token";
    private const string DefaultSummaryUrl = "https://gcs.garmin.com/diving/v1/dive/summary";
    private const string DefaultDetailUrl = "https://gcs.garmin.com/diving/v1/dive/detail";

    /// <summary>The app's own UA on the API legs; the WebView's UA on the SSO leg.</summary>
    private const string WebUserAgent =
        "Mozilla/5.0 (iPhone; CPU iPhone OS 18_7 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Mobile/15E148";
    private const string ApiUserAgent = "Dive/3.6.1 (com.garmin.Dive; build:1; iOS 27.0.0) Alamofire/5.9.1";
    private const string AppVersion = "3.6.1";

    /// <summary>Garmin's page cap; the summary endpoint is paged, not incremental.</summary>
    private const int PageSize = 100;

    /// <summary>Refresh this far before nominal expiry rather than racing it.</summary>
    private static readonly TimeSpan ExpirySkew = TimeSpan.FromMinutes(5);

    private readonly ISecureTokenStore _tokens = tokens;

    private string LoginUrl => options?.GarminLoginUrl ?? DefaultLoginUrl;
    private string TokenUrl => options?.GarminTokenUrl ?? DefaultTokenUrl;
    private string SummaryUrl => options?.GarminDiveSummaryUrl ?? DefaultSummaryUrl;
    private string DetailUrl => options?.GarminDiveDetailUrl ?? DefaultDetailUrl;
    private string ServiceUrl => options?.GarminServiceUrl ?? DefaultServiceUrl;

    public override ConnectorDescriptor Descriptor { get; } = new(
        ConnectorId: Id,
        Kind: ConnectorKind.CloudApi,
        Legal: LegalPosture.ToSRiskReverseEng,
        Fidelity: ProfileFidelity.FullProfile, // 1 Hz depth/temp/NDL/CNS/PO2 from the records block.
        Auth: AuthModel.UserCredential,
        Platforms: Platforms.Both,
        EnabledByDefault: false);

    public override string DisplayName => "Garmin Dive (personal export)";

    protected override string Disclosure =>
        "This imports your own dives from your personal Garmin account. It signs in with your Garmin " +
        "username and password using Garmin's private app login — this is NOT an official Garmin integration " +
        "and may breach Garmin's Terms of Service. Your password is used once to obtain an access token and is " +
        "never stored; only the access token is kept, in secure device storage. You can disconnect at any time.";

    /// <summary>
    /// Signs in and exchanges the resulting single-use service ticket for a bearer JWT. Returns a JSON
    /// envelope (access token + refresh token + absolute expiry) rather than a bare token, so the 30-day
    /// refresh token survives alongside the 24-hour access token in the one secure-store slot the base
    /// class manages. The password is used here and never written anywhere.
    /// </summary>
    protected override async Task<string> ExchangeCredentialsForTokenAsync(
        ConnectorCredentials credentials, CancellationToken ct)
    {
        var ticket = await SignInForServiceTicketAsync(credentials, ct).ConfigureAwait(false);
        var envelope = await ExchangeTicketAsync(ticket, ct).ConfigureAwait(false);
        return envelope.Serialize();
    }

    /// <summary>Step 1 — JSON credential POST returning a single-use <c>ST-…-sso</c> service ticket.</summary>
    private async Task<string> SignInForServiceTicketAsync(ConnectorCredentials credentials, CancellationToken ct)
    {
        var url = $"{LoginUrl}?clientId={SsoClientId}&locale=en-US&service={Uri.EscapeDataString(ServiceUrl)}";
        var payload = JsonSerializer.Serialize(new
        {
            username = credentials.Username,
            password = credentials.Password,
            rememberMe = false,
            captchaToken = string.Empty,
        });

        var response = await Http.SendAsync(new HttpExchangeRequest(
            "POST", url,
            new Dictionary<string, string>
            {
                ["Content-Type"] = "application/json",
                ["Accept"] = "application/json, text/plain, */*",
                ["User-Agent"] = WebUserAgent,
                ["Origin"] = "https://sso.garmin.com",
            },
            payload), ct).ConfigureAwait(false);

        if (!response.IsSuccess)
        {
            // Distinguishing these matters: telling a diver their password is wrong when Garmin merely
            // throttled us sends them off changing a password that was never the problem. Garmin does
            // rate-limit repeated sign-ins from one address (observed live).
            throw new ConnectorAuthException(response.StatusCode switch
            {
                429 => "Garmin is temporarily rate-limiting sign-in attempts. Wait a few minutes and try again.",
                401 or 403 => "Garmin rejected your username or password.",
                >= 500 => $"Garmin's sign-in service is unavailable right now (HTTP {response.StatusCode}). Try again shortly.",
                _ => $"Garmin sign-in failed (HTTP {response.StatusCode}).",
            });
        }

        using var doc = ParseJson(response.Body, "Garmin sign-in");
        var root = doc.RootElement;

        // Garmin reports a required second factor here. A pure-HTTP client cannot answer it.
        if (root.TryGetProperty("customerMfaInfo", out var mfa) && mfa.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
        {
            throw new ConnectorAuthException(
                "This Garmin account has multi-factor authentication enabled, which this import path cannot " +
                "complete yet. Disable MFA on Garmin, or wait for in-app Garmin sign-in.");
        }

        if (root.TryGetProperty("responseStatus", out var status)
            && status.TryGetProperty("type", out var statusType)
            && statusType.GetString() is { Length: > 0 } type
            && !type.Equals("SUCCESSFUL", StringComparison.OrdinalIgnoreCase))
        {
            var message = status.TryGetProperty("message", out var msg) ? msg.GetString() : null;
            throw new ConnectorAuthException(
                string.IsNullOrWhiteSpace(message)
                    ? $"Garmin rejected the sign-in ({type})."
                    : $"Garmin rejected the sign-in: {message}");
        }

        if (!root.TryGetProperty("serviceTicketId", out var ticketEl)
            || ticketEl.GetString() is not { Length: > 0 } ticket)
        {
            throw new ConnectorAuthException(
                "Garmin sign-in returned no service ticket (login rejected, or the flow changed).");
        }

        return ticket;
    }

    /// <summary>Step 2 — exchange the service ticket for the bearer JWT that authorizes the diving API.</summary>
    private async Task<GarminTokenEnvelope> ExchangeTicketAsync(string serviceTicket, CancellationToken ct)
    {
        var form = $"grant_type={Uri.EscapeDataString(TicketGrantType)}"
                 + $"&service_ticket={Uri.EscapeDataString(serviceTicket)}"
                 + $"&service_url={Uri.EscapeDataString(ServiceUrl)}"
                 + $"&client_id={OAuthClientId}";

        return await PostTokenFormAsync(form, "Garmin token exchange", ct).ConfigureAwait(false);
    }

    private async Task<GarminTokenEnvelope> PostTokenFormAsync(string form, string what, CancellationToken ct)
    {
        // Basic auth is the client id with an EMPTY secret.
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{OAuthClientId}:"));

        var response = await Http.SendAsync(new HttpExchangeRequest(
            "POST", TokenUrl,
            new Dictionary<string, string>
            {
                ["Content-Type"] = "application/x-www-form-urlencoded",
                ["Accept"] = "*/*",
                ["Authorization"] = $"Basic {basic}",
                ["User-Agent"] = "DIVE-iOS-3.6.1.1",
                ["garmin-client-platform"] = "iOS",
                ["x-app-ver"] = AppVersion,
                ["x-lang"] = "en",
            },
            form), ct).ConfigureAwait(false);

        if (!response.IsSuccess)
        {
            throw new ConnectorAuthException($"{what} failed (HTTP {response.StatusCode}).");
        }

        using var doc = ParseJson(response.Body, what);
        var root = doc.RootElement;

        if (!root.TryGetProperty("access_token", out var accessEl)
            || accessEl.GetString() is not { Length: > 0 } access)
        {
            throw new ConnectorAuthException($"{what} returned no access token.");
        }

        var refresh = root.TryGetProperty("refresh_token", out var refreshEl) ? refreshEl.GetString() : null;
        var lifetime = root.TryGetProperty("expires_in", out var expiresEl) && expiresEl.TryGetInt64(out var seconds)
            ? TimeSpan.FromSeconds(seconds)
            : TimeSpan.FromHours(24);

        return new GarminTokenEnvelope(access, refresh, DateTimeOffset.UtcNow.Add(lifetime));
    }

    /// <summary>
    /// Returns a usable access token, refreshing first if the stored one has expired.
    ///
    /// <para><b>The refresh call is unproven.</b> A refresh was never observed on the wire — only the
    /// <c>refresh_token</c> it returns — so the standard <c>grant_type=refresh_token</c> shape is attempted
    /// on the same endpoint. If Garmin rejects it, the stored token is cleared and the diver is asked to sign
    /// in again, which is correct behaviour rather than a silent failure.</para>
    /// </summary>
    private async Task<string> RequireAccessTokenAsync(CancellationToken ct)
    {
        var stored = await RequireTokenAsync(ct).ConfigureAwait(false);
        var envelope = GarminTokenEnvelope.TryParse(stored);

        if (envelope is null)
        {
            // A bare token from an earlier build: usable, but its expiry is unknown.
            return stored;
        }

        if (DateTimeOffset.UtcNow < envelope.ExpiresAt - ExpirySkew)
        {
            return envelope.AccessToken;
        }

        if (string.IsNullOrEmpty(envelope.RefreshToken))
        {
            await _tokens.RemoveAsync(TokenKey, ct).ConfigureAwait(false);
            throw new ConnectorAuthException("Your Garmin session expired. Please sign in again.");
        }

        try
        {
            var refreshed = await PostTokenFormAsync(
                $"grant_type=refresh_token&refresh_token={Uri.EscapeDataString(envelope.RefreshToken)}"
                + $"&client_id={OAuthClientId}",
                "Garmin token refresh", ct).ConfigureAwait(false);

            // Garmin may omit a rotated refresh token; keep the existing one when it does.
            var merged = refreshed.RefreshToken is null
                ? refreshed with { RefreshToken = envelope.RefreshToken }
                : refreshed;

            await _tokens.SetAsync(TokenKey, merged.Serialize(), ct).ConfigureAwait(false);
            return merged.AccessToken;
        }
        catch (ConnectorAuthException)
        {
            await _tokens.RemoveAsync(TokenKey, ct).ConfigureAwait(false);
            throw new ConnectorAuthException("Your Garmin session expired. Please sign in again.");
        }
    }

    /// <summary>
    /// Lists the diver's dives, newest first. The summary endpoint is <b>paged, not incremental</b> — no
    /// server-side "since" parameter exists — so <see cref="SyncCursor.Since"/> is applied client-side and
    /// paging stops as soon as a page falls entirely before the cursor.
    /// </summary>
    public override async Task<IReadOnlyList<DiveRef>> ListDivesSinceAsync(
        SyncCursor cursor, CancellationToken ct = default)
    {
        var token = await RequireAccessTokenAsync(ct).ConfigureAwait(false);
        var refs = new List<DiveRef>();

        for (var page = 0; ; page++)
        {
            var url = $"{SummaryUrl}?requestedPage={page}&resultsPerPage={PageSize}";
            var response = await Http.SendAsync(new HttpExchangeRequest("GET", url, DiveApiHeaders(token)), ct)
                .ConfigureAwait(false);
            if (!response.IsSuccess)
            {
                throw new InvalidOperationException($"Garmin dive list failed (HTTP {response.StatusCode}).");
            }

            using var doc = ParseJson(response.Body, "Garmin dive list");
            if (!doc.RootElement.TryGetProperty("diveActivities", out var activities)
                || activities.ValueKind != JsonValueKind.Array)
            {
                break;
            }

            var countThisPage = 0;
            var anyKept = false;
            foreach (var dive in activities.EnumerateArray())
            {
                countThisPage++;
                var id = ReadId(dive);
                if (id is null)
                {
                    continue;
                }

                var start = ReadStartUtc(dive);
                if (cursor.Since is { } since && start < since)
                {
                    continue;
                }

                anyKept = true;
                var duration = dive.TryGetProperty("totalTime", out var totalEl)
                    && totalEl.ValueKind == JsonValueKind.Number
                        ? (int)Math.Round(totalEl.GetDouble())
                        : 0;
                var label = dive.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;

                refs.Add(new DiveRef(Descriptor.ConnectorId, id, start, duration, label));
            }

            var total = doc.RootElement.TryGetProperty("totalCount", out var totalCountEl)
                && totalCountEl.TryGetInt32(out var t) ? t : 0;

            // Stop at the end of the collection, on an empty page, or once a whole page predates the cursor
            // (the feed is newest-first, so nothing older can follow).
            if (countThisPage == 0 || (page + 1) * PageSize >= total || (cursor.Since is not null && !anyKept))
            {
                break;
            }
        }

        return refs;
    }

    /// <summary>
    /// Fetches one dive's full detail and maps its columnar <c>records</c> block to
    /// <see cref="ProfileSampleDto"/>s. Samples are read <b>by column key</b>, never by index — the schema is
    /// self-describing precisely so Garmin can add columns.
    /// </summary>
    public override async Task<ImportedDive> FetchProfileAsync(DiveRef diveRef, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(diveRef);
        var token = await RequireAccessTokenAsync(ct).ConfigureAwait(false);

        var response = await Http.SendAsync(new HttpExchangeRequest(
            "GET", $"{DetailUrl}/{Uri.EscapeDataString(diveRef.SourceId)}", DiveApiHeaders(token)), ct)
            .ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            throw new InvalidOperationException(
                $"Garmin dive detail failed for {diveRef.SourceId} (HTTP {response.StatusCode}).");
        }

        using var doc = ParseJson(response.Body, "Garmin dive detail");
        var root = doc.RootElement;

        var startUtc = ReadStartUtc(root) is var parsed && parsed != default ? parsed : diveRef.StartUtc;
        var samples = ReadSamples(root, startUtc);

        var profile = root.TryGetProperty("profile", out var profileEl) ? profileEl : default;
        var environment = root.TryGetProperty("environment", out var envEl) ? envEl : default;

        var maxDepth = ReadDouble(profile, "maxDepth")
                       ?? (samples.Count > 0 ? samples.Max(s => s.DepthM) : 0d);
        var duration = root.TryGetProperty("totalTime", out var totalEl) && totalEl.ValueKind == JsonValueKind.Number
            ? (int)Math.Round(totalEl.GetDouble())
            : diveRef.DurationSec;

        var cylinders = ReadCylinders(root);
        var entryLoc = root.TryGetProperty("location", out var location)
            && location.TryGetProperty("entryLoc", out var entry) ? entry : default;

        return new ImportedDive(
            StartUtc: startUtc,
            DurationSec: duration,
            MaxDepthM: maxDepth,
            AvgDepthM: ReadDouble(profile, "averageDepth"),
            MinTempC: ReadDouble(environment, "minTemperature"),
            GasMix: DescribeGasMix(cylinders),
            Samples: samples,
            Cylinders: cylinders.Count > 0 ? cylinders : null,
            DiveNumber: ReadInt(root, "number"),
            BottomTimeSec: ReadDouble(profile, "bottomTime") is { } bottom ? (int)Math.Round(bottom) : null,
            SurfaceIntervalSec: ReadDouble(profile, "surfaceInterval") is { } si ? (int)Math.Round(si) : null,
            // Garmin exposes the deco flag on the dive SUMMARY, not on detail; absent here means unknown,
            // never "no deco".
            IsDeco: ReadBool(root, "isDeco") ?? ReadBool(root, "deco"),
            DiveTypeLabel: Humanize(ReadString(root, "diveType")),
            TimezoneId: ReadString(root, "timezone"),
            WaterType: Humanize(ReadString(environment, "waterType")),
            Environment: new DiveEnvironmentDto(
                MaxTempC: ReadDouble(environment, "maxTemperature"),
                AvgTempC: ReadDouble(environment, "avgTemperature"),
                WaterDensityKgM3: ReadDouble(environment, "waterDensity"),
                EntryLatitude: ReadDouble(entryLoc, "latitude"),
                EntryLongitude: ReadDouble(entryLoc, "longitude")),
            Exposure: ReadExposure(root),
            Computer: ReadComputer(root),
            Alerts: ReadAlerts(root, startUtc),
            Tags: ReadTags(root));
    }

    /// <summary>Maps the columnar <c>records</c> block into samples, resolving columns by key.</summary>
    private static List<ProfileSampleDto> ReadSamples(JsonElement root, DateTime startUtc)
    {
        var samples = new List<ProfileSampleDto>();
        if (!root.TryGetProperty("records", out var records) || records.ValueKind != JsonValueKind.Object)
        {
            return samples;
        }

        // recordMetaDatas: [{ position, key }] — the column schema.
        var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (records.TryGetProperty("recordMetaDatas", out var metas) && metas.ValueKind == JsonValueKind.Array)
        {
            foreach (var meta in metas.EnumerateArray())
            {
                if (meta.TryGetProperty("key", out var keyEl) && keyEl.GetString() is { Length: > 0 } key
                    && meta.TryGetProperty("position", out var posEl) && posEl.TryGetInt32(out var pos))
                {
                    columns[key] = pos;
                }
            }
        }

        if (columns.Count == 0 || !records.TryGetProperty("recordData", out var rows)
            || rows.ValueKind != JsonValueKind.Array)
        {
            return samples;
        }

        var startMs = new DateTimeOffset(DateTime.SpecifyKind(startUtc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();

        foreach (var row in rows.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            double? At(string key) =>
                columns.TryGetValue(key, out var index) && index < row.GetArrayLength()
                && row[index] is { ValueKind: JsonValueKind.Number } cell
                    ? cell.GetDouble()
                    : null;

            var depth = At("DEPTH");
            var timestampMs = At("TIMESTAMP");
            if (depth is null || timestampMs is null)
            {
                continue;
            }

            // TIMESTAMP is epoch milliseconds; samples carry seconds elapsed from the dive start.
            var elapsed = (int)Math.Round((timestampMs.Value - startMs) / 1000d);

            // ASCENT_RATE is metres/second on the wire; the DTO wants metres/minute.
            var ascentRate = At("ASCENT_RATE") is { } rate ? rate * 60d : (double?)null;

            // NDL_TIME is SECONDS on the wire while ProfileSampleDto.Ndl is minutes (the profile table
            // renders it with a " min" suffix). Confirmed against live data: the minimum on a 14.4 m EAN30
            // dive is 2234 → 37.2 min, and the maximum 59315 → ~16 h, i.e. "no limit" at the surface.
            // Passing the raw value through would overstate every NDL readout by 60×.
            var ndlMinutes = At("NDL_TIME") is { } ndl ? ndl / 60d : (double?)null;

            samples.Add(new ProfileSampleDto(
                TSeconds: elapsed,
                DepthM: depth.Value,
                TempC: At("TEMPERATURE"),
                PpO2: At("PO2"),
                Ndl: ndlMinutes,
                CeilingM: At("NEXT_STOP_DEPTH"),
                AscentRateMPerMin: ascentRate,
                Cns: At("PERCENT_CNS_LOAD"),
                TankPressureBar: null, // Not a records column; pressure arrives via tankSensors.
                Setpoint: null,
                HeartRate: At("HR")));
        }

        return samples;
    }

    /// <summary>
    /// Maps <c>performance</c> — what the computer <em>recorded</em> about oxygen and nitrogen exposure.
    /// Returns null when Garmin reported none, so the UI hides the block rather than showing zeroes.
    /// </summary>
    private static DiveExposureDto? ReadExposure(JsonElement root)
    {
        if (!root.TryGetProperty("performance", out var p) || p.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var exposure = new DiveExposureDto(
            CnsStartPercent: ReadDouble(p, "percentStartCNS"),
            CnsEndPercent: ReadDouble(p, "percentEndCNS"),
            N2StartPercent: ReadDouble(p, "percentStartN2"),
            N2EndPercent: ReadDouble(p, "percentEndN2"),
            O2ToxicityPercent: ReadDouble(p, "o2Toxicity"),
            AvgPo2: ReadDouble(p, "avgPo2"),
            MaxPo2: ReadDouble(p, "maxPo2"));

        return exposure == new DiveExposureDto() ? null : exposure;
    }

    /// <summary>
    /// Maps the recording device and the deco configuration it was running (<c>config[0]</c> — the dive's
    /// active configuration). Gradient factors are displayed, never used in a calculation.
    /// </summary>
    private static DiveComputerDto? ReadComputer(JsonElement root)
    {
        var device = root.TryGetProperty("deviceInfo", out var info)
            ? ReadString(info, "productDisplayName")
            : null;

        var config = root.TryGetProperty("config", out var configs)
            && configs.ValueKind == JsonValueKind.Array
            && configs.GetArrayLength() > 0
                ? configs[0]
                : default;

        var computer = new DiveComputerDto(
            DeviceName: device,
            TissueModel: Humanize(ReadString(config, "tissueModel")),
            GradientFactorLow: ReadInt(config, "gfLow"),
            GradientFactorHigh: ReadInt(config, "gfHigh"));

        return computer == new DiveComputerDto() ? null : computer;
    }

    /// <summary>
    /// Maps <c>records.alerts</c>, converting each absolute epoch-millisecond timestamp to seconds elapsed
    /// from the dive start so alerts line up with <see cref="ProfileSampleDto.TSeconds"/> on the profile graph.
    /// </summary>
    private static IReadOnlyList<DiveAlertDto>? ReadAlerts(JsonElement root, DateTime startUtc)
    {
        if (!root.TryGetProperty("records", out var records)
            || !records.TryGetProperty("alerts", out var alerts)
            || alerts.ValueKind != JsonValueKind.Array
            || alerts.GetArrayLength() == 0)
        {
            return null;
        }

        var startMs = new DateTimeOffset(DateTime.SpecifyKind(startUtc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
        var mapped = new List<DiveAlertDto>();

        foreach (var alert in alerts.EnumerateArray())
        {
            if (ReadString(alert, "alertType") is not { Length: > 0 } type)
            {
                continue;
            }

            var elapsed = ReadDouble(alert, "timestamp") is { } ms ? (int)Math.Round((ms - startMs) / 1000d) : 0;

            var raw = alert.TryGetProperty("data", out var data)
                && data.ValueKind == JsonValueKind.Array
                && data.GetArrayLength() > 0
                    ? data[0].GetString()
                    : null;

            mapped.Add(new DiveAlertDto(elapsed, Humanize(type)!, FormatAlertDetail(type, raw)));
        }

        return mapped.Count > 0 ? mapped : null;
    }

    /// <summary>
    /// Formats an alert's <c>data[0]</c> for display, but <b>only for alert types whose units are confirmed</b>.
    ///
    /// <para>Garmin's <c>data</c> array is alert-specific and mostly undocumented: observed values include a
    /// temperature for <c>MIN_TEMP</c>, a depth for <c>MAX_DEPTH</c>, a gas index for
    /// <c>DIVE_GAS_SWITCHED</c> and a nine-digit sensor identifier for <c>TANK_PRESSURE_RESERVE</c>. Rendering
    /// unknown values verbatim puts things like "3578197698" in front of a diver, so anything unconfirmed
    /// returns null and the UI shows the alert with no value.</para>
    /// </summary>
    private static string? FormatAlertDetail(string alertType, string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)
            || !double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        return alertType switch
        {
            "MIN_TEMP" or "MAX_TEMP" => string.Create(CultureInfo.InvariantCulture, $"{value:0.#} °C"),
            "MAX_DEPTH" => string.Create(CultureInfo.InvariantCulture, $"{value:0.0} m"),
            _ => null,
        };
    }

    private static IReadOnlyList<string>? ReadTags(JsonElement root)
    {
        if (!root.TryGetProperty("diveTags", out var tags) || tags.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var mapped = tags.EnumerateArray()
            .Select(t => t.ValueKind == JsonValueKind.String ? Humanize(t.GetString()) : null)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t!)
            .ToList();

        return mapped.Count > 0 ? mapped : null;
    }

    /// <summary>Turns Garmin's SCREAMING_SNAKE enums into display text: <c>ZHL_16C</c> → "ZHL 16C".</summary>
    private static string? Humanize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Replace('_', ' ');

    /// <summary>Maps <c>equipment.gases[]</c> (and tank pressures where air integration reported them).</summary>
    private static List<CylinderDto> ReadCylinders(JsonElement root)
    {
        var cylinders = new List<CylinderDto>();
        if (!root.TryGetProperty("equipment", out var equipment)
            || !equipment.TryGetProperty("gases", out var gases)
            || gases.ValueKind != JsonValueKind.Array)
        {
            return cylinders;
        }

        // Tank sensors are a parallel list when the diver used air integration.
        var sensors = root.TryGetProperty("tankSensors", out var sensorsEl)
            && sensorsEl.ValueKind == JsonValueKind.Array
                ? sensorsEl.EnumerateArray().ToList()
                : [];

        var gasList = gases.EnumerateArray().ToList();

        // A sidemount dive reports ONE gas across TWO tanks, so pair by index and then emit the surplus
        // sensors as their own cylinders — otherwise the second tank's pressures are silently dropped.
        var count = Math.Max(gasList.Count, sensors.Count);
        for (var index = 0; index < count; index++)
        {
            var gas = index < gasList.Count ? gasList[index]
                : gasList.Count > 0 ? gasList[0]   // extra tank carries the same (single) gas
                : default;
            var sensor = index < sensors.Count ? sensors[index] : default;

            cylinders.Add(new CylinderDto(
                O2: ReadDouble(gas, "percentOxygen"),
                He: ReadDouble(gas, "percentHelium"),
                StartBar: ReadDouble(sensor, "startingPressure"),
                // endingPressure is the measured pressure at the end of the dive. NOT reservePressure —
                // that is the diver's configured reserve threshold, a setting rather than a measurement,
                // and presenting it as the end pressure misreports remaining gas.
                EndBar: ReadDouble(sensor, "endingPressure"),
                VolumeL: null,
                WorkingPressureBar: ReadDouble(sensor, "ratedPressure")));
        }

        return cylinders;
    }

    /// <summary>Human-readable mix for the primary cylinder: "AIR", "EAN32", or a trimix triple.</summary>
    private static string? DescribeGasMix(List<CylinderDto> cylinders)
    {
        if (cylinders.Count == 0 || cylinders[0].O2 is not { } o2)
        {
            return null;
        }

        var he = cylinders[0].He ?? 0d;
        if (he > 0)
        {
            return string.Create(CultureInfo.InvariantCulture, $"TX{o2:0}/{he:0}");
        }

        // 21% is air, not "EAN21" — matches the app's own labelling.
        return Math.Abs(o2 - 21d) < 0.5
            ? "AIR"
            : string.Create(CultureInfo.InvariantCulture, $"EAN{o2:0}");
    }

    private Dictionary<string, string> DiveApiHeaders(string token) => new()
    {
        // Lowercase "bearer" is what the app sends and what Garmin returns as token_type.
        ["Authorization"] = $"bearer {token}",
        ["Accept"] = "application/json",
        ["User-Agent"] = ApiUserAgent,
        ["x-app-ver"] = AppVersion,
        ["x-lang"] = "en",
    };

    private static string? ReadId(JsonElement dive) =>
        dive.TryGetProperty("id", out var idEl)
            ? idEl.ValueKind == JsonValueKind.Number
                ? idEl.GetInt64().ToString(CultureInfo.InvariantCulture)
                : idEl.GetString()
            : null;

    /// <summary>Garmin sends ISO-8601 <b>with an offset</b> (e.g. <c>2026-04-17T13:33:06+02:00</c>).</summary>
    private static DateTime ReadStartUtc(JsonElement dive)
    {
        if (dive.TryGetProperty("startTime", out var el) && el.GetString() is { Length: > 0 } value
            && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return parsed.UtcDateTime;
        }

        return default;
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var el)
        && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    private static int? ReadInt(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var el)
        && el.ValueKind == JsonValueKind.Number
        && el.TryGetInt32(out var value)
            ? value
            : null;

    private static bool? ReadBool(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var el)
        && el.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? el.GetBoolean()
            : null;

    private static double? ReadDouble(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var el)
        && el.ValueKind == JsonValueKind.Number
            ? el.GetDouble()
            : null;

    private static JsonDocument ParseJson(string body, string what)
    {
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new ConnectorAuthException($"{what} returned a response that is not JSON: {ex.Message}");
        }
    }
}

/// <summary>
/// What is persisted in the single secure-store slot for Garmin: the 24-hour access token plus the 30-day
/// refresh token and an absolute expiry, so a re-login is not required daily. Serialized as compact JSON —
/// the store's contract is a string, and this keeps <c>ISecureTokenStore</c> unchanged. Never holds a password.
/// </summary>
internal sealed record GarminTokenEnvelope(string AccessToken, string? RefreshToken, DateTimeOffset ExpiresAt)
{
    public string Serialize() => JsonSerializer.Serialize(new Payload
    {
        Access = AccessToken,
        Refresh = RefreshToken,
        ExpiresAtUnix = ExpiresAt.ToUnixTimeSeconds(),
    });

    /// <summary>Returns null when the stored value is a bare token from an earlier build, not an envelope.</summary>
    public static GarminTokenEnvelope? TryParse(string stored)
    {
        if (string.IsNullOrWhiteSpace(stored) || stored.TrimStart() is not ['{', ..])
        {
            return null;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<Payload>(stored);
            return payload?.Access is { Length: > 0 } access
                ? new GarminTokenEnvelope(
                    access, payload.Refresh, DateTimeOffset.FromUnixTimeSeconds(payload.ExpiresAtUnix))
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed class Payload
    {
        public string? Access { get; set; }

        public string? Refresh { get; set; }

        public long ExpiresAtUnix { get; set; }
    }
}
