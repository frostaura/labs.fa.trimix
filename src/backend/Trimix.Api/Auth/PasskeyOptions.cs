namespace Trimix.Api.Auth;

/// <summary>
/// WebAuthn/FIDO2 relying-party configuration (bound from the <c>Passkey:*</c> configuration section)
/// plus the app-association metadata served from <c>/.well-known</c>.
/// <para>
/// HONESTY (device/domain-dependent): passkeys only work once the app is associated with a real
/// HTTPS relying-party domain — iOS needs the <c>associated-domains</c> entitlement plus a matching
/// <c>apple-app-site-association</c> served from that domain, and Android needs a Digital Asset Links
/// <c>assetlinks.json</c> tied to the app-signing SHA-256. The platform authenticator APIs refuse to
/// mint or assert a credential otherwise, so this flow CANNOT be exercised on a simulator/emulator
/// against a localhost backend. The defaults below (<c>localhost</c> RP id, <c>https://localhost</c>
/// origin) exist only so local/dev and hermetic tests can drive the server relying party; real
/// runtime validation is gated on a production domain + signing certificate (M3-style).
/// </para>
/// </summary>
public sealed class PasskeyOptions
{
    public const string SectionName = "Passkey";

    /// <summary>The relying-party id — the registrable domain (no scheme/port). Defaults to <c>localhost</c>.</summary>
    public string ServerDomain { get; set; } = "localhost";

    /// <summary>Human-readable relying-party name shown by the platform passkey manager.</summary>
    public string ServerName { get; set; } = "Trimix";

    /// <summary>
    /// Allowed WebAuthn origins (the exact value the authenticator stamps in <c>clientDataJSON.origin</c>).
    /// Defaults to the local dev origin; production must list the real HTTPS origin(s).
    /// </summary>
    public HashSet<string> Origins { get; set; } = new(StringComparer.Ordinal) { "https://localhost" };

    /// <summary>
    /// Apple app identifier(s) — <c>&lt;TEAMID&gt;.&lt;bundle-id&gt;</c> — advertised under
    /// <c>webcredentials</c> in the apple-app-site-association document.
    /// </summary>
    public List<string> AppleAppIds { get; set; } = [];

    /// <summary>Android application id (package name) for the Digital Asset Links statement.</summary>
    public string AndroidPackageName { get; set; } = string.Empty;

    /// <summary>Android app-signing certificate SHA-256 fingerprints (colon-delimited hex) for asset links.</summary>
    public List<string> AndroidSha256CertFingerprints { get; set; } = [];
}
