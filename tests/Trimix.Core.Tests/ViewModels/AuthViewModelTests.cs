using Trimix.Core;
using Trimix.Core.Tests.Fakes;
using Trimix.Core.ViewModels;
using Xunit;

namespace Trimix.Core.Tests.ViewModels;

/// <summary>
/// The biometric-only local unlock (C2): the ring drives IBiometricUnlock; success lands on the
/// logbook with the "Signed in on-device" toast, cancel/failure stays put for a retry. No server
/// account exists and no account_created event fires.
/// </summary>
public class AuthViewModelTests
{
    private readonly FakeBiometricUnlock _biometrics = new();
    private readonly FakeNavigator _navigator = new();
    private readonly FakeDialogs _dialogs = new();

    private AuthViewModel CreateViewModel() => new(_biometrics, _navigator, _dialogs);

    [Fact]
    public void Idle_label_offers_biometrics()
    {
        var vm = CreateViewModel();

        Assert.Equal("Continue with biometrics", vm.AuthLabel);
    }

    [Fact]
    public async Task Success_opens_the_logbook_with_the_on_device_toast()
    {
        var vm = CreateViewModel();

        await vm.AuthenticateCommand.ExecuteAsync(null);

        Assert.Equal(1, _biometrics.Prompts);
        var nav = Assert.Single(_navigator.Navigations);
        Assert.Equal(Routes.Logbook, nav.Route);
        Assert.Contains("Signed in on-device", _dialogs.Toasts);
        Assert.False(vm.IsAuthenticating);
    }

    [Fact]
    public async Task Cancel_stays_on_the_ring_for_a_retry()
    {
        _biometrics.Result = new Trimix.Core.Abstractions.BiometricUnlockResult(false, "Cancelled.");
        var vm = CreateViewModel();

        await vm.AuthenticateCommand.ExecuteAsync(null);

        Assert.Empty(_navigator.Navigations);
        Assert.Empty(_dialogs.Toasts);
        Assert.False(vm.IsAuthenticating);
        Assert.Equal("Continue with biometrics", vm.AuthLabel); // ready to retry
    }
}
