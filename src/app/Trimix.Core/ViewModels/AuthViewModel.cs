using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trimix.Core.Abstractions;

namespace Trimix.Core.ViewModels;

/// <summary>
/// The account step (prototype S3): a biometric-only LOCAL unlock — "Yours, on this phone." No
/// cloud, no servers, no passkeys, no email. The ring button drives
/// <see cref="IBiometricUnlock.AuthenticateAsync"/> (iOS LocalAuthentication / Android
/// BiometricPrompt; dev fallback succeeds on emulators) and lands on the logbook with the
/// "Signed in on-device" toast. The frozen <c>account_created</c> funnel constant remains in the
/// taxonomy but is no longer emitted (no server account exists to create).
/// </summary>
public partial class AuthViewModel(
    IBiometricUnlock biometrics,
    INavigator navigator,
    IUserDialogs dialogs) : ObservableObject
{
    /// <summary>Ring-button label: "Continue with biometrics" → "Authenticating…" while the sheet is up.</summary>
    public string AuthLabel => IsAuthenticating ? "Authenticating…" : "Continue with biometrics";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AuthLabel))]
    private bool _isAuthenticating;

    /// <summary>Back navigation, wired to the page's "← Back".</summary>
    [RelayCommand]
    private Task Close() => navigator.GoBackAsync();

    [RelayCommand]
    private async Task AuthenticateAsync()
    {
        if (IsAuthenticating)
        {
            return;
        }

        IsAuthenticating = true;
        try
        {
            var result = await biometrics.AuthenticateAsync(
                "Unlock your logbook — everything stays on this phone.");
            if (!result.Succeeded)
            {
                return; // cancelled/failed — stay here so the diver can retry the ring
            }

            await navigator.GoToAsync(Routes.Logbook);
            await dialogs.ToastAsync("Signed in on-device");
        }
        finally
        {
            IsAuthenticating = false;
        }
    }
}
