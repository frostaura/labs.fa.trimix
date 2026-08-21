using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using Trimix.Core.Abstractions;

namespace Trimix.Core.Connectors.Cloud;

/// <summary>
/// Reverse-engineered Suunto personal-export source. The Suunto app's backend is <b>Sports Tracker</b>
/// (<c>api.sports-tracker.com</c>, Suunto-owned) — NOT <c>cloudapi.suunto.com</c>, and auth is a Sports
/// Tracker session key in an <c>STTAuthorization</c> header, NOT an OAuth Bearer token. Both were confirmed
/// by mitmproxy capture of the Suunto iOS app on 2026-08-07 (contract:
/// <c>docs/reverse-engineering/suunto-sports-tracker-api.md</c>). This is
/// <see cref="LegalPosture.ToSRiskReverseEng"/> and <b>ships dark</b> (<c>EnabledByDefault=false</c>).
///
/// <para><b>Supported at reduced fidelity (founder decision 2026-08-07: "support what we have").</b> The login
/// and dive-list legs are verified, so Suunto is connectable (<c>ProviderCatalog.IsAvailable("suunto_cloud")
/// == true</c>). The account captured held no real scuba dive, so the <b>diving <c>activityId</c></b>
/// (<see cref="DivingActivityIds"/>) and the <b>depth/temperature sample-fetch endpoint</b> remain UNVERIFIED
/// best guesses — <see cref="FetchProfileAsync"/> therefore degrades to a summary-only dive rather than
/// failing. Confirm both against a real dive when one is available (contract doc's "To finish" section) to
/// lift fidelity from summary to full depth/temp.</para>
///
/// <para><b>Fidelity is deliberately <see cref="ProfileFidelity.DepthTempOnly"/>:</b> research holds that
/// Suunto's cloud export strips tank/cylinder pressure, gas mix, and deco/route data — depth + temperature
/// come through, the dive-critical channels do not. The clean, full-fidelity Suunto path is BLE-direct from
/// the diver's own computer (a different branch); this cloud connector exists only as a gated fallback. FIT
/// decode is delegated to the shared <see cref="IFitActivityParser"/>.</para>
/// </summary>
public sealed class SuuntoCloudSourceConnector(
    IHttpTransport http,
    ISecureTokenStore tokens,
    IConnectorConsentStore consent,
    IFitActivityParser fit,
    CloudSourceOptions? options = null) : GatedCloudSourceConnector(http, tokens, consent)
{
    public const string Id = "suunto_cloud";

    // VERIFIED (2026-08-07 capture). Sports Tracker is Suunto's real backend.
    //   Login: POST /apiserver/v1/login2, form `l=<email>&p=<password>&version=2` → JSON with `sessionkey`.
    //   List : GET  /apiserver/v1/workouts?since={ms}&offset=0&limit=50 → { payload:[…], metadata:{until} }.
    // The login endpoint is carried on the SuuntoTokenUrl override slot (config "AuthUrl"/"TokenUrl").
    private const string DefaultLoginUrl = "https://api.sports-tracker.com/apiserver/v1/login2";
    private const string DefaultWorkoutsUrl = "https://api.sports-tracker.com/apiserver/v1/workouts";

    // UNVERIFIED placeholder — the real per-dive sample/profile endpoint was NOT observed (no dive in the
    // captured account). Kept only so the connector compiles and ships dark; never called at runtime while
    // Suunto is "Coming soon". Confirm against a real dive before trusting this.
    private const string DefaultExportFitUrl = "https://api.sports-tracker.com/apiserver/v1/workout/exportFit";

    private string LoginUrl => options?.SuuntoTokenUrl ?? DefaultLoginUrl;
    private string WorkoutsUrl => options?.SuuntoWorkoutsUrl ?? DefaultWorkoutsUrl;
    private string ExportFitUrl => options?.SuuntoExportFitUrl ?? DefaultExportFitUrl;

    // UNVERIFIED — Sports Tracker activity ids that represent scuba/free diving. The captured account held
    // only manual test workouts (activityId 1 and 78), neither a dive, so the true diving id is unconfirmed.
    // Kept as a set so the list step can filter dives from runs/rides once the id is known; must be verified
    // against a real dive before Suunto is promoted.
    private static readonly HashSet<int> DivingActivityIds = [82, 83];

    public override ConnectorDescriptor Descriptor { get; } = new(
        ConnectorId: Id,
        Kind: ConnectorKind.CloudApi,
        Legal: LegalPosture.ToSRiskReverseEng,
        Fidelity: ProfileFidelity.DepthTempOnly, // cloud export strips tank/gas (research).
        Auth: AuthModel.UserCredential,
        Platforms: Platforms.Both,
        EnabledByDefault: false);

    public override string DisplayName => "Suunto app (personal export)";

    protected override string Disclosure =>
        "This imports your own dives from your personal Suunto app cloud. It signs in with your Suunto account " +
        "credentials against Suunto's private backend (Sports Tracker), which is NOT an official Suunto " +
        "integration and may breach Suunto's Terms of Service. Cloud export omits tank pressure and gas mix " +
        "(depth and temperature only) — reading your dive computer directly over Bluetooth is the complete, " +
        "Terms-clean path. Your password is used once to obtain a session token and is never stored; only the " +
        "token is kept, in secure device storage.";

    /// <summary>
    /// VERIFIED login: <c>POST /apiserver/v1/login2</c> with the form body <c>l={email}&amp;p={password}&amp;version=2</c>.
    /// The response JSON carries a 32-char <c>sessionkey</c> which becomes the <c>STTAuthorization</c> header
    /// value for every subsequent call. (Sports Tracker also returns an equal <c>userKey</c>; the session key
    /// is the one the app sends.)
    /// </summary>
    protected override async Task<string> ExchangeCredentialsForTokenAsync(ConnectorCredentials credentials, CancellationToken ct)
    {
        var form =
            $"l={Uri.EscapeDataString(credentials.Username)}" +
            $"&p={Uri.EscapeDataString(credentials.Password)}" +
            "&version=2";
        var response = await Http.SendAsync(new HttpExchangeRequest(
            "POST", LoginUrl,
            new Dictionary<string, string>
            {
                ["Content-Type"] = "application/x-www-form-urlencoded",
                ["Accept"] = "application/json",
            },
            form), ct).ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            throw new ConnectorAuthException("Suunto sign-in failed — check your account credentials.");
        }

        using var doc = JsonDocument.Parse(response.Body);
        if (!doc.RootElement.TryGetProperty("sessionkey", out var sessionKey) ||
            sessionKey.GetString() is not { Length: > 0 } token)
        {
            throw new ConnectorAuthException("Suunto sign-in returned no session key.");
        }

        return token;
    }

    /// <summary>
    /// VERIFIED list: <c>GET /apiserver/v1/workouts?since={ms}&amp;offset=0&amp;limit=50</c>, authorized with the
    /// session key in <c>STTAuthorization</c>. The response is <c>{ "payload": [ … ], "metadata": {…} }</c>; each
    /// workout carries <c>workoutKey</c> (the id used everywhere else), <c>activityId</c>, <c>startTime</c> and
    /// <c>stopTime</c> in epoch millis, and <c>totalTime</c> in seconds.
    /// <para>The diving filter is UNVERIFIED (see <see cref="DivingActivityIds"/>) — until the real diving
    /// activity id is confirmed, a workout whose id is not in the set is skipped, so an unconfirmed set would
    /// silently import nothing rather than import non-dives.</para>
    /// </summary>
    public override async Task<IReadOnlyList<DiveRef>> ListDivesSinceAsync(SyncCursor cursor, CancellationToken ct = default)
    {
        var token = await RequireTokenAsync(ct).ConfigureAwait(false);
        var sinceMs = cursor.Since is { } since
            ? new DateTimeOffset(DateTime.SpecifyKind(since, DateTimeKind.Utc)).ToUnixTimeMilliseconds()
            : 0L;
        var url = $"{WorkoutsUrl}?since={sinceMs.ToString(CultureInfo.InvariantCulture)}&offset=0&limit=50";

        var response = await Http.SendAsync(new HttpExchangeRequest("GET", url, SttAuth(token)), ct).ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            throw new InvalidOperationException($"Suunto workout list failed (HTTP {response.StatusCode}).");
        }

        var refs = new List<DiveRef>();
        using var doc = JsonDocument.Parse(response.Body);
        // Sports Tracker wraps the list in { "payload": [ … ] }; tolerate a bare array too.
        var items = doc.RootElement.ValueKind == JsonValueKind.Array
            ? doc.RootElement
            : doc.RootElement.TryGetProperty("payload", out var payload) ? payload : default;
        if (items.ValueKind != JsonValueKind.Array)
        {
            return refs;
        }

        foreach (var workout in items.EnumerateArray())
        {
            // Filter to diving workouts when the activity id is present (UNVERIFIED set — see the field).
            if (workout.TryGetProperty("activityId", out var actEl) && actEl.ValueKind == JsonValueKind.Number &&
                !DivingActivityIds.Contains(actEl.GetInt32()))
            {
                continue;
            }

            var key = workout.TryGetProperty("workoutKey", out var keyEl) ? keyEl.GetString() : null;
            if (string.IsNullOrEmpty(key))
            {
                continue;
            }

            var start = ParseEpochMillis(workout, "startTime");
            var duration = workout.TryGetProperty("totalTime", out var totalEl) && totalEl.ValueKind == JsonValueKind.Number
                ? (int)Math.Round(totalEl.GetDouble())
                : 0;

            refs.Add(new DiveRef(Descriptor.ConnectorId, key, start, duration));
        }

        return refs;
    }

    /// <summary>
    /// Best-effort profile fetch with a <b>graceful summary fallback</b>. The per-dive sample/profile endpoint
    /// was never observed (the captured account had no dive), so the FIT-export URL is an UNVERIFIED best guess.
    /// Rather than drop the dive when that guess doesn't pan out, this <b>always returns at least a summary dive</b>
    /// built from the list entry (start + duration) — "support what we have": the dive appears, just without the
    /// depth/temp profile until a real-dive capture confirms the real endpoint. Any failure (non-2xx, decode
    /// error, empty parse) falls through to the summary; a successful decode returns the richer dive.
    /// </summary>
    public override async Task<ImportedDive> FetchProfileAsync(DiveRef diveRef, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(diveRef);
        var token = await RequireTokenAsync(ct).ConfigureAwait(false);

        try
        {
            var response = await Http.SendAsync(new HttpExchangeRequest(
                "GET", $"{ExportFitUrl}/{Uri.EscapeDataString(diveRef.SourceId)}",
                new Dictionary<string, string> { ["STTAuthorization"] = token, ["Accept"] = "application/octet-stream" }),
                ct).ConfigureAwait(false);
            if (response.IsSuccess)
            {
                var dives = fit.Parse(BinaryPayload.Decode(response.Body));
                if (dives.Count > 0)
                {
                    return dives.Count == 1
                        ? dives[0]
                        : dives.OrderBy(d => Math.Abs((d.StartUtc - diveRef.StartUtc).Ticks)).First();
                }
            }
        }
        catch (Exception)
        {
            // Unverified endpoint — degrade to summary rather than fail the whole import.
        }

        // Summary-only dive from the verified list data: the dive is imported, the profile is simply absent.
        return new ImportedDive(
            StartUtc: diveRef.StartUtc,
            DurationSec: diveRef.DurationSec,
            MaxDepthM: 0,
            AvgDepthM: null,
            MinTempC: null,
            GasMix: null,
            Samples: []);
    }

    /// <summary>Sports Tracker auth header — the session key goes in <c>STTAuthorization</c>, not <c>Authorization</c>.</summary>
    private static Dictionary<string, string> SttAuth(string token) => new()
    {
        ["STTAuthorization"] = token,
        ["Accept"] = "application/json",
        ["Content-Type"] = "application/json",
    };

    private static DateTime ParseEpochMillis(JsonElement el, string prop)
    {
        if (el.TryGetProperty(prop, out var value) && value.ValueKind == JsonValueKind.Number)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(value.GetInt64()).UtcDateTime;
        }

        return default;
    }
}
