using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using Trimix.Core.Abstractions;

namespace Trimix.Core.Connectors.Cloud;

/// <summary>
/// Reverse-engineered Suunto personal-export source. Reads the diver's own dives from their Suunto app cloud
/// by minting a token from their Suunto account credentials (private-cloud path), listing scuba workouts, and
/// downloading each as a <c>.FIT</c> export. This is <see cref="LegalPosture.ToSRiskReverseEng"/> and
/// <b>ships dark</b> (<c>EnabledByDefault=false</c>).
///
/// <para><b>Fidelity is deliberately <see cref="ProfileFidelity.DepthTempOnly"/>:</b> research confirms Suunto's
/// cloud FIT export <em>strips</em> tank/cylinder pressure, gas mix, and deco/route data — depth + temperature
/// come through, the dive-critical channels do not. The clean, full-fidelity Suunto path is BLE-direct from the
/// diver's own computer (a different branch); this cloud connector exists only as a gated fallback. FIT decode
/// is delegated to the shared <see cref="IFitActivityParser"/>.</para>
/// </summary>
public sealed class SuuntoCloudSourceConnector(
    IHttpTransport http,
    ISecureTokenStore tokens,
    IConnectorConsentStore consent,
    IFitActivityParser fit,
    CloudSourceOptions? options = null) : GatedCloudSourceConnector(http, tokens, consent)
{
    public const string Id = "suunto_cloud";

    // Researched Suunto endpoints (apizone.suunto.com developer portal). OAuth token host + cloud API host.
    // Modelled as a resource-owner password-grant against the OAuth token endpoint for the private path. Each
    // is overridable via CloudSourceOptions (config-injected at deploy); a null override keeps the default.
    private const string DefaultTokenUrl = "https://cloudapi-oauth.suunto.com/oauth/token";
    private const string DefaultWorkoutsUrl = "https://cloudapi.suunto.com/v2/workouts";
    private const string DefaultExportFitUrl = "https://cloudapi.suunto.com/v2/workout/exportFit";

    private string TokenUrl => options?.SuuntoTokenUrl ?? DefaultTokenUrl;
    private string WorkoutsUrl => options?.SuuntoWorkoutsUrl ?? DefaultWorkoutsUrl;
    private string ExportFitUrl => options?.SuuntoExportFitUrl ?? DefaultExportFitUrl;

    // Suunto activity ids that represent scuba/free diving (v2 workout schema). Kept as a set so the list step
    // filters to dives and ignores runs/rides synced to the same account.
    private static readonly HashSet<int> DivingActivityIds = [82, 83];

    public override ConnectorDescriptor Descriptor { get; } = new(
        ConnectorId: Id,
        Kind: ConnectorKind.CloudApi,
        Legal: LegalPosture.ToSRiskReverseEng,
        Fidelity: ProfileFidelity.DepthTempOnly, // cloud FIT strips tank/gas/deco (research).
        Auth: AuthModel.UserCredential,
        Platforms: Platforms.Both,
        EnabledByDefault: false);

    public override string DisplayName => "Suunto app (personal export)";

    protected override string Disclosure =>
        "This imports your own dives from your personal Suunto app cloud. It signs in with your Suunto account " +
        "credentials using Suunto's private cloud, which is NOT an official Suunto integration and may breach " +
        "Suunto's Terms of Service. Cloud export omits tank pressure and gas mix (depth and temperature only) — " +
        "reading your dive computer directly over Bluetooth is the complete, Terms-clean path. Your password is " +
        "used once to obtain an access token and is never stored; only the token is kept, in secure device storage.";

    protected override async Task<string> ExchangeCredentialsForTokenAsync(ConnectorCredentials credentials, CancellationToken ct)
    {
        var form =
            "grant_type=password" +
            $"&username={Uri.EscapeDataString(credentials.Username)}" +
            $"&password={Uri.EscapeDataString(credentials.Password)}";
        var response = await Http.SendAsync(new HttpExchangeRequest(
            "POST", TokenUrl,
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
        if (!doc.RootElement.TryGetProperty("access_token", out var accessToken) ||
            accessToken.GetString() is not { Length: > 0 } token)
        {
            throw new ConnectorAuthException("Suunto sign-in returned no access token.");
        }

        return token;
    }

    public override async Task<IReadOnlyList<DiveRef>> ListDivesSinceAsync(SyncCursor cursor, CancellationToken ct = default)
    {
        var token = await RequireTokenAsync(ct).ConfigureAwait(false);
        var url = WorkoutsUrl;
        if (cursor.Since is { } since)
        {
            var sinceMs = new DateTimeOffset(DateTime.SpecifyKind(since, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
            url += $"?since={sinceMs.ToString(CultureInfo.InvariantCulture)}";
        }

        var response = await Http.SendAsync(new HttpExchangeRequest("GET", url, Bearer(token)), ct).ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            throw new InvalidOperationException($"Suunto workout list failed (HTTP {response.StatusCode}).");
        }

        var refs = new List<DiveRef>();
        using var doc = JsonDocument.Parse(response.Body);
        // Suunto wraps the list in { "payload": [ ... ] }; tolerate a bare array too.
        var items = doc.RootElement.ValueKind == JsonValueKind.Array
            ? doc.RootElement
            : doc.RootElement.TryGetProperty("payload", out var payload) ? payload : default;
        if (items.ValueKind != JsonValueKind.Array)
        {
            return refs;
        }

        foreach (var workout in items.EnumerateArray())
        {
            // Filter to diving workouts when the activity id is present.
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

    public override async Task<ImportedDive> FetchProfileAsync(DiveRef diveRef, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(diveRef);
        var token = await RequireTokenAsync(ct).ConfigureAwait(false);

        var response = await Http.SendAsync(new HttpExchangeRequest(
            "GET", $"{ExportFitUrl}/{Uri.EscapeDataString(diveRef.SourceId)}",
            new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}", ["Accept"] = "application/octet-stream" }),
            ct).ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            throw new InvalidOperationException($"Suunto FIT export failed for workout {diveRef.SourceId} (HTTP {response.StatusCode}).");
        }

        var dives = fit.Parse(BinaryPayload.Decode(response.Body));
        if (dives.Count == 0)
        {
            throw new InvalidOperationException($"Suunto FIT for workout {diveRef.SourceId} contained no dive.");
        }

        return dives.Count == 1
            ? dives[0]
            : dives.OrderBy(d => Math.Abs((d.StartUtc - diveRef.StartUtc).Ticks)).First();
    }

    private static DateTime ParseEpochMillis(JsonElement el, string prop)
    {
        if (el.TryGetProperty(prop, out var value) && value.ValueKind == JsonValueKind.Number)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(value.GetInt64()).UtcDateTime;
        }

        return default;
    }
}
