using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trimix.Core.Abstractions;

namespace Trimix.Core.ViewModels;

/// <summary>
/// First-run onboarding (PRD Flow A: 3 screens — what it is, site-DB value, computer-import value).
/// Completing (or skipping) onboarding fires <c>first_run_completed</c> exactly once; the
/// <c>install</c> event is emitted by app startup (see AppStartupService), not here.
/// </summary>
public partial class OnboardingViewModel(IAnalytics analytics, INavigator navigator) : ObservableObject
{
    public const int PageCount = 3;

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

    [RelayCommand]
    private Task SkipAsync() => CompleteAsync();

    /// <summary>
    /// Storyboard S3 "Continue as guest": completes onboarding (fires <c>first_run_completed</c>
    /// exactly as the other completion paths) then enters guest mode (local-only) at the logbook.
    /// Navigation-only; no new analytics beyond the shared first-run completion.
    /// </summary>
    [RelayCommand]
    private async Task ContinueAsGuestAsync()
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
