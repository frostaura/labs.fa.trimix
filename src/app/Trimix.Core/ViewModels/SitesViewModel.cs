using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trimix.Core.Abstractions;
using Trimix.Core.Formatting;
using Trimix.Core.Sites;
using Trimix.Core.Stats;

namespace Trimix.Core.ViewModels;

/// <summary>
/// One Sites-tab row (prototype C7): dive-count badge · site name · location · deepest depth.
/// Tapping opens the LATEST dive logged at the site.
/// </summary>
public sealed record SiteRowItem(
    string Name,
    string Location,
    string CountText,
    string MaxDepthText,
    Guid LatestDiveId);

/// <summary>
/// Sites tab (prototype C7) — DERIVED from the user's own synced dives plus the bundled read-only
/// atlas: dives that auto-matched an atlas site aggregate into one row each (count badge, deepest,
/// latest). No server, no browse/search/contribute — the shared community site DB was removed with
/// the backend. Empty state: "No sites yet · Sites appear as dives sync in."
/// </summary>
public partial class SitesViewModel(
    ILocalStore store,
    SiteAtlas atlas,
    UnitFormatter units,
    INavigator navigator) : ObservableObject
{
    [ObservableProperty]
    private IReadOnlyList<SiteRowItem> _sites = [];

    [ObservableProperty]
    private bool _isEmpty = true;

    [RelayCommand]
    private async Task LoadAsync()
    {
        var all = await store.GetDivesAsync();
        var active = all.Where(d => !d.Deleted).ToList();

        // Group PHYSICAL dives (merged, like every display aggregate) by their matched atlas site.
        var dives = DiveMergePolicy.Merge(active).Select(m => m.Primary).ToList();
        var rows = dives
            .Where(d => d.SiteId is not null)
            .GroupBy(d => d.SiteId!.Value)
            .Select(g => (Site: atlas.GetById(g.Key), Dives: g.ToList()))
            .Where(x => x.Site is not null)
            .Select(x =>
            {
                var latest = x.Dives.OrderByDescending(d => d.StartUtc).First();
                return (Row: new SiteRowItem(
                    x.Site!.Name,
                    x.Site.Location,
                    x.Dives.Count.ToString(CultureInfo.InvariantCulture),
                    units.FormatDepth(x.Dives.Max(d => d.MaxDepthM)),
                    latest.Id), LatestUtc: latest.StartUtc);
            })
            .OrderByDescending(x => x.LatestUtc)
            .Select(x => x.Row)
            .ToList();

        Sites = rows;
        IsEmpty = rows.Count == 0;
    }

    /// <summary>Tap → the latest dive at that site.</summary>
    [RelayCommand]
    private Task OpenSiteAsync(SiteRowItem site) =>
        navigator.GoToAsync(Routes.DiveDetail, new Dictionary<string, object> { [Routes.Params.DiveId] = site.LatestDiveId });
}
