using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Trimix.Core.Abstractions;

namespace Trimix.Core.Connectors.Cloud;

/// <summary>
/// Reverse-engineered Garmin Connect personal-export source (<c>garth</c>-style flow). Reads the diver's own
/// dive activities from their Garmin Connect account by replicating the Garmin Connect Mobile SSO login, then
/// downloading each dive's <b>ORIGINAL <c>.FIT</c></b> (the only container that preserves the full Garmin
/// Descent profile: per-sample depth/temp/NDL/CNS, <c>dive_summary</c>, <c>dive_gas</c>). This is
/// <see cref="LegalPosture.ToSRiskReverseEng"/>: it holds the user's Garmin credentials for a single
/// token exchange, violates Garmin's ToS, and <b>ships dark</b> (<c>EnabledByDefault=false</c>), enabled only
/// behind explicit per-user opt-in + disclosure + legal sign-off.
///
/// <para>Research: Garmin has <em>no</em> BLE dive-download and no official dive API — file/cloud is the only
/// path, and this is the automated-cloud variant of user-supplied FIT import. FIT decode is delegated to
/// <see cref="IFitActivityParser"/> (shared with file import). Flow: GET signin (CSRF) → POST signin (ticket)
/// → POST oauth exchange (bearer) → list diving activities → download ORIGINAL FIT.</para>
/// </summary>
public sealed partial class GarminCloudSourceConnector(
    IHttpTransport http,
    ISecureTokenStore tokens,
    IConnectorConsentStore consent,
    IFitActivityParser fit,
    CloudSourceOptions? options = null) : GatedCloudSourceConnector(http, tokens, consent)
{
    public const string Id = "garmin_connect";

    // Researched garth / python-garminconnect endpoints (do not invent — these are the documented reverse-eng
    // surfaces). The OAuth1→OAuth2 handshake is modelled as a single ticket→bearer exchange call for the dark
    // connector; the exact consumer-key signing is finalised with legal before the flag flips. Each is
    // overridable via CloudSourceOptions (config-injected at deploy) — a null override keeps the default here.
    private const string DefaultSigninUrl =
        "https://sso.garmin.com/sso/signin?service=https%3A%2F%2Fconnect.garmin.com%2Fmodern%2F&embed=true";
    private const string DefaultTokenExchangeUrl = "https://connectapi.garmin.com/oauth-service/oauth/exchange/user/2.0";
    private const string DefaultActivitySearchUrl = "https://connectapi.garmin.com/activitylist-service/activities/search/activities";
    private const string DefaultDownloadUrl = "https://connectapi.garmin.com/download-service/files/activity";

    private string SigninUrl => options?.GarminSigninUrl ?? DefaultSigninUrl;
    private string TokenExchangeUrl => options?.GarminTokenExchangeUrl ?? DefaultTokenExchangeUrl;
    private string ActivitySearchUrl => options?.GarminActivitySearchUrl ?? DefaultActivitySearchUrl;
    private string DownloadUrl => options?.GarminDownloadUrl ?? DefaultDownloadUrl;

    public override ConnectorDescriptor Descriptor { get; } = new(
        ConnectorId: Id,
        Kind: ConnectorKind.CloudApi,
        Legal: LegalPosture.ToSRiskReverseEng,
        Fidelity: ProfileFidelity.FullProfile, // ORIGINAL FIT carries depth/temp/NDL/CNS + gas.
        Auth: AuthModel.UserCredential,
        Platforms: Platforms.Both,
        EnabledByDefault: false);

    public override string DisplayName => "Garmin Connect (personal export)";

    protected override string Disclosure =>
        "This imports your own dives from your personal Garmin Connect account. It signs in with your Garmin " +
        "username and password using Garmin's private app login — this is NOT an official Garmin integration and " +
        "may breach Garmin's Terms of Service. Your password is used once to obtain an access token and is never " +
        "stored; only the access token is kept, in secure device storage. You can disconnect at any time.";

    protected override async Task<string> ExchangeCredentialsForTokenAsync(ConnectorCredentials credentials, CancellationToken ct)
    {
        // 1. GET the SSO signin page to read the CSRF token.
        var signinPage = await Http.SendAsync(new HttpExchangeRequest(
            "GET", SigninUrl, new Dictionary<string, string> { ["Accept"] = "text/html" }), ct).ConfigureAwait(false);
        if (!signinPage.IsSuccess)
        {
            throw new ConnectorAuthException($"Garmin sign-in page unavailable (HTTP {signinPage.StatusCode}).");
        }

        var csrf = CsrfPattern().Match(signinPage.Body) is { Success: true } m ? m.Groups[1].Value : null;

        // 2. POST credentials + CSRF → the response embeds a service ticket (ST-...).
        var form =
            $"username={Uri.EscapeDataString(credentials.Username)}" +
            $"&password={Uri.EscapeDataString(credentials.Password)}" +
            "&embed=true" +
            (csrf is null ? string.Empty : $"&_csrf={Uri.EscapeDataString(csrf)}");
        var signinResult = await Http.SendAsync(new HttpExchangeRequest(
            "POST", SigninUrl,
            new Dictionary<string, string> { ["Content-Type"] = "application/x-www-form-urlencoded" },
            form), ct).ConfigureAwait(false);
        if (!signinResult.IsSuccess)
        {
            throw new ConnectorAuthException("Garmin sign-in failed — check your username and password.");
        }

        var ticket = TicketPattern().Match(signinResult.Body) is { Success: true } t ? t.Groups[1].Value : null;
        if (string.IsNullOrEmpty(ticket))
        {
            throw new ConnectorAuthException("Garmin sign-in did not return a session ticket (login rejected or flow changed).");
        }

        // 3. Exchange the ticket for an OAuth2 bearer access token.
        var exchange = await Http.SendAsync(new HttpExchangeRequest(
            "POST", $"{TokenExchangeUrl}?ticket={Uri.EscapeDataString(ticket)}",
            new Dictionary<string, string> { ["Accept"] = "application/json" }), ct).ConfigureAwait(false);
        if (!exchange.IsSuccess)
        {
            throw new ConnectorAuthException($"Garmin token exchange failed (HTTP {exchange.StatusCode}).");
        }

        using var doc = JsonDocument.Parse(exchange.Body);
        if (!doc.RootElement.TryGetProperty("access_token", out var accessToken) ||
            accessToken.GetString() is not { Length: > 0 } token)
        {
            throw new ConnectorAuthException("Garmin token exchange returned no access token.");
        }

        return token;
    }

    public override async Task<IReadOnlyList<DiveRef>> ListDivesSinceAsync(SyncCursor cursor, CancellationToken ct = default)
    {
        var token = await RequireTokenAsync(ct).ConfigureAwait(false);
        var url = $"{ActivitySearchUrl}?start=0&limit=100&activityType=diving";
        if (cursor.Since is { } since)
        {
            url += $"&startDate={Uri.EscapeDataString(since.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))}";
        }

        var response = await Http.SendAsync(new HttpExchangeRequest("GET", url, Bearer(token)), ct).ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            throw new InvalidOperationException($"Garmin activity list failed (HTTP {response.StatusCode}).");
        }

        var refs = new List<DiveRef>();
        using var doc = JsonDocument.Parse(response.Body);
        foreach (var activity in doc.RootElement.EnumerateArray())
        {
            var activityId = activity.TryGetProperty("activityId", out var idEl)
                ? idEl.ValueKind == JsonValueKind.Number ? idEl.GetInt64().ToString(CultureInfo.InvariantCulture) : idEl.GetString()
                : null;
            if (string.IsNullOrEmpty(activityId))
            {
                continue;
            }

            var start = ParseGarminTime(activity, "startTimeGMT");
            var duration = activity.TryGetProperty("duration", out var durEl) && durEl.ValueKind == JsonValueKind.Number
                ? (int)Math.Round(durEl.GetDouble())
                : 0;
            var label = activity.TryGetProperty("activityName", out var nameEl) ? nameEl.GetString() : null;

            refs.Add(new DiveRef(Descriptor.ConnectorId, activityId, start, duration, label));
        }

        return refs;
    }

    public override async Task<ImportedDive> FetchProfileAsync(DiveRef diveRef, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(diveRef);
        var token = await RequireTokenAsync(ct).ConfigureAwait(false);

        // Download the ORIGINAL FIT (binary → Base64 in Body per the transport contract).
        var response = await Http.SendAsync(new HttpExchangeRequest(
            "GET", $"{DownloadUrl}/{Uri.EscapeDataString(diveRef.SourceId)}",
            new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}", ["Accept"] = "application/octet-stream" }),
            ct).ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            throw new InvalidOperationException($"Garmin FIT download failed for activity {diveRef.SourceId} (HTTP {response.StatusCode}).");
        }

        var fitBytes = BinaryPayload.Decode(response.Body);
        var dives = fit.Parse(fitBytes);
        if (dives.Count == 0)
        {
            throw new InvalidOperationException($"Garmin FIT for activity {diveRef.SourceId} contained no dive.");
        }

        // A dive activity is one dive; if multiple, pick the one nearest the ref start.
        return dives.Count == 1
            ? dives[0]
            : dives.OrderBy(d => Math.Abs((d.StartUtc - diveRef.StartUtc).Ticks)).First();
    }

    private static DateTime ParseGarminTime(JsonElement activity, string prop)
    {
        if (activity.TryGetProperty(prop, out var el) && el.GetString() is { Length: > 0 } value)
        {
            // Garmin GMT format: "yyyy-MM-dd HH:mm:ss" (already UTC) or ISO-8601.
            if (DateTime.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
            {
                return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
            }
        }

        return default;
    }

    [GeneratedRegex("name=\"_csrf\"\\s+value=\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex CsrfPattern();

    [GeneratedRegex("ticket=(ST-[^\"'&\\s]+)", RegexOptions.IgnoreCase)]
    private static partial Regex TicketPattern();
}
