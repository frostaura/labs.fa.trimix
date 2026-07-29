using Trimix.Core;
using Trimix.Core.Tests.Fakes;
using Trimix.Core.ViewModels;
using Xunit;

namespace Trimix.Core.Tests.ViewModels;

/// <summary>
/// Onboarding is a single, account-free path: "Get started" advances the panes, and "Continue" on the
/// last pane completes onboarding and opens the logbook directly — there is no biometric/account step
/// to route into any more.
/// </summary>
public sealed class OnboardingViewModelTests
{
    private static (OnboardingViewModel Vm, RecordingAnalytics Analytics, FakeNavigator Navigator) NewSut()
    {
        var analytics = new RecordingAnalytics();
        var navigator = new FakeNavigator();
        return (new OnboardingViewModel(analytics, navigator), analytics, navigator);
    }

    [Fact]
    public async Task Advancing_past_the_last_pane_completes_onboarding_and_opens_the_logbook()
    {
        var (vm, _, navigator) = NewSut();

        // Pane 0 (Welcome) → pane 1 (How it works): "Get started".
        await vm.NextCommand.ExecuteAsync(null);
        Assert.Equal(1, vm.PageIndex);
        Assert.Empty(navigator.Navigations); // not complete yet

        // Pane 1 → complete: "Continue" lands straight on the logbook — no auth step in between.
        await vm.NextCommand.ExecuteAsync(null);

        Assert.Single(navigator.Navigations);
        Assert.Equal(Routes.Logbook, navigator.Navigations[0].Route);
    }

    [Fact]
    public void There_is_no_skip_account_command()
    {
        // "I already have an account" is gone — the account notion was removed entirely.
        Assert.Null(typeof(OnboardingViewModel).GetProperty("SkipCommand"));
    }
}
