using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trimix.Contracts;
using Trimix.Contracts.Dtos;
using Trimix.Core.Abstractions;

namespace Trimix.Core.ViewModels;

/// <summary>
/// Site detail — serverless Phase-1 stub. The server-backed site DB and the moderated contribution
/// flow are removed; until Phase 3 feeds this from the bundled on-device atlas there is no site
/// source, so load reports unavailable and returns. <c>site_viewed</c> still fires when a site
/// loads (frozen taxonomy); the <c>site_contribute</c> paywall trigger constant stays frozen but is
/// no longer emitted (contribution UI removed).
/// </summary>
public partial class SiteDetailViewModel(
    IAnalytics analytics,
    INavigator navigator,
    IUserDialogs dialogs) : ObservableObject
{
    [ObservableProperty]
    private SiteDetailDto? _site;

    /// <summary>Back navigation, wired to the page's Back button.</summary>
    [RelayCommand]
    private Task Close() => navigator.GoBackAsync();

    [RelayCommand]
    private async Task LoadAsync(Guid siteId)
    {
        // Serverless Phase 1: no on-device site atlas yet (Phase 3 bundles one). Site stays null.
        if (Site is null)
        {
            await dialogs.AlertAsync("Site unavailable", "Site details are not available in this build.");
            await navigator.GoBackAsync();
            return;
        }

        analytics.Track(FunnelEvents.SiteViewed, new Dictionary<string, string>
        {
            [FunnelEvents.Props.SiteId] = siteId.ToString("N"),
        });
    }
}
