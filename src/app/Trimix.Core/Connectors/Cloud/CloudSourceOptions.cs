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
    // ── Garmin Connect (garth-style SSO → OAuth exchange) ──
    public string? GarminSigninUrl { get; set; }

    public string? GarminTokenExchangeUrl { get; set; }

    public string? GarminActivitySearchUrl { get; set; }

    public string? GarminDownloadUrl { get; set; }

    // ── Suunto app cloud (ROPC token → workouts → FIT export) ──
    public string? SuuntoTokenUrl { get; set; }

    public string? SuuntoWorkoutsUrl { get; set; }

    public string? SuuntoExportFitUrl { get; set; }

    // ── Oceanic+ (Huish) — endpoints UNPUBLISHED; placeholders until legal/BD confirm ──
    public string? OceanicLoginUrl { get; set; }

    public string? OceanicDivesUrl { get; set; }

    // ── MySSI (DiveSSI) shared cloud — the READ corridor serving BOTH the Mares and ScubaPro sources
    //    (neither vendor has a cloud API of its own; both flow into MySSI). Consumed by MySsiCloudClient. ──
    public string? MySsiLoginUrl { get; set; }

    public string? MySsiDivesUrl { get; set; }
}
