using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trimix.Core.Abstractions;

namespace Trimix.Core.ViewModels;

/// <summary>
/// First-run onboarding (prototype S1 Welcome + S2 "How it works"): the hero pane ("Your logbook
/// fills itself." + the SYNCS WITH provider marquee) and the 3-step loop (01 Dive / 02 Sync /
/// 03 Log). "Get started" advances to the loop; "Continue" on the loop completes onboarding and
/// opens the biometric account step; "I already have an account" completes and goes straight to
/// the logbook (local — there is no server account to sign in to). Every completion path fires
/// <c>first_run_completed</c> exactly once; <c>install</c> is emitted by app startup
/// (AppStartupService), not here.
/// </summary>
public partial class OnboardingViewModel(IAnalytics analytics, INavigator navigator) : ObservableObject
{
    public const int PageCount = 2;

    [ObservableProperty]
    private int _pageIndex;

    public bool IsLastPage => PageIndex >= PageCount - 1;

    partial void OnPageIndexChanged(int value) => OnPropertyChanged(nameof(IsLastPage));

    [RelayCommand]
    private async Task NextAsync()
    {
        if (IsLastPage)
        {
            await CompleteAsync();
            return;
        }

        PageIndex++;
    }

    /// <summary>Back from the loop pane to the welcome pane (prototype "← Back").</summary>
    [RelayCommand]
    private void PreviousPane()
    {
        if (PageIndex > 0)
        {
            PageIndex--;
        }
    }

    /// <summary>
    /// "I already have an account": everything is on-device, so there is nothing to sign in to —
    /// complete onboarding (fires <c>first_run_completed</c> exactly as the other completion path)
    /// and land on the logbook.
    /// </summary>
    [RelayCommand]
    private async Task SkipAsync()
    {
        await analytics.EnsureFirstRunCompletedTrackedAsync();
        await navigator.GoToAsync(Routes.Logbook);
    }

    private async Task CompleteAsync()
    {
        await analytics.EnsureFirstRunCompletedTrackedAsync();
        await navigator.GoToAsync(Routes.Auth);
    }
}
