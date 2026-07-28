namespace Trimix.Core.Abstractions;

/// <summary>Outcome of a local biometric unlock attempt.</summary>
public sealed record BiometricUnlockResult(bool Succeeded, string? Message = null);

/// <summary>
/// The on-device biometric gate (serverless C2): Face ID / Touch ID via iOS LocalAuthentication,
/// fingerprint/face via Android BiometricPrompt. There is no server account behind it — success
/// simply opens the local logbook ("Yours, on this phone"). Implementations MUST degrade gracefully:
/// on hardware with no biometric capability or nothing enrolled (emulators, dev machines) they
/// return success rather than locking the user out of their own on-device data.
/// </summary>
public interface IBiometricUnlock
{
    /// <summary>Prompts the platform biometric sheet. Cancel/failure returns <c>Succeeded=false</c>;
    /// missing hardware/enrollment resolves <c>Succeeded=true</c> (local-trust fallback).</summary>
    Task<BiometricUnlockResult> AuthenticateAsync(string reason, CancellationToken cancellationToken = default);
}
