using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using Trimix.Contracts.Dtos;
using Trimix.Core.Abstractions;
using Trimix.Core.Connectors.Cloud;

namespace Trimix.Core.Connectors.Infra;

/// <summary>One dive as listed by the MySSI logbook, before its profile is fetched.</summary>
public sealed record MySsiDiveSummary(
    string Id,
    DateTime StartUtc,
    int DurationSec,
    string? ComputerBrand);

/// <summary>
/// Shared reverse-engineered <b>read</b> client for the MySSI (DiveSSI) private cloud. It serves the
/// <c>MaresCloudSourceConnector</c> read path (Mares has no cloud API of its own; MySSI reads Mares computers
/// directly) and is adjacent to <c>SsiPushDestinationConnector</c> (write, separate options).
///
/// <para><b>ScubaPro no longer routes here (2026-08-07).</b> The old design assumed ScubaPro synced
/// LogTRAK→MySSI, but a live capture proved LogTRAK has its own Firebase backend — see
/// <c>ScubaProCloudSourceConnector</c> and <c>docs/reverse-engineering/scubapro-logtrak-api.md</c>. The Mares
/// corridor below is now the ONLY MySSI read user, and — because the ScubaPro assumption was wrong — the
/// Mares-via-MySSI assumption is itself unverified until captured; the endpoints here remain
/// researched-guess defaults.</para>
///
/// <para><b>Token discipline.</b> The client is stateless about credentials: <see cref="AuthenticateAsync"/>
/// performs the one-shot credential→token exchange and <em>returns</em> the token — persistence is owned by
/// the calling connector's <c>GatedCloudSourceConnector</c> base, which writes only the token to
/// <c>ISecureTokenStore</c> (never the password). List/fetch take the token as an argument.</para>
///
/// <para><b>Endpoints are researched-guess defaults</b>, consistent with the SSI-push research (the private
/// DiveSSI cloud authenticates with <c>l</c>/<c>p</c> query parameters — the vendor's undocumented wire
/// shape, not our choice). Each is overridable via <see cref="CloudSourceOptions"/> (config-injected at
/// deploy); a null override keeps the in-source constant. EXPOSURE NOTE (same as the SSI push connector):
/// credentials in a request URL are higher-risk than a body, so no request-URL logging may ever be attached
/// to this client's transport.</para>
///
/// <para>All I/O goes through <see cref="IHttpTransport"/>, so the whole login → list → fetch flow is
/// offline-testable against recorded fixtures, and consent-gating stays enforceable by the callers
/// (this client is only ever invoked from behind a consent-gated connector).</para>
/// </summary>
public sealed class MySsiCloudClient(IHttpTransport http, CloudSourceOptions? options = null)
{
    // Researched-guess defaults (DiveSSI private API host, aligned with the SSI-push corridor research).
    // Overridable via CloudSourceOptions.MySsi* — null keeps these constants in force.
    private const string DefaultLoginUrl = "https://api.divessi.com/1/API/login";
    private const string DefaultDivesUrl = "https://api.divessi.com/1/API/logbook";

    private string LoginUrl => options?.MySsiLoginUrl ?? DefaultLoginUrl;

    private string DivesUrl => options?.MySsiDivesUrl ?? DefaultDivesUrl;

    /// <summary>
    /// One-shot credential→token exchange against the MySSI login endpoint. Returns the exchanged token —
    /// the caller (a gated connector base) persists it; the password goes no further than this request.
    /// Throws <see cref="ConnectorAuthException"/> on failure.
    /// </summary>
    public async Task<string> AuthenticateAsync(string username, string password, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(username);
        ArgumentException.ThrowIfNullOrEmpty(password);

        // Researched wire shape: the private DiveSSI cloud takes credentials as l/p query params (see class
        // remarks for the exposure note). Config-overridable host; never called before connector consent.
        var sep = LoginUrl.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        var url = string.Create(
            CultureInfo.InvariantCulture,
            $"{LoginUrl}{sep}l={Uri.EscapeDataString(username)}&p={Uri.EscapeDataString(password)}");

        var response = await http.SendAsync(new HttpExchangeRequest(
            "GET", url,
            new Dictionary<string, string> { ["Accept"] = "application/json" }), ct).ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            throw new ConnectorAuthException("MySSI sign-in failed — check your MySSI (SSI) account credentials.");
        }

