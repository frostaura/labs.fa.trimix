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
        Routing.RegisterRoute(Routes.Auth, typeof(AuthPage));
        // Settings moved off the tab bar (the "You" tab is now the StatsPage) and is reached as a
        // pushed route from the StatsPage gear.
        Routing.RegisterRoute(Routes.Settings, typeof(SettingsPage));
        // Import is a pushed flow (no longer a tab) — reached from the Logbook's "Import from computer".
        Routing.RegisterRoute(Routes.Import, typeof(ImportPage));
        Routing.RegisterRoute(Routes.ManualEntry, typeof(ManualEntryPage));
        Routing.RegisterRoute(Routes.DiveDetail, typeof(DiveDetailPage));
        Routing.RegisterRoute(Routes.SiteDetail, typeof(SiteDetailPage));
        Routing.RegisterRoute(Routes.Contribute, typeof(ContributePage));
        Routing.RegisterRoute(Routes.Paywall, typeof(PaywallPage));
        // Slice 1 — export-a-dive modal chooser (reached from DiveDetail's Export header button).
        Routing.RegisterRoute(ExportDiveViewModel.Route, typeof(ExportDivePage));
        // Slice 2 — Connected accounts (connector activation + SSI QR), pushed from Settings.
        Routing.RegisterRoute(ConnectedAccountsViewModel.Route, typeof(ConnectedAccountsPage));
    }
}
