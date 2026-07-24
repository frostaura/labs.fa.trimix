namespace Trimix.Core.Abstractions;

/// <summary>
/// Platform passkey (WebAuthn/FIDO2) authenticator. Passkeys are the ONLY account sign-in method
/// (no password, no OAuth). The server (a FIDO2 relying party) issues WebAuthn creation/request
/// options as JSON; this service hands them to the platform authenticator (iOS
/// <c>ASAuthorizationPlatformPublicKeyCredentialProvider</c> / Android Credential Manager),
/// gathers a biometric/screen-lock gesture, and returns the attestation/assertion response as JSON
/// for the server to verify. Implementations: iOS + Android (App/Platforms); a
/// <see cref="Trimix.Core"/> fallback that reports unavailable so DI + tests always resolve.
///
/// Runtime prerequisite (documented in docs/known-gaps.md): passkeys require the app to be
/// associated with a real HTTPS relying-party domain — iOS associated-domains entitlement +
/// server-hosted apple-app-site-association, Android Digital Asset Links (assetlinks.json) tied to
/// the app-signing certificate. Without that association the platform APIs refuse; the flow cannot be
/// exercised against a localhost dev backend on a simulator/emulator.
/// </summary>
public interface IPasskeyAuthService
{
    /// <summary>Whether this platform + build can create/use platform passkeys.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Creates a passkey from the server's WebAuthn <c>PublicKeyCredentialCreationOptions</c> (JSON) and
    /// returns the authenticator's attestation response (JSON) to POST to the register/finish endpoint.
    /// Throws <see cref="PasskeyCancelledException"/> if the user dismisses the system sheet.
    /// </summary>
    Task<string> CreatePasskeyAsync(string creationOptionsJson, CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a (usernameless/discoverable) passkey assertion from the server's WebAuthn
    /// <c>PublicKeyCredentialRequestOptions</c> (JSON) and returns the assertion response (JSON) to POST
    /// to the login/finish endpoint. Throws <see cref="PasskeyCancelledException"/> on user cancel.
    /// </summary>
    Task<string> GetPasskeyAsync(string requestOptionsJson, CancellationToken cancellationToken = default);
}

/// <summary>A passkey operation failed (authenticator error, no credential, malformed options).</summary>
public class PasskeyException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>The user cancelled/dismissed the system passkey sheet.</summary>
public sealed class PasskeyCancelledException() : PasskeyException("The passkey prompt was cancelled.");

/// <summary>Passkeys are not available on this platform/build (used by the Core fallback).</summary>
public sealed class PasskeyUnavailableException()
    : PasskeyException("Passkeys are not available on this device.");
