namespace Trimix.Core.Connectors.Cloud;

/// <summary>
/// Config-injectable endpoint overrides for the reverse-engineered cloud-pull source connectors
/// (Garmin / Suunto / Oceanic+). Every field defaults <c>null</c>; a null field means "use the connector's
/// in-source researched default constant". This makes the real endpoints supplied at deploy time
/// (post legal/BD sign-off) WITHOUT a code change and WITHOUT committing a guessed URL — the connectors
/// still ship dark and only ever hit an endpoint after per-user consent regardless of what is configured.
///
/// <para>The App binds this from its client config asset (slice 4's <c>TrimixClientConfig.Connectors.Cloud</c>)
/// and pre-registers a populated instance before <c>AddTrimixCore</c>; unconfigured (the default) leaves the
/// researched constants in force, so existing behaviour and tests are unchanged.</para>
/// </summary>
public sealed class CloudSourceOptions
{
    // ── Garmin Dive (private app API — observed and verified live; see
    //    docs/reverse-engineering/garmin-dive-api.md). These are NOT guesses: the defaults in
    //    GarminCloudSourceConnector are the real endpoints. The overrides remain so a Garmin-side change
    //    can be absorbed by config rather than a release. ──
    public string? GarminLoginUrl { get; set; }

    public string? GarminTokenUrl { get; set; }

    public string? GarminDiveSummaryUrl { get; set; }

    public string? GarminDiveDetailUrl { get; set; }

    /// <summary>The SSO <c>service</c> / <c>service_url</c> both auth legs must agree on.</summary>
    public string? GarminServiceUrl { get; set; }

    // ── Suunto app cloud (ROPC token → workouts → FIT export) ──
    public string? SuuntoTokenUrl { get; set; }

    public string? SuuntoWorkoutsUrl { get; set; }

    public string? SuuntoExportFitUrl { get; set; }

    // ── Oceanic+ (Huish) — endpoints UNPUBLISHED; placeholders until legal/BD confirm ──
    public string? OceanicLoginUrl { get; set; }

    public string? OceanicDivesUrl { get; set; }

    // ── MySSI (DiveSSI) shared cloud — the READ corridor for the Mares source (Mares has no cloud API of
    //    its own; it flows into MySSI). Consumed by MySsiCloudClient. NOTE: ScubaPro was moved OFF this
    //    corridor on 2026-08-07 — LogTRAK has its own Firebase backend (see the ScubaPro* fields below and
    //    docs/reverse-engineering/scubapro-logtrak-api.md). Whether Mares genuinely uses MySSI is now itself
    //    unverified and should be captured before first live contact. ──
    public string? MySsiLoginUrl { get; set; }

    public string? MySsiDivesUrl { get; set; }

    // ── ScubaPro / Uwatec LogTRAK (Firebase project logtrak2-production — Firebase Auth + Cloud Firestore,
    //    verified live; see docs/reverse-engineering/scubapro-logtrak-api.md). The connector's in-source
    //    constants are the REAL, public app identifiers; these overrides exist only so a provider-side
    //    rotation of the project id or Web API key is absorbed by deploy config rather than a release. The
    //    Google endpoint hosts are not overridable here — FirebaseAppConfig owns those. ──
    public string? ScubaProFirebaseProjectId { get; set; }

    public string? ScubaProFirebaseApiKey { get; set; }
}
