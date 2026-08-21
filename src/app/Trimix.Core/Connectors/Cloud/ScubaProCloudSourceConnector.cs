using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using Trimix.Contracts.Dtos;
using Trimix.Core.Abstractions;
using Trimix.Core.Connectors.Infra;

namespace Trimix.Core.Connectors.Cloud;

/// <summary>
/// Reverse-engineered ScubaPro / Uwatec personal-export source, reading the diver's own dives from the
/// <b>LogTRAK</b> app's Firebase backend (project <c>logtrak2-production</c>). Implements the contract in
/// <c>docs/reverse-engineering/scubapro-logtrak-api.md</c>, captured from LogTRAK 2 v1.13.1 (iOS 27) and
/// verified live against the founder's own account.
///
/// <para><b>This supersedes the old MySSI-corridor design.</b> ScubaPro does NOT flow through MySSI — LogTRAK
/// has its own Firebase project, and MySSI/SSI is only a <em>push</em> target LogTRAK exports to (the diver's
/// user doc carries an <c>ssi.oauth</c> block, and each dive a <c>lastSendToSsi</c> field). The connector id
/// stays <c>scubapro</c> so the provider list, funnel taxonomy and consent state are unchanged; only the
/// backend it talks to moved.</para>
///
/// <para><b>Firebase, not a proxy capture.</b> Auth is Firebase email/password (id token + refresh token);
/// dives live in Cloud Firestore, collection <c>dives</c>, queryable only when filtered to the diver's own
/// <c>userId</c> (security rules forbid an unfiltered list). The full dive — summary AND the per-sample
/// profile — arrives in one query document, so there is no separate detail call: the list is fetched once and
/// each dive is mapped from the cached document. This is <see cref="LegalPosture.ToSRiskReverseEng"/> and
/// ships dark behind explicit per-user consent + disclosure, like every reverse-engineered source.</para>
/// </summary>
public sealed class ScubaProCloudSourceConnector : GatedCloudSourceConnector
{
    public const string Id = "scubapro";

    // Public identifiers embedded in LogTRAK 2 v1.13.1 (iOS) — not secrets; the Web API key is bundle-locked.
    // Overridable via CloudSourceOptions so a provider-side rotation is absorbed by deploy config, not a release.
    private const string DefaultProjectId = "logtrak2-production";
    private const string DefaultApiKey = "AIzaSyB1RncixeW_92_GtPiLlef7PL1l34mgXcM";
    private const string GmpAppId = "1:367468051507:ios:b01cd5cd7305d94328b746";
    private const string BundleId = "com.uwatec.LogTrak2";

    /// <summary>The Firestore collection and owner field the whole read hinges on.</summary>
    private const string DivesCollection = "dives";
    private const string OwnerField = "userId";

    /// <summary>Refresh this far before nominal expiry rather than racing it.</summary>
    private static readonly TimeSpan ExpirySkew = TimeSpan.FromMinutes(5);

    private readonly ISecureTokenStore _tokens;
    private readonly FirebaseCloudClient _firebase;

    /// <summary>Full dive documents cached from the last list, so <see cref="FetchProfileAsync"/> maps without
    /// a second network call — the profile already arrives inside the list document.</summary>
    private readonly ConcurrentDictionary<string, JsonElement> _diveCache = new(StringComparer.Ordinal);

    public ScubaProCloudSourceConnector(
        IHttpTransport http,
        ISecureTokenStore tokens,
        IConnectorConsentStore consent,
        CloudSourceOptions? options = null)
        : base(http, tokens, consent)
    {
        _tokens = tokens;
        _firebase = new FirebaseCloudClient(http, new FirebaseAppConfig(
            ProjectId: options?.ScubaProFirebaseProjectId ?? DefaultProjectId,
            ApiKey: options?.ScubaProFirebaseApiKey ?? DefaultApiKey,
            GmpAppId: GmpAppId,
            BundleId: BundleId));
    }

    public override ConnectorDescriptor Descriptor { get; } = new(
        ConnectorId: Id,
        Kind: ConnectorKind.CloudApi,
        Legal: LegalPosture.ToSRiskReverseEng,
        Fidelity: ProfileFidelity.FullProfile, // depth + water-temp + tank-pressure per sample from LogTRAK.
        Auth: AuthModel.UserCredential,
        Platforms: Platforms.Both,
        EnabledByDefault: false);

