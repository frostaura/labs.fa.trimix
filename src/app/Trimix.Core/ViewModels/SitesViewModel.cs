using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trimix.Contracts.Dtos;
using Trimix.Core.Abstractions;

namespace Trimix.Core.ViewModels;

/// <summary>
/// Shared site DB, read path (PRD Flow C): search field + list with distance from the device
/// (no map control in the slice — architecture overview §10). Location is requested just-in-time,
/// "while using" only.
/// </summary>
public partial class SitesViewModel(
    IApiClient api,
    ILocationService location,
    INavigator navigator) : ObservableObject
{
    [ObservableProperty]
    private string _query = "";

    [ObservableProperty]
    private IReadOnlyList<SiteSummaryDto> _sites = [];

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isOffline;

    [RelayCommand]
    private async Task SearchAsync()
    {
        IsBusy = true;
        try
        {
            var fix = await location.GetCurrentLocationAsync();
            var response = await api.SearchSitesAsync(
                string.IsNullOrWhiteSpace(Query) ? null : Query.Trim(),
                fix?.Lat,
                fix?.Lng,
                50);
            Sites = response.Sites;
            IsOffline = false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Sites = [];
            IsOffline = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task OpenSiteAsync(SiteSummaryDto site) =>
        navigator.GoToAsync(Routes.SiteDetail, new Dictionary<string, object> { [Routes.Params.SiteId] = site.Id });
}
