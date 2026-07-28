using Android.Content;
using Android.Hardware.Biometrics;
using Android.OS;
using Microsoft.Extensions.Logging;
using Trimix.Core.Abstractions;

namespace Trimix.App.Auth;

/// <summary>
/// Android <see cref="IBiometricUnlock"/> over the platform <c>android.hardware.biometrics.BiometricPrompt</c>
/// (API 28+; no extra NuGet binding required). Degrades to SUCCESS whenever the prompt cannot run —
/// API &lt; 28, no biometric hardware, nothing enrolled (emulators), or a prompt error that is not an
/// explicit user cancel — because the gate protects an on-device logbook, not a server account:
/// locking the diver out of their own phone-resident data would be strictly worse than opening.
/// Explicit user cancel/negative-button returns a failure so the ring can be retried.
/// </summary>
public sealed class AndroidBiometricUnlock(ILogger<AndroidBiometricUnlock> logger) : IBiometricUnlock
{
    public async Task<BiometricUnlockResult> AuthenticateAsync(string reason, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(28))
        {
            return new BiometricUnlockResult(true, "Biometric prompt unavailable below API 28 — local fallback.");
        }

        var context = Platform.CurrentActivity ?? Android.App.Application.Context;

        // API 29+: ask the BiometricManager first so no-hardware / none-enrolled devices (emulators)
        // fall through to success without flashing an error prompt.
        if (OperatingSystem.IsAndroidVersionAtLeast(29)
            && context.GetSystemService(Context.BiometricService) is BiometricManager manager
            && manager.CanAuthenticate() != BiometricCode.Success)
        {
            return new BiometricUnlockResult(true, "No usable biometrics on this device — local fallback.");
        }

        try
        {
            var executor = context.MainExecutor;
            if (executor is null)
            {
                return new BiometricUnlockResult(true, "No main executor — local fallback.");
            }

            var tcs = new TaskCompletionSource<BiometricUnlockResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var cancelSignal = new CancellationSignal();
            using var registration = cancellationToken.Register(cancelSignal.Cancel);

            var prompt = new BiometricPrompt.Builder(context)
                .SetTitle("Unlock Trimix")
                .SetDescription(reason)
                .SetNegativeButton("Cancel", executor, new NegativeButtonListener(tcs))
                .Build();

            prompt.Authenticate(cancelSignal, executor, new Callback(tcs));
            return await tcs.Task.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // A broken prompt must never lock the diver out of on-device data.
            logger.LogWarning(ex, "BiometricPrompt failed; falling back to local success.");
            return new BiometricUnlockResult(true, "Biometric prompt failed — local fallback.");
        }
    }

    private sealed class Callback(TaskCompletionSource<BiometricUnlockResult> tcs) : BiometricPrompt.AuthenticationCallback
    {
        public override void OnAuthenticationSucceeded(BiometricPrompt.AuthenticationResult? result) =>
            tcs.TrySetResult(new BiometricUnlockResult(true));

        public override void OnAuthenticationError(BiometricErrorCode errorCode, Java.Lang.ICharSequence? errString)
        {
            // Explicit user dismissals fail (retryable); environmental errors fall back to success.
            // (The negative button completes via NegativeButtonListener, not an error code.)
            var userDismissed = errorCode is BiometricErrorCode.UserCanceled
                or BiometricErrorCode.Canceled;
            tcs.TrySetResult(userDismissed
                ? new BiometricUnlockResult(false, errString?.ToString() ?? "Cancelled.")
                : new BiometricUnlockResult(true, $"Biometric error {errorCode} — local fallback."));
        }

        // OnAuthenticationFailed = a non-matching finger/face; the prompt stays up for a retry, so
        // do not complete the task here.
    }

    private sealed class NegativeButtonListener(TaskCompletionSource<BiometricUnlockResult> tcs)
        : Java.Lang.Object, IDialogInterfaceOnClickListener
    {
        public void OnClick(IDialogInterface? dialog, int which) =>
            tcs.TrySetResult(new BiometricUnlockResult(false, "Cancelled."));
    }
}
