namespace Trimix.Core.Configuration;

/// <summary>
/// External, deploy-time client configuration (aggregator architecture §9.4). This POCO tree is the
/// single surface a deployer edits — via the shipped <c>Resources/Raw/appsettings.json</c> asset — to
/// point the app at a real API, hand real (legal-cleared) reverse-eng connector endpoints to the dark
/// connectors, override per-vendor BLE GATT UUIDs verified against hardware, and flip the connector
/// family master switches. <b>No value here is a secret and none carries a real default</b>: every
/// field is nullable/off so the shipped asset boots against safe dev defaults and a wrong/missing file
/// never bricks startup. The App loads the asset (<c>AppConfigLoader</c>) → <see cref="ClientConfigBinder.Parse"/>
/// → this tree; <c>MauiProgram</c> maps it onto <c>TrimixCoreOptions</c> + the connector option objects
/// BEFORE <c>AddTrimixCore</c> so no code edit is required to deploy.
/// </summary>
public sealed class TrimixClientConfig
{
    public ApiConfig Api { get; init; } = new();

    public ConnectorsConfig Connectors { get; init; } = new();

    /// <summary>
    /// Per-vendor BLE GATT overrides keyed by vendor name (e.g. <c>"Shearwater"</c>). Absent ⇒ the
    /// in-source BLE profile is used. Slice 3's <c>BleGattOverrides.Apply(...)</c> consumes this at startup.
    /// </summary>
    public Dictionary<string, BleVendorConfig> Ble { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public FeaturesConfig Features { get; init; } = new();
}

/// <summary>Trimix API endpoint. <see cref="BaseUrl"/> null ⇒ the built-in <c>TrimixCoreOptions</c> default.</summary>
public sealed class ApiConfig
{
    /// <summary>DEPLOY INPUT: real API base URL. Null keeps the code default (dev loopback).</summary>
    public string? BaseUrl { get; init; }
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
/// Per-vendor BLE GATT UUID override (aggregator architecture §9.3). Any field null/absent ⇒ the
/// in-source BLE profile value is kept. <b>DEPLOY INPUT:</b> supply only UUIDs / handshake bytes
/// verified against real hardware — never a guessed identifier.
/// </summary>
public sealed class BleVendorConfig
{
    public string? ServiceUuid { get; init; }

    public string? WriteCharUuid { get; init; }

    public string? NotifyCharUuid { get; init; }

    /// <summary>Hex-encoded download handshake command(s), e.g. <c>"a5..."</c>. Null ⇒ in-source handshake.</summary>
    public string? HandshakeCommandsHex { get; init; }
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
