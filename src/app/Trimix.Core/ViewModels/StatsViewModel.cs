using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trimix.Contracts;
using Trimix.Core.Abstractions;
using Trimix.Core.Connectors;
using Trimix.Core.Connectors.Infra;
using Trimix.Core.Formatting;
using Trimix.Core.Privacy;
using Trimix.Core.Stats;

namespace Trimix.Core.ViewModels;

/// <summary>
/// The "You" tab (prototype C8): the plan card (Free "Keeps your last N dives" → Upgrade / Pro
/// "Lifetime · unlimited import" · Active), the Metric|Imperial units toggle (re-renders depths/temps
/// everywhere via the shared preference), the Nitrox MOD planner, and the "Manage providers · N of 7
/// linked" row — plus the career analytics computed by the pure, static <see cref="DiveStats"/>
/// (completeness-captioned, unit-formatted; storage stays metric). There is no name/avatar/identity
/// card: Trimix is single-device-per-user with no accounts, so there is no user context to show.
///
/// <para>Guardrail: the MOD planner is pure display math — MOD = (1.4/FO₂ − 1) × 10 — over a gas
/// the diver drags to; nothing computes tissue loading or deco ("we log, we don't compute deco").
/// The plan card's Upgrade navigates to the paywall WITHOUT emitting <c>paywall_shown</c>: the
/// frozen trigger vocabulary (ble_import_cap / cloud_sync_cap / logbook_cap / site_contribute) has
/// no value for a voluntary plan-card entry, and the taxonomy is frozen — misattributing a cap
/// trigger would corrupt the funnel.</para>
///
/// <para>The privacy actions (export / factory reset) and the about/onboarding-replay affordances
/// live here too: the standalone Settings page was retired 2026-07-29 and folded into this "You"
/// surface, which already carried the duplicate plan + units controls.</para>
/// </summary>
public partial class StatsViewModel(
    ILocalStore store,
    UnitFormatter units,
    INavigator navigator,
    IPreferences preferences,
    IEntitlementService entitlements,
    IUserDialogs dialogs,
    IConnectorPurgeService connectorPurge,
    IConnectorConsentStore consent,
    IProviderPriority priority,
    IEnumerable<IDiveSourceConnector> sources,
    IEnumerable<IDiveDestinationConnector> destinations) : ObservableObject
{
    /// <summary>MOD (m) of pure O₂ at ppO₂ 1.4 — the planner fill bar's full-scale value.</summary>
    private const double ModFullScaleM = 56.7;

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

    /// <summary>Blend badge — "EAN32" etc., or "AIR" at 21% O₂ (21% IS air).</summary>
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
        // 21% O₂ IS air — label it "AIR" rather than the pedantic "EAN21".
        EanLabel = (int)o2 == 21 ? "AIR" : string.Create(CultureInfo.InvariantCulture, $"EAN{o2:0}");
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
        var dives = DiveMergePolicy.Merge(active, priority.FidelityDelegate()).Select(m => m.Primary).ToList();

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

        // ── The prototype "You" surface (C8) — single device per user, so no name/identity card ──
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

    // ── Privacy & about (serverless, on-device — folded in when the Settings page was retired) ──

    /// <summary>GDPR/CCPA data export: serializes the local-first dive store (the App shares the file).</summary>
    [RelayCommand]
    private async Task<string> ExportDataAsync()
    {
        var dives = await store.GetDivesAsync();
        var json = JsonSerializer.Serialize(
            new { exportedAtUtc = DateTime.UtcNow, dives },
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        await dialogs.ToastAsync($"Export ready — {dives.Count} dives.");
        return json;
    }

    /// <summary>
    /// Factory reset (the single destructive action, serverless): everything lives on-device, so this
    /// is a full local wipe — dives, every connector consent+token, the entitlement cache, and every
    /// on-device setting — then onboarding re-runs from a fresh-install state. Confirmed first, with the
    /// downstream-provider disclaimer (already-synced dives remain on PADI/SSI etc.).
    /// </summary>
    [RelayCommand]
    private async Task ResetAppAsync()
    {
        var confirmed = await dialogs.ConfirmAsync(
            "Reset Trimix?",
            "This erases everything on this device — all your dives, provider links and settings — and "
            + "starts you fresh. Dives already synced to PADI, SSI or other providers won't be removed "
            + "from those services. This cannot be undone.",
            accept: "Reset",
            cancel: "Cancel");
        if (!confirmed)
        {
            return;
        }

        // Wipe local dives, sever every device-global vendor consent+token, and drop the paid tier to Free
        // (a store restore can re-grant later). Serverless: there is no server copy to erase.
        foreach (var dive in await store.GetDivesAsync())
        {
            await store.DeleteDiveAsync(dive.Id);
        }

        await connectorPurge.PurgeAllAsync();
        await entitlements.ClearLocalGrantAsync();

        // Clear every on-device preference so the app returns to a fresh-install state. FirstRunTracked
        // is cleared too: onboarding will replay AND (this time) re-fire first_run_completed.
        foreach (var key in (string[])[
            PreferenceKeys.FirstRunTracked, PreferenceKeys.DepthUnit, PreferenceKeys.TempUnit,
            PreferenceKeys.UserName, PreferenceKeys.CertLabel, PreferenceKeys.LastSyncUtc,
            PreferenceKeys.AutoRestoreDone])
        {
            preferences.Remove(key);
        }

        await navigator.GoToAsync(Routes.Onboarding);
    }

    /// <summary>
    /// Re-runs the onboarding intro on demand (to revisit it, or demo Trimix to a friend). Purely a
    /// re-show: dives, provider links and settings are untouched, and first-run stays marked complete.
    /// </summary>
    [RelayCommand]
    private Task ReplayOnboardingAsync() => navigator.GoToAsync(Routes.Onboarding);

    /// <summary>Renders a completeness signal verbatim, e.g. "SAC available for 42 of 120 dives".</summary>
    private static string Caption(string metric, Completeness completeness) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{metric} available for {completeness.Available} of {completeness.Total} dives");
}
