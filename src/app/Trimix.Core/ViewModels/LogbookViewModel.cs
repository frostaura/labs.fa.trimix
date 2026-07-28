using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trimix.Contracts;
using Trimix.Contracts.Dtos;
using Trimix.Core.Abstractions;
using Trimix.Core.Connectors;
using Trimix.Core.Connectors.Infra;
using Trimix.Core.Entitlements;
using Trimix.Core.Formatting;
using Trimix.Core.Sites;
using Trimix.Core.Stats;

namespace Trimix.Core.ViewModels;

/// <summary>
/// One rendered logbook row (prototype C3): sequence number · site · "location · date" ·
/// origin→destination route string (<c>GARMIN → PADI · SSI</c>) · depth · minutes · gas tag.
/// <see cref="Merged"/> keeps the underlying presentation union (badges, tap target).
/// </summary>
public sealed record DiveRowItem(
    MergedDive Merged,
    string Number,
    string Site,
    string Meta,
    string Route,
    string DepthText,
    string TimeText,
    string Gas);

/// <summary>
/// Logbook home (prototype C3): stat strip DIVES · MAX (unit-aware) · BOTTOM hours, dive rows with
/// the origin→destination route string, free-tier cap meter at/over the cap, `+` add sheet and the
/// streaming sync sheet. Stats totals cover ALL dives; the free logbook-visibility cap only
/// truncates the list. The cap meter's upgrade action is a <c>paywall_shown</c> purchase moment
/// with <c>trigger=logbook_cap</c>.
///
/// <para>The visible list is a <see cref="DiveMergePolicy"/> presentation-layer union so one
/// physical dive arriving from several provider sources shows as a single
/// badged card. Merge is display-only: the free-tier cap and every lifetime aggregate still count
/// the RAW active dives (never the merged count) so entitlement semantics are unchanged.</para>
/// </summary>
public partial class LogbookViewModel(
    ILocalStore store,
    IEntitlementService entitlements,
    IAnalytics analytics,
    INavigator navigator,
    SyncSessionViewModel sync,
    UnitFormatter units,
    SiteAtlas atlas,
    IConnectorConsentStore consent,
    IEnumerable<IDiveDestinationConnector> destinations) : ObservableObject
{
    /// <summary>The shared sync session — the page's streaming sync bottom sheet binds to it.</summary>
    public SyncSessionViewModel Sync { get; } = sync;

    /// <summary>The `+` FAB's add sheet (prototype: exactly "Sync now" + "Manage providers").</summary>
    [ObservableProperty]
    private bool _isAddSheetOpen;

    [RelayCommand]
    private void OpenAddSheet() => IsAddSheetOpen = true;

    [RelayCommand]
    private void CloseAddSheet() => IsAddSheetOpen = false;

    /// <summary>Add-sheet "Sync now" (routes to Providers with the red nudge when nothing is linked).</summary>
    [RelayCommand]
    private Task SyncNowAsync()
    {
        IsAddSheetOpen = false;
        return Sync.SyncNowCommand.ExecuteAsync(null);
    }

    /// <summary>Add-sheet "Manage providers" + the empty state's "Connect your computer or dive center".</summary>
    [RelayCommand]
    private Task ManageProvidersAsync()
    {
        IsAddSheetOpen = false;
        return navigator.GoToAsync(Routes.Providers);
    }

    /// <summary>
    /// The rows to render, cross-source duplicates collapsed to one badged entry. Newest first.
    /// Display-only — see the type remarks on the cap counting raw dives.
    /// </summary>
    [ObservableProperty]
    private IReadOnlyList<DiveRowItem> _visibleDives = [];

    [ObservableProperty]
    private int _totalDives;

    [ObservableProperty]
    private double _maxDepthM;

    [ObservableProperty]
    private int _totalBottomTimeSec;

    /// <summary>Stat strip MAX numeral, unit-aware (prototype: "34.1" m / "112" ft).</summary>
    [ObservableProperty]
    private string _statMaxText = "—";

    /// <summary>Stat strip MAX caption: "MAX m" / "MAX ft" per the units preference.</summary>
    [ObservableProperty]
    private string _statMaxLabel = "MAX m";

    /// <summary>Stat strip BOTTOM numeral: total bottom time in whole hours, e.g. "4h".</summary>
    [ObservableProperty]
    private string _statHoursText = "—";

    [ObservableProperty]
    private bool _isEmpty = true;

    [ObservableProperty]
    private bool _isCapMeterVisible;

    [ObservableProperty]
    private int _hiddenDiveCount;

    /// <summary>Fraction of the free logbook cap in use (drives the cap meter).</summary>
    [ObservableProperty]
    private double _capProgress;

    [RelayCommand]
    private async Task LoadAsync()
    {
        var all = await store.GetDivesAsync();
        var active = all
            .Where(d => !d.Deleted)
            .OrderByDescending(d => d.StartUtc)
            .ToList();

        // Lifetime strip aggregates are over PHYSICAL dives: collapse cross-source duplicates first so the
        // strip (and the "You" screen) agree with the merged cards below — one dive synced from two sources
        // must not read as two here while showing once in the list. The free-tier CAP below still counts RAW
        // active dives (binding entitlement rule): merge never feeds the gate.
        var summary = DiveStats.Summarize(DiveMergePolicy.Merge(active).Select(m => m.Primary).ToList());
        TotalDives = summary.TotalDives;
        MaxDepthM = summary.MaxDepthM;
        TotalBottomTimeSec = summary.TotalBottomTimeSec;
        IsEmpty = active.Count == 0;

        // Stat strip (prototype): MAX as a bare unit-aware numeral, BOTTOM as whole hours.
        var isFeet = units.DepthUnit == DepthUnits.Feet;
        StatMaxLabel = isFeet ? "MAX ft" : "MAX m";
        StatMaxText = active.Count == 0
            ? "—"
            : isFeet
                ? Math.Round(summary.MaxDepthM * UnitFormatter.FeetPerMeter).ToString("0", CultureInfo.InvariantCulture)
                : summary.MaxDepthM.ToString("0.0", CultureInfo.InvariantCulture);
        StatHoursText = active.Count == 0
            ? "—"
            : string.Create(CultureInfo.InvariantCulture, $"{Math.Round(summary.TotalBottomTimeSec / 3600.0):0}h");

        var tier = entitlements.CurrentTier;
        // Cap math is on RAW active dives — merge never feeds the entitlement gate.
        var visibleCount = EntitlementGate.LogbookVisibleCount(tier, active.Count);
        var visibleRaw = active.Take(visibleCount).ToList();
        // Presentation union: collapse cross-source duplicates to one badged row, newest first.
        var merged = DiveMergePolicy.Merge(visibleRaw)
            .OrderByDescending(m => m.Primary.StartUtc)
            .ToList();

        // Route string destinations: the linked push providers (PADI · SSI), shared by every row.
        var linkedDestinations = destinations
            .Where(d => consent.HasConsent(d.Descriptor.ConnectorId))
            .Select(d => ProviderCatalog.ShortLabelFor(d.Descriptor.ConnectorId))
            .ToList();
        var destinationSuffix = linkedDestinations.Count > 0
            ? " → " + string.Join(" · ", linkedDestinations)
            : "";

        var rows = new List<DiveRowItem>(merged.Count);
        for (var i = 0; i < merged.Count; i++)
        {
            var m = merged[i];
            var d = m.Primary;
            var site = d.SiteId is Guid siteId ? atlas.GetById(siteId) : null;
            var date = d.StartUtc.ToString("d MMM", CultureInfo.InvariantCulture);
            rows.Add(new DiveRowItem(
                m,
                (merged.Count - i).ToString(CultureInfo.InvariantCulture),
                site?.Name ?? "Open water",
                site is null ? date : $"{site.Location} · {date}",
                ProviderCatalog.ShortLabelFor(d.SourceVendor) + destinationSuffix,
                units.FormatDepth(d.MaxDepthM),
                string.Create(CultureInfo.InvariantCulture, $"{Math.Round(d.DurationSec / 60.0):0}min"),
                d.GasMix ?? "AIR"));
        }

        VisibleDives = rows;
        HiddenDiveCount = EntitlementGate.LogbookHiddenCount(tier, active.Count);
        CapProgress = EntitlementGate.IsPaid(tier)
            ? 0
            : Math.Min(1.0, (double)active.Count / FreeTier.FreeLogbookVisibleDives);
        // Free tier shows the cap meter once the cap is reached or exceeded ("keeps your last 5").
        IsCapMeterVisible = !EntitlementGate.IsPaid(tier)
            && active.Count >= FreeTier.FreeLogbookVisibleDives;
    }

    [RelayCommand]
    private Task OpenDiveAsync(DiveDto dive) =>
        navigator.GoToAsync(Routes.DiveDetail, new Dictionary<string, object> { [Routes.Params.DiveId] = dive.Id });

    /// <summary>Purchase moment from the cap meter (frozen trigger <c>logbook_cap</c>).</summary>
    [RelayCommand]
    private Task UpgradeAsync()
    {
        analytics.Track(FunnelEvents.PaywallShown, new Dictionary<string, string>
        {
            [FunnelEvents.Props.Trigger] = FunnelEvents.Values.TriggerLogbookCap,
            [FunnelEvents.Props.EntitlementState] = FunnelEvents.Values.EntitlementStateFree,
        });
        return navigator.GoToAsync(Routes.Paywall);
    }
}
