using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trimix.Contracts;
using Trimix.Contracts.Dtos;
using Trimix.Core.Abstractions;

namespace Trimix.Core.ViewModels;

/// <summary>
/// Manual dive entry (PRD Flow A step 4; design language: single scrolling form). Required
/// fields: date, site (a shared-DB match OR a local draft-site name), max depth, duration; optional
/// time-of-day, conditions, gas, gear, buddies, notes, rating (PRD MVP field list). Saves
/// local-first, fires <c>dive_logged_manual</c> with <c>site_matched_bool</c>, best-effort syncs,
/// then navigates to the dive detail with the "import from your computer for accuracy" prompt
/// (PRD Flow A step 5 — the hand-off that feeds Flow B and the purchase moment). Draft-site name and
/// conditions are stored under distinct keys in the dive's ConditionsJson (DiveDto has no dedicated
/// fields for them; site creation goes through moderated contributions, not dive logging).
/// </summary>
public partial class ManualEntryViewModel(
    ILocalStore store,
    IApiClient api,
    ILocationService location,
    IAnalytics analytics,
    ISyncService sync,
    INavigator navigator,
    IUserDialogs dialogs,
    IClock clock) : ObservableObject
{
    [ObservableProperty]
    private DateTime? _date;

    /// <summary>Time-of-day of the dive; combined with <see cref="Date"/> so same-day dives sort by entry time.</summary>
    [ObservableProperty]
    private TimeSpan? _time;

    /// <summary>Free-text conditions (visibility / current / weather), stored in ConditionsJson.</summary>
    [ObservableProperty]
    private string _conditions = "";

    [ObservableProperty]
    private string _siteQuery = "";

    [ObservableProperty]
    private IReadOnlyList<SiteSummaryDto> _siteResults = [];

    [ObservableProperty]
    private SiteSummaryDto? _selectedSite;

    /// <summary>Local "create draft site" fallback when the shared DB has no match.</summary>
    [ObservableProperty]
    private string _draftSiteName = "";

    [ObservableProperty]
    private double? _maxDepthM;

    [ObservableProperty]
    private int? _durationMin;

    [ObservableProperty]
    private string _gasMix = "";

    [ObservableProperty]
    private string _gear = "";

    [ObservableProperty]
    private string _buddies = "";

    [ObservableProperty]
    private string _notes = "";

    [ObservableProperty]
    private int _rating;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Back navigation (cancel), wired to the page's Back button.</summary>
    [RelayCommand]
    private Task Close() => navigator.GoBackAsync();

    [RelayCommand]
    private async Task SearchSitesAsync()
    {
        if (string.IsNullOrWhiteSpace(SiteQuery))
        {
            SiteResults = [];
            return;
        }

        try
        {
            var fix = await location.GetCurrentLocationAsync();
            var response = await api.SearchSitesAsync(SiteQuery.Trim(), fix?.Lat, fix?.Lng, 10);
            SiteResults = response.Sites;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SiteResults = []; // Offline: the draft-site path still works.
        }
    }

    [RelayCommand]
    private void SelectSite(SiteSummaryDto site)
    {
        SelectedSite = site;
        DraftSiteName = "";
    }

    [RelayCommand]
    private void UseDraftSite()
    {
        DraftSiteName = SiteQuery.Trim();
        SelectedSite = null;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var missing = new List<string>(4);
        if (Date is null)
        {
            missing.Add("date");
        }

        if (SelectedSite is null && string.IsNullOrWhiteSpace(DraftSiteName))
        {
            missing.Add("site");
        }

        if (MaxDepthM is null or <= 0)
        {
            missing.Add("max depth");
        }

        if (DurationMin is null or <= 0)
        {
            missing.Add("duration");
        }

        if (missing.Count > 0)
        {
            await dialogs.AlertAsync("Missing required fields", $"Please provide: {string.Join(", ", missing)}.");
            return;
        }

        IsBusy = true;
        try
        {
            // Capture the entry TIME, not just the date, so two dives on the same day are distinct.
            var startLocal = Date!.Value.Date + (Time ?? TimeSpan.Zero);

            var conditions = new Dictionary<string, string>();
            if (!string.IsNullOrWhiteSpace(DraftSiteName))
            {
                conditions["draft_site_name"] = DraftSiteName.Trim();
            }

            if (!string.IsNullOrWhiteSpace(Conditions))
            {
                conditions["conditions"] = Conditions.Trim();
            }

            var dive = new DiveDto(
                Id: Guid.NewGuid(),
                StartUtc: DateTime.SpecifyKind(startLocal, DateTimeKind.Utc),
                DurationSec: DurationMin!.Value * 60,
                MaxDepthM: MaxDepthM!.Value,
                AvgDepthM: null,
                MinTempC: null,
                GasMix: string.IsNullOrWhiteSpace(GasMix) ? null : GasMix.Trim(),
                Source: DiveSources.Manual,
                SourceVendor: null,
                SiteId: SelectedSite?.Id,
                ConditionsJson: conditions.Count == 0 ? null : JsonSerializer.Serialize(conditions),
                GearCsv: string.IsNullOrWhiteSpace(Gear) ? null : Gear.Trim(),
                BuddiesCsv: string.IsNullOrWhiteSpace(Buddies) ? null : Buddies.Trim(),
                Notes: string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim(),
                Rating: Rating is >= 1 and <= 5 ? Rating : null,
                UpdatedAtUtc: clock.UtcNow.UtcDateTime,
                Deleted: false,
                ProfileSamples: []);

            await store.UpsertDiveAsync(dive);
            analytics.Track(FunnelEvents.DiveLoggedManual, new Dictionary<string, string>
            {
                [FunnelEvents.Props.DiveId] = dive.Id.ToString("N"),
                [FunnelEvents.Props.SiteMatchedBool] = SelectedSite is not null ? "true" : "false",
            });
            await sync.SyncAsync(); // No-op for guests/offline — local-first either way.

            // PRD Flow A step 5: land on the dive detail + running stats and nudge toward BLE import.
            await navigator.GoToAsync(Routes.DiveDetail, new Dictionary<string, object> { [Routes.Params.DiveId] = dive.Id });
            await dialogs.ToastAsync("Dive saved. Import from your dive computer for depth-accurate profiles.");
        }
        finally
        {
            IsBusy = false;
        }
    }
}
