using Trimix.App.Controls;
using Trimix.App.Services;
using Trimix.Core.ViewModels;

namespace Trimix.App.Pages;

/// <summary>
/// Onboarding (S1 + S2). Provider status is shown as two static wrap rows — SYNCS WITH (supported today) and
/// COMING SOON — that mirror the Providers screen's split (the former auto-scrolling SYNCS WITH marquee was
/// replaced 2026-08-07 so the two states read honestly). Code-behind owns only the per-swipe haptic cue.
/// </summary>
public partial class OnboardingPage : TrimixPage
{
    public OnboardingPage(OnboardingViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    /// <summary>
    /// Swiping between onboarding panes is the same kind of action as tapping the section switcher, so it
    /// gets the same Selection cue. Without this the only gesture on the app's very first screen gave no
    /// feedback at all.
    /// </summary>
    private void OnCarouselPositionChanged(object? sender, PositionChangedEventArgs e) =>
        Feedback.Selection();
}
