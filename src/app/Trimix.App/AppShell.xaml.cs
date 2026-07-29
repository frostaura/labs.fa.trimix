using Trimix.App.Pages;
using Trimix.Core;
using Trimix.Core.ViewModels;

namespace Trimix.App;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        RegisterModalRoutes();
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
        Routing.RegisterRoute(Routes.SiteDetail, typeof(SiteDetailPage));
        Routing.RegisterRoute(Routes.Paywall, typeof(PaywallPage));
        // Providers (prototype S5) — the connector registry, pushed from the add sheet / You tab —
        // and the per-provider in-app sign-in sheet (S6).
        Routing.RegisterRoute(Routes.Providers, typeof(ConnectedAccountsPage));
        Routing.RegisterRoute(Routes.ProviderOAuth, typeof(ProviderOAuthPage));
    }
}
