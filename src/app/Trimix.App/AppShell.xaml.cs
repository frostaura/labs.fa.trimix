using Trimix.App.Pages;
using Trimix.App.Services;
using Trimix.Core;
using Trimix.Core.ViewModels;

namespace Trimix.App;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        RegisterModalRoutes();

        // Tab-bar taps are native platform views, not TapGestureRecognizers, so the page-level tap hook
        // never sees them — this is what gives tab switching a haptic. Firing on Navigating (the intent)
        // rather than Navigated (arrival) is the whole point: the tick lands under the finger while the
        // destination is still measuring, which is what makes the switch feel instant.
        // Feedback coalesces, so a button press that navigates still produces exactly one tick.
        Navigating += (_, _) => Feedback.Navigate();
    }

    /// <summary>
    /// Modal routes per the architecture doc's App structure. Route names come from Core's
    /// <see cref="Routes"/> constants (shared with the viewmodels' INavigator calls); pages are
    /// resolved through DI (registered in MauiProgram) so their viewmodels constructor-inject.
    /// </summary>
    private static void RegisterModalRoutes()
    {
        Routing.RegisterRoute(Routes.Onboarding, typeof(OnboardingPage));
        Routing.RegisterRoute(Routes.DiveDetail, typeof(DiveDetailPage));
        Routing.RegisterRoute(Routes.Paywall, typeof(PaywallPage));
        // Providers (prototype S5) — the connector registry, pushed from the add sheet / You tab —
        // and the per-provider in-app sign-in sheet (S6).
        Routing.RegisterRoute(Routes.Providers, typeof(ConnectedAccountsPage));
        Routing.RegisterRoute(Routes.ProviderOAuth, typeof(ProviderOAuthPage));
    }
}