    public override string DisplayName => "ScubaPro (personal export)";

    protected override string Disclosure =>
        "This imports your own ScubaPro / Uwatec dives from your personal LogTRAK account. ScubaPro offers no " +
        "cloud API of its own, so it signs in with your LogTRAK email and password using LogTRAK's private " +
        "app backend — this is NOT an official ScubaPro integration and may breach LogTRAK's Terms of Service. " +
        "Your password is used once to obtain an access token and is never stored; only the token is kept, in " +
        "secure device storage. You can disconnect at any time.";

    protected override async Task<string> ExchangeCredentialsForTokenAsync(
        ConnectorCredentials credentials, CancellationToken ct)
    {
        var session = await _firebase.SignInAsync(credentials.Username, credentials.Password, ct).ConfigureAwait(false);
        return FirebaseTokenEnvelope.From(session).Serialize();
    }

    public override async Task<IReadOnlyList<DiveRef>> ListDivesSinceAsync(
        SyncCursor cursor, CancellationToken ct = default)
    {
        var session = await RequireSessionAsync(ct).ConfigureAwait(false);

        // No server-side orderBy: an equality filter on userId combined with orderBy(startTime) needs a
        // composite index that logtrak2-production does not expose (it 400s). The connector needs no server
        // ordering anyway — it pulls the whole logbook in one query and filters the cursor client-side.
        var documents = await _firebase.QueryOwnedAsync(
            session.IdToken, DivesCollection, OwnerField, session.Uid, ct: ct)
            .ConfigureAwait(false);

        var refs = new List<DiveRef>(documents.Count);
        foreach (var document in documents)
        {
            var fields = document.Fields;
            var start = ReadStartUtc(fields);

            // No server-side "since" was observed; filter the cursor client-side (the query is newest-first).
            if (cursor.Since is { } since && start < since)
            {
                continue;
            }

            _diveCache[document.Id] = fields;
            refs.Add(new DiveRef(Descriptor.ConnectorId, document.Id, start, DurationSec(fields), "ScubaPro"));
        }

        return refs;
    }

    public override async Task<ImportedDive> FetchProfileAsync(DiveRef diveRef, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(diveRef);

        // The list document already carries the full profile. Use the cached one; only re-query if this
        // connector instance never listed (e.g. a fetch after a process restart) so ordering never matters.
        if (!_diveCache.TryGetValue(diveRef.SourceId, out var fields))
        {
            await ListDivesSinceAsync(SyncCursor.None, ct).ConfigureAwait(false);
            if (!_diveCache.TryGetValue(diveRef.SourceId, out fields))
            {
                throw new InvalidOperationException(
                    $"ScubaPro dive {diveRef.SourceId} was not found in the LogTRAK logbook.");
            }
        }

        return MapDive(fields, diveRef);
    }

    /// <summary>Maps one LogTRAK dive document (Firestore typed values) to the canonical <see cref="ImportedDive"/>.</summary>
    private static ImportedDive MapDive(JsonElement fields, DiveRef diveRef)
    {
        var start = ReadStartUtc(fields) is var s && s != default ? s : diveRef.StartUtc;
        var duration = DurationSec(fields) is var d && d > 0 ? d : diveRef.DurationSec;

        var samples = ReadSamples(fields, duration);
        var cylinders = ReadCylinders(fields);

        var maxDepth = FirestoreValue.Double(fields, "depthMetersMax")
                       ?? (samples.Count > 0 ? samples.Max(x => x.DepthM) : 0d);

        return new ImportedDive(
            StartUtc: start,
            DurationSec: duration,
            MaxDepthM: maxDepth,
            AvgDepthM: FirestoreValue.Double(fields, "depthMetersAvg"),
            MinTempC: FirestoreValue.Double(fields, "waterTempCelsiusMin"),
            GasMix: DescribeGasMix(cylinders),
            Samples: samples,
            Cylinders: cylinders.Count > 0 ? cylinders : null,
            // LogTRAK has no per-dive logbook number, bottom time or surface interval we can trust
            // (intervalMinutes was 0 on verified data — semantics unconfirmed), so those stay null.
            DiveTypeLabel: Capitalize(FirestoreValue.String(fields, "diveMode")),
            WaterType: FirestoreValue.Bool(fields, "saltwaterCalibrated") switch
            {
                true => "Salt",
                false => "Fresh",
                null => null,
            },
            Environment: BuildEnvironment(fields),
            Exposure: BuildExposure(fields));
    }

