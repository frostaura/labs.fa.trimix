using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trimix.Contracts;
using Trimix.Core.Abstractions;
using Trimix.Core.Connectors.Infra;
using Trimix.Core.Formatting;
using Trimix.Core.Stats;

namespace Trimix.Core.ViewModels;

/// <summary>
/// The "You" tab (prototype C8): avatar initials + inline-editable name (local-only), cert line,
/// the plan card (Free "Keeps your last N dives" → Upgrade / Pro "Lifetime · unlimited import" ·
/// Active), the Metric|Imperial units toggle (re-renders depths/temps everywhere via the shared
/// preference), the Nitrox MOD planner, and the "Manage providers · N of 7 linked" row — plus the
/// career analytics computed by the pure, static <see cref="DiveStats"/> (completeness-captioned,
/// unit-formatted; storage stays metric).
///
/// <para>Guardrail: the MOD planner is pure display math — MOD = (1.4/FO₂ − 1) × 10 — over a gas
/// the diver drags to; nothing computes tissue loading or deco ("we log, we don't compute deco").
/// The plan card's Upgrade navigates to the paywall WITHOUT emitting <c>paywall_shown</c>: the
/// frozen trigger vocabulary (ble_import_cap / cloud_sync_cap / logbook_cap / site_contribute) has
/// no value for a voluntary plan-card entry, and the taxonomy is frozen — misattributing a cap
/// trigger would corrupt the funnel.</para>
/// </summary>
public partial class StatsViewModel(
    ILocalStore store,
    UnitFormatter units,
    INavigator navigator,
    IPreferences preferences,
    IEntitlementService entitlements,
    IConnectorConsentStore consent,
    IEnumerable<IDiveSourceConnector> sources,
    IEnumerable<IDiveDestinationConnector> destinations) : ObservableObject
{
    private const string DefaultUserName = "Alex Reyes";
    private const string DefaultCert = "PADI Advanced OW";

    /// <summary>MOD (m) of pure O₂ at ppO₂ 1.4 — the planner fill bar's full-scale value.</summary>
    private const double ModFullScaleM = 56.7;

    // ── Profile: avatar initials + inline-editable name + cert line (local-only) ──

    [ObservableProperty]
    private string _userName = "";

    [ObservableProperty]
    private string _userInitials = "";

    [ObservableProperty]
    private bool _isNameEditing;

    [ObservableProperty]
    private string _nameDraft = "";

    /// <summary>Sub-line: "PADI Advanced OW · N dives".</summary>
    [ObservableProperty]
    private string _certLine = "";

    [RelayCommand]
    private void StartNameEdit()
    {
        NameDraft = UserName;
        IsNameEditing = true;
    }

    /// <summary>Enter/blur: persists the trimmed name locally (empty keeps the old name).</summary>
    [RelayCommand]
    private void SaveName()
    {
        var name = NameDraft.Trim();
        if (name.Length > 0)
        {
            UserName = name;
            preferences.Set(PreferenceKeys.UserName, name);
            UserInitials = InitialsFor(name);
        }

        IsNameEditing = false;
    }

    /// <summary>Escape: discards the draft.</summary>
    [RelayCommand]
    private void CancelNameEdit() => IsNameEditing = false;

    // ── Plan card (Free → Upgrade · Pro → Active) ──

    [ObservableProperty]
    private bool _isPro;

    [ObservableProperty]
    private string _planTitle = "Free plan";

    [ObservableProperty]
    private string _planSub = "";

    [ObservableProperty]
    private string _planCta = "Upgrade";

    /// <summary>Free → paywall (no <c>paywall_shown</c>: see the type remarks). Pro → no-op.</summary>
    [RelayCommand]
    private Task PlanTapAsync() => IsPro ? Task.CompletedTask : navigator.GoToAsync(Routes.Paywall);

    // ── Units toggle: Metric · m/°C | Imperial · ft/°F (shared preference → app-wide) ──

    [ObservableProperty]
    private bool _isMetric = true;

    [RelayCommand]
    private async Task SetMetricAsync()
    {
        preferences.Set(PreferenceKeys.DepthUnit, DepthUnits.Meters);
        preferences.Set(PreferenceKeys.TempUnit, TempUnits.Celsius);
        IsMetric = true;
        UpdateMod();
        await LoadAsync(); // re-render every depth/temp on this surface; other pages re-load on appearing
    }

    [RelayCommand]
    private async Task SetImperialAsync()
    {
        preferences.Set(PreferenceKeys.DepthUnit, DepthUnits.Feet);
        preferences.Set(PreferenceKeys.TempUnit, TempUnits.Fahrenheit);
        IsMetric = false;
        UpdateMod();
        await LoadAsync();
    }

    // ── Nitrox MOD planner (pure display math — no deco engine, ever) ──

    /// <summary>O₂ fraction of the blend in percent, slider range 21–40.</summary>
    [ObservableProperty]
    private double _o2Percent = 32;

    /// <summary>"EAN32" badge (EAN21 reads as air but keeps the prototype's badge shape).</summary>
    [ObservableProperty]
    private string _eanLabel = "EAN32";

    /// <summary>Live MOD readout at ppO₂ 1.4, unit-formatted per the units toggle.</summary>
    [ObservableProperty]
    private string _modText = "";

    /// <summary>0..1 fraction for the animated fill bar (MOD / MOD-at-100%-O₂).</summary>
    [ObservableProperty]
    private double _modFillFraction;

    partial void OnO2PercentChanged(double value) => UpdateMod();

    private void UpdateMod()
    {
        var o2 = Math.Clamp(Math.Round(O2Percent), 21, 40);
        EanLabel = string.Create(CultureInfo.InvariantCulture, $"EAN{o2:0}");
        var modM = (1.4 / (o2 / 100.0) - 1.0) * 10.0;
        ModText = units.DepthUnit == DepthUnits.Feet
            ? string.Create(CultureInfo.InvariantCulture, $"{Math.Round(modM * UnitFormatter.FeetPerMeter):0}ft")
            : string.Create(CultureInfo.InvariantCulture, $"{modM:0.0}m");
        ModFillFraction = Math.Min(1.0, modM / ModFullScaleM);
    }

    // ── Manage providers row ──

    /// <summary>"N of 7 linked" (counts consented connectors over the registered seven).</summary>
    [ObservableProperty]
    private string _linkedProvidersLabel = "";

    [RelayCommand]
    private Task ManageProvidersAsync() => navigator.GoToAsync(Routes.Providers);

    private static string InitialsFor(string name)
    {
        var initials = string.Concat(name
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Take(2)
            .Select(w => char.ToUpperInvariant(w[0])));
        return initials.Length > 0 ? initials : "?";
    }
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

        // Career analytics are over PHYSICAL dives. One dive imported from several provider sources (e.g.
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

        // ── The prototype "You" surface (C8) ──
        UserName = preferences.Get(PreferenceKeys.UserName) ?? DefaultUserName;
        UserInitials = InitialsFor(UserName);
        CertLine = string.Create(
            CultureInfo.InvariantCulture,
            $"{preferences.Get(PreferenceKeys.CertLabel) ?? DefaultCert} · {TotalDives} dives");

        IsPro = entitlements.CurrentTier is EntitlementTier.Pro or EntitlementTier.Lifetime;
        PlanTitle = IsPro ? "Trimix Pro" : "Free plan";
        // The free-plan copy binds to the FreeTier constant — never a hardcoded 5.
        PlanSub = IsPro
            ? "Lifetime · unlimited import"
            : $"Keeps your last {FreeTier.FreeLogbookVisibleDives} dives";
        PlanCta = IsPro ? "Active" : "Upgrade";

        IsMetric = units.DepthUnit != DepthUnits.Feet;
        UpdateMod();

        var connectorIds = sources.Select(s => s.Descriptor.ConnectorId)
            .Concat(destinations.Select(d => d.Descriptor.ConnectorId))
            .Distinct()
            .ToList();
        var linked = connectorIds.Count(id => consent.HasConsent(id));
        LinkedProvidersLabel = string.Create(
            CultureInfo.InvariantCulture,
            $"{linked} of {connectorIds.Count} linked");
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
