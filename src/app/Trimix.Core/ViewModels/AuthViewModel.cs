using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trimix.Contracts.Dtos;
using Trimix.Core.Abstractions;
using Trimix.Core.Api;

namespace Trimix.Core.ViewModels;

/// <summary>
/// Account sign-in (PRD Flow A step 2) — PASSKEY ONLY (no email/password, no OAuth). "Create a
/// passkey" registers a new account bound to a WebAuthn credential; "Sign in with a passkey" does a
/// usernameless assertion. After either: entitlement refresh + once-per-install auto-restore
/// (<see cref="IEntitlementService.OnSignedInAsync"/>), then a dive sync so guest dives upload to the
/// account. The server logs the frozen <c>account_created</c> funnel event on register — the client
/// does not duplicate it. "Continue as guest" stays local-only until a passkey account is created.
/// </summary>
public partial class AuthViewModel(
    IApiClient api,
    IPasskeyAuthService passkeys,
    IAuthSession session,
    IEntitlementService entitlements,
    ISyncService sync,
    IAnalytics analytics,
    INavigator navigator,
    IUserDialogs dialogs) : ObservableObject
{
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Drives whether the passkey buttons are shown (hidden on platforms without an authenticator).</summary>
    public bool IsPasskeyAvailable => passkeys.IsAvailable;

    /// <summary>Back navigation (cancel sign-in), wired to the page's Back button.</summary>
    [RelayCommand]
    private Task Close() => navigator.GoBackAsync();

    /// <summary>Create a new account + passkey (WebAuthn registration).</summary>
    [RelayCommand]
    private Task CreatePasskeyAsync() => RunPasskeyAsync(async () =>
    {
        var begin = await api.PasskeyRegisterBeginAsync(new PasskeyRegisterBeginRequest(null));
        var attestation = await passkeys.CreatePasskeyAsync(begin.CreationOptionsJson);
        return await api.PasskeyRegisterFinishAsync(new PasskeyRegisterFinishRequest(begin.ChallengeId, attestation));
    });

    /// <summary>Sign in with an existing passkey (WebAuthn assertion, usernameless).</summary>
    [RelayCommand]
    private Task SignInWithPasskeyAsync() => RunPasskeyAsync(async () =>
    {
        var begin = await api.PasskeyLoginBeginAsync();
        var assertion = await passkeys.GetPasskeyAsync(begin.RequestOptionsJson);
        return await api.PasskeyLoginFinishAsync(new PasskeyLoginFinishRequest(begin.ChallengeId, assertion));
    });

    [RelayCommand]
    private Task ContinueAsGuestAsync() =>
        // Guest mode: local-only until a passkey account is created (architecture overview §6/§8).
        navigator.GoToAsync(Routes.Logbook);

    private async Task RunPasskeyAsync(Func<Task<AuthResponse>> authenticate)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var auth = await authenticate();
            await session.SignInAsync(auth);
            await entitlements.OnSignedInAsync();
            await sync.SyncAsync();
            await analytics.FlushAsync();
            await navigator.GoToAsync(Routes.Logbook);
        }
        catch (PasskeyCancelledException)
        {
            // User dismissed the system passkey sheet — no error, just abort.
        }
        catch (PasskeyException ex)
        {
            await dialogs.AlertAsync("Passkey unavailable", ex.Message);
        }
        catch (ApiException ex)
        {
            await dialogs.AlertAsync("Sign-in failed", ex.Error.Message);
        }
        catch (HttpRequestException)
        {
            await dialogs.AlertAsync("Offline", "Could not reach the server. Try again, or continue as guest.");
        }
        finally
        {
            IsBusy = false;
        }
    }
}