    /// <summary>
    /// Builds the sample profile from LogTRAK's parallel string-encoded channels. Depth is the spine; water
    /// temp, tank pressure and heart rate are aligned by index where present. Sample cadence is derived from
    /// the dive duration over the sample count, because <c>intervalMinutes</c> is not the per-sample rate.
    /// </summary>
    private static List<ProfileSampleDto> ReadSamples(JsonElement fields, int durationSec)
    {
        var depth = FirestoreValue.NumberArray(fields, "depthMetersValues");
        if (depth is not { Length: > 0 })
        {
            return [];
        }

        var temp = FirestoreValue.NumberArray(fields, "waterTempCelsiusValues");
        var heartRate = FirestoreValue.NumberArray(fields, "heartRateBpmValues");

        // Tank pressure is a per-tank map of string arrays; the primary cylinder is tank1.
        var tankPressure = FirestoreValue.NumberArray(FirestoreValue.Map(fields, "tankPressureValues"), "tank1");

        var count = depth.Length;
        var step = count > 1 ? durationSec / (double)(count - 1) : 0d;
        var samples = new List<ProfileSampleDto>(count);

        for (var i = 0; i < count; i++)
        {
            samples.Add(new ProfileSampleDto(
                TSeconds: (int)Math.Round(i * step),
                DepthM: depth[i],
                TempC: At(temp, i),
                PpO2: null,
                TankPressureBar: At(tankPressure, i),
                HeartRate: At(heartRate, i)));
        }

        return samples;

        static double? At(double[]? channel, int index) =>
            channel is not null && index < channel.Length && !double.IsNaN(channel[index]) ? channel[index] : null;
    }

    /// <summary>Maps <c>tankData.tank1..tank10</c> — each non-null tank is one cylinder.</summary>
    private static List<CylinderDto> ReadCylinders(JsonElement fields)
    {
        var tankData = FirestoreValue.Map(fields, "tankData");
        var cylinders = new List<CylinderDto>();
        if (tankData.ValueKind != JsonValueKind.Object)
        {
            return cylinders;
        }

        for (var i = 1; i <= 10; i++)
        {
            var tank = FirestoreValue.Map(tankData, $"tank{i}");
            if (tank.ValueKind != JsonValueKind.Object)
            {
                continue; // null tank slot
            }

            var o2 = FirestoreValue.Double(tank, "o2Mixture");
            var start = FirestoreValue.Double(tank, "startPressure");
            var end = FirestoreValue.Double(tank, "endPressure");
            if (o2 is null && start is null && end is null)
            {
                continue; // an empty tank object carries nothing worth showing
            }

            cylinders.Add(new CylinderDto(
                O2: o2,
                He: FirestoreValue.Double(tank, "heMixture"),
                StartBar: start,
                EndBar: end,
                VolumeL: FirestoreValue.Double(tank, "size")));
        }

        return cylinders;
    }

    private static DiveEnvironmentDto? BuildEnvironment(JsonElement fields)
    {
        var environment = new DiveEnvironmentDto(
            MaxTempC: FirestoreValue.Double(fields, "waterTempCelsiusMax"),
            EntryLatitude: FirestoreValue.Double(fields, "locationLat"),
            EntryLongitude: FirestoreValue.Double(fields, "locationLng"));

        return environment == new DiveEnvironmentDto() ? null : environment;
    }

