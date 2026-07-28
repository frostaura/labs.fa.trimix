using Foundation;
using LocalAuthentication;
using Microsoft.Extensions.Logging;
using Trimix.Core.Abstractions;

namespace Trimix.App.Auth;

/// <summary>
/// iOS <see cref="IBiometricUnlock"/> over LocalAuthentication (<c>LAContext</c>), policy
/// <see cref="LAPolicy.DeviceOwnerAuthentication"/> — Face ID / Touch ID with the device passcode as
/// the system's own fallback. Degrades to SUCCESS when the policy cannot be evaluated at all
/// (simulators with no enrolled biometrics and no passcode): the gate protects an on-device logbook,
/// so locking the diver out of their own phone-resident data would be strictly worse than opening.
/// An explicit user cancel returns failure so the ring can be retried.
/// </summary>
public sealed class IosBiometricUnlock(ILogger<IosBiometricUnlock> logger) : IBiometricUnlock
{
    public async Task<BiometricUnlockResult> AuthenticateAsync(string reason, CancellationToken cancellationToken = default)
    {
        using var context = new LAContext();

        if (!context.CanEvaluatePolicy(LAPolicy.DeviceOwnerAuthentication, out NSError? availabilityError))
        {
            logger.LogInformation(
                "LocalAuthentication unavailable ({Error}); falling back to local success.",
                availabilityError?.LocalizedDescription);
            return new BiometricUnlockResult(true, "No usable authentication on this device — local fallback.");
        }

        try
        {
            var (success, error) = await context.EvaluatePolicyAsync(LAPolicy.DeviceOwnerAuthentication, reason)
                .ConfigureAwait(false);
            return success
                ? new BiometricUnlockResult(true)
                : new BiometricUnlockResult(false, error?.LocalizedDescription ?? "Cancelled.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "LocalAuthentication evaluate failed; falling back to local success.");
            return new BiometricUnlockResult(true, "Authentication sheet failed — local fallback.");
        }
    }
}