        var token = ExtractToken(response.Body);
        if (string.IsNullOrEmpty(token))
        {
            throw new ConnectorAuthException("MySSI sign-in returned no access token.");
        }

        return token;
    }

    /// <summary>
    /// Lists the diver's logbook entries, newest-tolerant and vendor-filterable: each MySSI dive carries the
    /// source computer's brand, so a caller can request only Mares or only ScubaPro dives by passing
    /// case-insensitive brand aliases (<c>null</c>/empty ⇒ no filter). <paramref name="since"/> maps to the
    /// incremental-sync cursor.
    /// </summary>
    public async Task<IReadOnlyList<MySsiDiveSummary>> ListDivesAsync(
        string token,
        DateTime? since = null,
        IReadOnlyCollection<string>? brandAliases = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);

        var url = DivesUrl;
        if (since is { } s)
        {
            var sep = url.Contains('?', StringComparison.Ordinal) ? '&' : '?';
            url += $"{sep}since={Uri.EscapeDataString(s.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))}";
        }

        var response = await http.SendAsync(new HttpExchangeRequest("GET", url, Bearer(token)), ct).ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            throw new InvalidOperationException($"MySSI logbook list failed (HTTP {response.StatusCode}).");
        }

        var summaries = new List<MySsiDiveSummary>();
        using var doc = JsonDocument.Parse(response.Body);
        var items = doc.RootElement.ValueKind == JsonValueKind.Array
            ? doc.RootElement
            : doc.RootElement.TryGetProperty("dives", out var divesEl) ? divesEl : default;
        if (items.ValueKind != JsonValueKind.Array)
        {
            return summaries;
        }

        foreach (var dive in items.EnumerateArray())
        {
            var id = ReadId(dive);
            if (string.IsNullOrEmpty(id))
            {
                continue;
            }

            var brand = ReadBrand(dive);
            if (brandAliases is { Count: > 0 } &&
                (brand is null || !brandAliases.Any(a => brand.Contains(a, StringComparison.OrdinalIgnoreCase))))
            {
                continue;
            }

            summaries.Add(new MySsiDiveSummary(id, ReadStartUtc(dive), ReadDurationSec(dive), brand));
        }

        return summaries;
    }

    /// <summary>Fetches one dive's full record (profile samples + cylinders) as an <see cref="ImportedDive"/>.</summary>
    public async Task<ImportedDive> FetchDiveAsync(string token, string diveId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);
        ArgumentException.ThrowIfNullOrEmpty(diveId);

        var response = await http.SendAsync(new HttpExchangeRequest(
            "GET", $"{DivesUrl}/{Uri.EscapeDataString(diveId)}", Bearer(token)), ct).ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            throw new InvalidOperationException($"MySSI dive fetch failed for {diveId} (HTTP {response.StatusCode}).");
        }

        using var doc = JsonDocument.Parse(response.Body);
        var dive = doc.RootElement;

        var samples = new List<ProfileSampleDto>();
        if (dive.TryGetProperty("profile", out var profileEl) && profileEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var s in profileEl.EnumerateArray())
            {
                samples.Add(new ProfileSampleDto(
                    TSeconds: ReadInt(s, "t") ?? ReadInt(s, "offsetSeconds") ?? 0,
                    DepthM: ReadDouble(s, "depth_m") ?? ReadDouble(s, "depthMeters") ?? 0,
                    TempC: ReadDouble(s, "temp_c") ?? ReadDouble(s, "tempC"),
                    PpO2: ReadDouble(s, "ppo2"),
                    Ndl: ReadDouble(s, "ndl_min"),
                    CeilingM: ReadDouble(s, "ceiling_m"),
                    AscentRateMPerMin: ReadDouble(s, "ascent_m_min")));
            }
        }

        var cylinders = new List<CylinderDto>();
        if (dive.TryGetProperty("cylinders", out var cylsEl) && cylsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in cylsEl.EnumerateArray())
            {
                cylinders.Add(new CylinderDto(
                    O2: ReadDouble(c, "o2"),
                    He: ReadDouble(c, "he"),
                    StartBar: ReadDouble(c, "start_bar"),
                    EndBar: ReadDouble(c, "end_bar"),
                    VolumeL: ReadDouble(c, "volume_l")));
            }
        }

        return new ImportedDive(
            StartUtc: ReadStartUtc(dive),
            DurationSec: ReadDurationSec(dive),
            MaxDepthM: ReadDouble(dive, "depth_m") ?? ReadDouble(dive, "maxDepthMeters") ?? 0,
            AvgDepthM: ReadDouble(dive, "avg_depth_m") ?? ReadDouble(dive, "avgDepthMeters"),
            MinTempC: ReadDouble(dive, "temp_c") ?? ReadDouble(dive, "minWaterTempC"),
            GasMix: dive.TryGetProperty("gasmix", out var gasEl) && gasEl.ValueKind == JsonValueKind.String
                ? gasEl.GetString()
                : null,
            Samples: samples,
            Cylinders: cylinders.Count > 0 ? cylinders : null);
    }

    private static Dictionary<string, string> Bearer(string token) => new()
    {
        ["Authorization"] = $"Bearer {token}",
        ["Accept"] = "application/json",
    };

    /// <summary>Pulls a token from the login response — JSON <c>token</c>/<c>access_token</c> or a bare body.</summary>
    private static string? ExtractToken(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        var trimmed = body.Trim();
        if (!trimmed.StartsWith('{'))
        {
            return trimmed;
        }

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            foreach (var key in (ReadOnlySpan<string>)["token", "access_token", "accessToken"])
            {
                if (doc.RootElement.TryGetProperty(key, out var el) && el.ValueKind == JsonValueKind.String)
                {
                    return el.GetString();
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static string? ReadId(JsonElement dive)
    {
        foreach (var key in (ReadOnlySpan<string>)["dive_id", "id"])
        {
            if (dive.TryGetProperty(key, out var el))
            {
                return el.ValueKind == JsonValueKind.Number
                    ? el.GetInt64().ToString(CultureInfo.InvariantCulture)
                    : el.GetString();
            }
        }

        return null;
    }

    /// <summary>The source computer's brand — the field the Mares/ScubaPro vendor filter keys on.</summary>
    private static string? ReadBrand(JsonElement dive)
    {
        foreach (var key in (ReadOnlySpan<string>)["brand", "computer_brand", "computer"])
        {
            if (dive.TryGetProperty(key, out var el) && el.ValueKind == JsonValueKind.String)
            {
                return el.GetString();
            }
        }

        return null;
    }

    private static DateTime ReadStartUtc(JsonElement dive)
    {
        foreach (var key in (ReadOnlySpan<string>)["datetime", "startTime", "date"])
        {
            if (dive.TryGetProperty(key, out var el) && el.GetString() is { Length: > 0 } s &&
                DateTime.TryParse(s, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
            {
                return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
            }
        }

        return default;
    }

    /// <summary>MySSI reports <c>divetime</c> in minutes (the SSI summary convention); tolerate explicit seconds too.</summary>
    private static int ReadDurationSec(JsonElement dive)
    {
        if (ReadInt(dive, "duration_sec") is { } sec)
        {
            return sec;
        }

        if (ReadInt(dive, "divetime") is { } minutes)
        {
            return minutes * 60;
        }

        return 0;
    }

    private static double? ReadDouble(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;

    private static int? ReadInt(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;
}
