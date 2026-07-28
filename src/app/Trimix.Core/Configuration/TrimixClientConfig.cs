namespace Trimix.Core.Configuration;

/// <summary>
/// External, deploy-time client configuration (serverless): the single surface a deployer edits — via
/// the shipped <c>Resources/Raw/appsettings.json</c> asset — to hand real (legal-cleared) reverse-eng
/// connector endpoints to the dark connectors and flip the connector family master switches. <b>No value
/// here is a secret and none carries a real default</b>: every field is nullable/off so the shipped
/// asset boots against safe dev defaults and a wrong/missing file never bricks startup. The App loads
/// the asset (<c>AppConfigLoader</c>) → <see cref="ClientConfigBinder.Parse"/> → this tree; MauiProgram
/// maps it onto the connector option objects BEFORE <c>AddTrimixCore</c>.
/// </summary>
public sealed class TrimixClientConfig
{
    public ConnectorsConfig Connectors { get; init; } = new();

    public FeaturesConfig Features { get; init; } = new();
}

/// <summary>Reverse-engineered personal-export connector endpoints. All null ⇒ the connectors ship dark.</summary>
public sealed class ConnectorsConfig
{
    public PushEndpointConfig Padi { get; init; } = new();

    public PushEndpointConfig Ssi { get; init; } = new();

    public CloudConfig Cloud { get; init; } = new();
}

/// <summary>Cert-agency push endpoints (PADI/SSI). Both null ⇒ <c>PushDiveAsync</c> reports Unsupported.</summary>
public sealed class PushEndpointConfig
{
    /// <summary>DEPLOY INPUT (legal-gated): credential→token exchange URL. Null keeps the connector dark.</summary>
    public string? AuthUrl { get; init; }

    /// <summary>DEPLOY INPUT (legal-gated): dive-write URL. Null keeps the connector dark.</summary>
    public string? PushUrl { get; init; }
}

/// <summary>Reverse-eng cloud-pull endpoints, one bag per vendor. Mapped onto slice-2's cloud options by the integrator.</summary>
public sealed class CloudConfig
{
    public CloudVendorConfig Garmin { get; init; } = new();

    public CloudVendorConfig Suunto { get; init; } = new();

    public CloudVendorConfig Oceanic { get; init; } = new();

    /// <summary>The shared MySSI (DiveSSI) read corridor serving BOTH the Mares and ScubaPro sources —
    /// neither vendor has a cloud API of its own. AuthUrl ⇒ login; ListUrl ⇒ logbook list/detail base.</summary>
    public CloudVendorConfig MySsi { get; init; } = new();
}

/// <summary>
/// Nullable cloud endpoints for one vendor. All null ⇒ the connector uses its in-source (researched)
/// reverse-eng surface — no value here is guessed, and none is required to boot.
/// </summary>
public sealed class CloudVendorConfig
{
    /// <summary>DEPLOY INPUT: sign-in / SSO URL. Null ⇒ in-source default.</summary>
    public string? AuthUrl { get; init; }

    /// <summary>DEPLOY INPUT: credential→bearer token exchange URL. Null ⇒ in-source default.</summary>
    public string? TokenUrl { get; init; }

    /// <summary>DEPLOY INPUT: dive-list / activity-search URL. Null ⇒ in-source default.</summary>
    public string? ListUrl { get; init; }

    /// <summary>DEPLOY INPUT: profile / original-file download URL. Null ⇒ in-source default.</summary>
    public string? DownloadUrl { get; init; }
}

/// <summary>
/// Master switches for the reverse-engineered connector families. Both default OFF: even with real
/// endpoints configured, a family stays hidden until legal/BD sign-off flips its flag. This is a
/// deploy gate ON TOP OF the per-connector consent gate — not a replacement for it.
/// </summary>
public sealed class FeaturesConfig
{
    public bool EnableCloudConnectors { get; init; }

    public bool EnablePushConnectors { get; init; }
}
