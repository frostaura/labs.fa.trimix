using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trimix.Core.Abstractions;
using Trimix.Core.Formatting;
using Trimix.Core.Stats;

namespace Trimix.Core.ViewModels;

/// <summary>
/// The "You" surface (aggregator architecture §8.3 / §8.5): career analytics over the local
/// logbook. Every channel-dependent metric is captioned with its data-completeness fraction
/// ("SAC available for 42 of 120 dives") so heterogeneous-fidelity sources (a depth-temp-only
/// HealthKit dive) never silently skew a stat. All values are computed by the pure, static
/// <see cref="DiveStats"/> and unit-formatted for display — storage stays metric.
///
/// <para>Guardrail: every figure is arithmetic over recorded values (depth, pressure, recorded
/// NDL/ceiling). Nothing computes tissue loading or deco. Stats is read-only over the local store
/// and is not a purchase moment — it emits no funnel events.</para>
/// </summary>
public partial class StatsViewModel(
    ILocalStore store,
    UnitFormatter units,
    INavigator navigator) : ObservableObject
{
    [ObservableProperty]
    private bool _isEmpty = true;

    [ObservableProperty]
    private int _totalDives;

    [ObservableProperty]
    private string _maxDepthText = "";

    [ObservableProperty]
    private string _totalBottomTimeText = "";

    [ObservableProperty]
    private string _averageDepthText = "—";

    [ObservableProperty]
    private string _averageDepthCaption = "";

    [ObservableProperty]
    private string _averageDurationText = "—";

    [ObservableProperty]
    private string _temperatureRangeText = "—";

    [ObservableProperty]
    private string _temperatureCaption = "";

    [ObservableProperty]
    private string _sacText = "—";

    [ObservableProperty]
    private string _rmvText = "—";

    [ObservableProperty]
    private string _sacCaption = "";

    /// <summary>RMV's own completeness caption (RMV covers a strict subset of the SAC dives).</summary>
    [ObservableProperty]
    private string _rmvCaption = "";

    [ObservableProperty]
    private string _gasMixText = "";

    [ObservableProperty]
    private string _gasMixCaption = "";

    [ObservableProperty]
    private int _currentStreakDays;

    [ObservableProperty]
    private int _longestStreakDays;

    /// <summary>Dive-count histogram by max-depth band (for the depth-distribution chart).</summary>
    [ObservableProperty]
    private IReadOnlyList<HistogramBin> _depthHistogram = [];

    /// <summary>Dive counts per calendar month (for the activity trend).</summary>
    [ObservableProperty]
    private IReadOnlyList<MonthCount> _divesByMonth = [];

    /// <summary>Per-year rollups (dive count · bottom time · deepest).</summary>
    [ObservableProperty]
    private IReadOnlyList<YearRollup> _perYear = [];

    /// <summary>Per-dive SAC/RMV trend points (chronological), for the SAC-over-time chart.</summary>
    [ObservableProperty]
    private IReadOnlyList<SacPoint> _sacOverTime = [];

    [RelayCommand]
    private async Task LoadAsync()
    {
        var all = await store.GetDivesAsync();
        var active = all.Where(d => !d.Deleted).ToList();
        IsEmpty = active.Count == 0;

        // Career analytics are over PHYSICAL dives. One dive imported from several sources (BLE + HealthKit +
        // a file) is stored as several vendor-keyed raw rows by design (that is what DiveMergePolicy exists to
        // reconcile); counting/summing the raw rows would double- or triple-count that one dive and get MORE
        // wrong the more sources a diver connects. Collapse to one merged dive each so totals, histograms and
        // SAC-over-time match the merged logbook cards. (The free-tier CAP still counts RAW dives — that is a
        // binding entitlement rule and lives in LogbookViewModel; merge here is display-analytics only.)
        var dives = DiveMergePolicy.Merge(active).Select(m => m.Primary).ToList();

        var summary = DiveStats.Summarize(dives);
        TotalDives = summary.TotalDives;
        MaxDepthText = units.FormatDepth(summary.MaxDepthM);
        TotalBottomTimeText = units.FormatDuration(summary.TotalBottomTimeSec);

        var avgDepth = DiveStats.AverageDepthM(dives);
        AverageDepthText = avgDepth.HasData ? units.FormatDepth(avgDepth.Value) : "—";
        AverageDepthCaption = Caption("Average depth", avgDepth.Completeness);

        var avgDuration = DiveStats.AverageDurationSec(dives);
        AverageDurationText = avgDuration.HasData
            ? units.FormatDuration((int)Math.Round(avgDuration.Value))
            : "—";

        var temp = DiveStats.TemperatureRange(dives);
        TemperatureRangeText = temp.HasData
            ? $"{units.FormatTemp(temp.Value.MinC)} – {units.FormatTemp(temp.Value.MaxC)}"
            : "—";
        TemperatureCaption = Caption("Temperature", temp.Completeness);

        var sac = DiveStats.SacSummary(dives);
        SacText = sac.HasData
            ? string.Create(CultureInfo.InvariantCulture, $"{sac.Value.MeanSacBarPerMin:0.0} bar/min")
            : "—";
        RmvText = sac.Value.MeanRmvLPerMin is double rmv
            ? string.Create(CultureInfo.InvariantCulture, $"{rmv:0.0} L/min")
            : "—";
        SacCaption = Caption("SAC", sac.Completeness);
        // RMV rides on the SAME StatValue but is available for a STRICT SUBSET of the SAC dives (it needs a
        // recorded cylinder volume). Caption it from its own dive count so the completeness never overstates
        // the RMV figure's data basis (SAC 40/120 must not imply RMV 40/120 when RMV came from 10).
        RmvCaption = RmvText == "—"
            ? ""
            : Caption("RMV", new Completeness(sac.Value.RmvDiveCount, dives.Count));

        var gas = DiveStats.GasMixBreakdown(dives);
        GasMixText = string.Create(
            CultureInfo.InvariantCulture,
            $"Air {gas.Value.Air} · Nitrox {gas.Value.Nitrox} · Trimix {gas.Value.Trimix} · Unknown {gas.Value.Unknown}");
        GasMixCaption = Caption("Gas mix", gas.Completeness);

        var streaks = DiveStats.Streaks(dives);
        CurrentStreakDays = streaks.CurrentDays;
        LongestStreakDays = streaks.LongestDays;

        DepthHistogram = DiveStats.DepthHistogram(dives).Value;
        DivesByMonth = DiveStats.DivesByMonth(dives);
        PerYear = DiveStats.PerYear(dives);
        SacOverTime = DiveStats.SacOverTime(dives).Value;
    }

    /// <summary>Settings lives behind the "You" surface's gear (Settings is a pushed route).</summary>
    [RelayCommand]
    private Task OpenSettings() => navigator.GoToAsync(Routes.Settings);

    /// <summary>Renders a completeness signal verbatim, e.g. "SAC available for 42 of 120 dives".</summary>
    private static string Caption(string metric, Completeness completeness) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{metric} available for {completeness.Available} of {completeness.Total} dives");
}
