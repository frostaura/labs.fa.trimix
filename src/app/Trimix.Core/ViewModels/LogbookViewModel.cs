using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trimix.Contracts;
using Trimix.Contracts.Dtos;
using Trimix.Core.Abstractions;
using Trimix.Core.Entitlements;
using Trimix.Core.Stats;

namespace Trimix.Core.ViewModels;

/// <summary>
/// Logbook home (design language: stats strip · dive cards · free-tier cap meter · ping-rings
/// empty state). Stats totals cover ALL dives; the free logbook-visibility cap only truncates the
/// list. The cap meter's upgrade action is a <c>paywall_shown</c> purchase moment with
/// <c>trigger=logbook_cap</c>.
///
/// <para>The visible list is a <see cref="DiveMergePolicy"/> presentation-layer union so one
/// physical dive arriving from several sources (BLE + HealthKit + a FIT file) shows as a single
/// badged card. Merge is display-only: the free-tier cap and every lifetime aggregate still count
/// the RAW active dives (never the merged count) so entitlement semantics are unchanged.</para>
/// </summary>
public partial class LogbookViewModel(
    ILocalStore store,
    IEntitlementService entitlements,
    IAnalytics analytics,
    INavigator navigator) : ObservableObject
{
    /// <summary>
    /// The dives to render, cross-source duplicates collapsed to one badged <see cref="MergedDive"/>.
    /// Newest first. Display-only — see the type remarks on the cap counting raw dives.
    /// </summary>
    [ObservableProperty]
    private IReadOnlyList<MergedDive> _visibleDives = [];

    [ObservableProperty]
    private int _totalDives;

    [ObservableProperty]
    private double _maxDepthM;

    [ObservableProperty]
    private int _totalBottomTimeSec;

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

        var tier = entitlements.CurrentTier;
        // Cap math is on RAW active dives — merge never feeds the entitlement gate.
        var visibleCount = EntitlementGate.LogbookVisibleCount(tier, active.Count);
        var visibleRaw = active.Take(visibleCount).ToList();
        // Presentation union: collapse cross-source duplicates to one badged entry, newest first.
        VisibleDives = DiveMergePolicy.Merge(visibleRaw)
            .OrderByDescending(m => m.Primary.StartUtc)
            .ToList();
        HiddenDiveCount = EntitlementGate.LogbookHiddenCount(tier, active.Count);
        CapProgress = EntitlementGate.IsPaid(tier)
            ? 0
            : Math.Min(1.0, (double)active.Count / FreeTier.FreeLogbookVisibleDives);
        // "Free tier shows cap meter near the cap" — within 5 dives of it, or over it.
        IsCapMeterVisible = !EntitlementGate.IsPaid(tier)
            && active.Count >= FreeTier.FreeLogbookVisibleDives - 5;
    }

    [RelayCommand]
    private Task OpenDiveAsync(DiveDto dive) =>
        navigator.GoToAsync(Routes.DiveDetail, new Dictionary<string, object> { [Routes.Params.DiveId] = dive.Id });

    [RelayCommand]
    private Task AddManualAsync() => navigator.GoToAsync(Routes.ManualEntry);

    [RelayCommand]
    private Task ImportAsync() => navigator.GoToAsync(Routes.Import);

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
