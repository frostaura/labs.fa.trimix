using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trimix.Contracts;
using Trimix.Contracts.Dtos;
using Trimix.Core.Abstractions;
using Trimix.Core.Entitlements;

namespace Trimix.Core.ViewModels;

/// <summary>
/// Site detail (PRD Flow C): depth range, type, hazards, marine life, access notes, approved
/// community notes. Fires <c>site_viewed</c>. "Suggest correction" is Pro-gated — for free users
/// it is a purchase moment (<c>paywall_shown</c> with <c>trigger=site_contribute</c>).
/// </summary>
public partial class SiteDetailViewModel(
    IApiClient api,
    IEntitlementService entitlements,
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
        try
        {
            Site = await api.GetSiteAsync(siteId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await dialogs.AlertAsync("Offline", "Could not load this site right now.");
            await navigator.GoBackAsync();
            return;
        }

        analytics.Track(FunnelEvents.SiteViewed, new Dictionary<string, string>
        {
            [FunnelEvents.Props.SiteId] = siteId.ToString("N"),
        });
    }

    [RelayCommand]
    private async Task ContributeAsync()
    {
        if (Site is null)
        {
            return;
        }

        if (!EntitlementGate.CanContributeSites(entitlements.CurrentTier))
        {
            analytics.Track(FunnelEvents.PaywallShown, new Dictionary<string, string>
            {
                [FunnelEvents.Props.Trigger] = FunnelEvents.Values.TriggerSiteContribute,
                [FunnelEvents.Props.EntitlementState] = FunnelEvents.Values.EntitlementStateFree,
            });
            await navigator.GoToAsync(Routes.Paywall);
            return;
        }

        await navigator.GoToAsync(Routes.Contribute, new Dictionary<string, object> { [Routes.Params.SiteId] = Site.Id });
    }
}