    private static DiveExposureDto? BuildExposure(JsonElement fields)
    {
        var exposure = new DiveExposureDto(
            CnsStartPercent: FirestoreValue.Double(fields, "cnsO2Before"),
            CnsEndPercent: FirestoreValue.Double(fields, "cnsO2After"));

        return exposure == new DiveExposureDto() ? null : exposure;
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

    /// <summary>
    /// Returns a live session, refreshing the id token first if it has expired. The refresh token may rotate,
    /// so the merged envelope is written back. A failed refresh clears the stored token and asks the diver to
    /// sign in again rather than failing opaquely.
    /// </summary>
    private async Task<FirebaseSession> RequireSessionAsync(CancellationToken ct)
    {
        var stored = await RequireTokenAsync(ct).ConfigureAwait(false);
        var envelope = FirebaseTokenEnvelope.TryParse(stored)
            ?? throw new ConnectorAuthException("Your ScubaPro session is unreadable. Please sign in again.");

        if (DateTimeOffset.UtcNow < envelope.ExpiresAt - ExpirySkew)
        {
            return envelope.ToSession();
        }

        if (string.IsNullOrEmpty(envelope.RefreshToken))
        {
            await _tokens.RemoveAsync(TokenKey, ct).ConfigureAwait(false);
            throw new ConnectorAuthException("Your ScubaPro session expired. Please sign in again.");
        }

        try
        {
            var refreshed = await _firebase.RefreshAsync(envelope.RefreshToken, ct).ConfigureAwait(false);
            await _tokens.SetAsync(TokenKey, FirebaseTokenEnvelope.From(refreshed).Serialize(), ct).ConfigureAwait(false);
            return refreshed;
        }
        catch (ConnectorAuthException)
        {
            await _tokens.RemoveAsync(TokenKey, ct).ConfigureAwait(false);
            throw new ConnectorAuthException("Your ScubaPro session expired. Please sign in again.");
        }
    }

    /// <summary>LogTRAK stores timestamps as ISO-8601 UTC strings (e.g. <c>2024-11-02T07:37:50.000Z</c>).</summary>
    private static DateTime ReadStartUtc(JsonElement fields)
    {
        if (FirestoreValue.String(fields, "startTime") is { Length: > 0 } value
            && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return parsed.UtcDateTime;
        }

        return default;
    }

    /// <summary>Duration is the span from <c>startTime</c> to <c>endTime</c> — LogTRAK has no scalar duration field.</summary>
    private static int DurationSec(JsonElement fields)
    {
        var start = ReadStartUtc(fields);
        if (start == default
            || FirestoreValue.String(fields, "endTime") is not { Length: > 0 } endText
            || !DateTimeOffset.TryParse(endText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var end))
        {
            return 0;
        }

        var seconds = (end.UtcDateTime - start).TotalSeconds;
        return seconds > 0 ? (int)Math.Round(seconds) : 0;
    }

    private static string? Capitalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : char.ToUpperInvariant(value[0]) + value[1..];
}

/// <summary>
/// What is persisted in the single secure-store slot for ScubaPro: the 1-hour Firebase id token, the
/// long-lived refresh token, an absolute expiry, and the Firestore <c>userId</c> (needed to filter the diver's
/// own dives). Serialized as compact JSON so <c>ISecureTokenStore</c>'s string contract is unchanged. Never
/// holds a password.
/// </summary>
internal sealed record FirebaseTokenEnvelope(string IdToken, string? RefreshToken, DateTimeOffset ExpiresAt, string Uid)
{
    public static FirebaseTokenEnvelope From(FirebaseSession session) =>
        new(session.IdToken, session.RefreshToken, session.ExpiresAt, session.Uid);

    public FirebaseSession ToSession() => new(IdToken, RefreshToken, ExpiresAt, Uid);

    public string Serialize() => JsonSerializer.Serialize(new Payload
    {
        Id = IdToken,
        Refresh = RefreshToken,
        ExpiresAtUnix = ExpiresAt.ToUnixTimeSeconds(),
        Uid = Uid,
    });

    public static FirebaseTokenEnvelope? TryParse(string stored)
    {
        if (string.IsNullOrWhiteSpace(stored) || stored.TrimStart() is not ['{', ..])
        {
            return null;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<Payload>(stored);
            return payload?.Id is { Length: > 0 } id && payload.Uid is { Length: > 0 } uid
                ? new FirebaseTokenEnvelope(id, payload.Refresh, DateTimeOffset.FromUnixTimeSeconds(payload.ExpiresAtUnix), uid)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed class Payload
    {
        public string? Id { get; set; }

        public string? Refresh { get; set; }

        public long ExpiresAtUnix { get; set; }

        public string? Uid { get; set; }
    }
}
