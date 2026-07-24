namespace Trimix.Core.Connectors.Push;

/// <summary>
/// Endpoints for the gated PADI personal-export push. Research finding: PADI publishes NO logbook write
/// API and the only ingest it ever accepted (legacy ScubaEarth DL7 bulk upload) is dead — so there is no
/// sanctioned endpoint to hardcode. Both URLs default null: the connector ships dark and reports
/// <c>Unsupported</c> until legal/BD supply a (reverse-engineered) endpoint. Never inline a guessed URL.
/// </summary>
public sealed class PadiPushOptions
{
    /// <summary>Credential-&gt;token exchange URL. Null keeps the connector unconfigured (dark).</summary>
    public string? AuthUrl { get; set; }

    /// <summary>Dive-write URL. Null =&gt; <c>PushDiveAsync</c> returns <c>Unsupported</c> (no sanctioned path).</summary>
    public string? PushUrl { get; set; }
}

/// <summary>
/// Endpoints for the gated SSI personal-export push. Research finding: SSI (MySSI, Mares-owned) publishes
/// no partner write API; the only artifacts are a diver-mediated QR import (summary-only — see
/// <see cref="SsiQrPayloadBuilder"/>) and a private DiveSSI cloud API that takes credentials as query
/// params. No documented write endpoint exists — both URLs default null (dark). Never inline a guessed URL.
/// </summary>
public sealed class SsiPushOptions
{
    /// <summary>Credential-&gt;token exchange URL (private DiveSSI cloud; l/p query params). Null =&gt; dark.</summary>
    public string? AuthUrl { get; set; }

    /// <summary>Summary dive-write URL. Null =&gt; <c>PushDiveAsync</c> returns <c>Unsupported</c>.</summary>
    public string? PushUrl { get; set; }
}
