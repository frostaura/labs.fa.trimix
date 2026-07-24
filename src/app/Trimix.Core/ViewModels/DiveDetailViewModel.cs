using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trimix.Contracts.Dtos;
using Trimix.Core.Abstractions;
using Trimix.Core.Charts;
using Trimix.Core.Formatting;
using Trimix.Core.Stats;

namespace Trimix.Core.ViewModels;

/// <summary>One row of the columnar profile table alternative: cells align positionally to
/// <see cref="DiveDetailViewModel.ProfileTableHeaders"/> (WCAG data-as-text; §8.4).</summary>
public sealed record ProfileTableRow(IReadOnlyList<string> Cells);

/// <summary>A recorded (never computed) instrument readout surfaced for display, e.g. "Min NDL 3 min".</summary>
public sealed record RecordedReadout(string Label, string Value);

/// <summary>
/// Dive detail: hero numerals, profile chart geometry (drawn by the App's IDrawable) plus the
/// additive recorded-channel <see cref="Overlays"/>, and the accessibility-mandated columnar table
/// alternative (WCAG: the graph always pairs with data-as-text). When the dive is one of several
/// cross-source rows for the same physical dive, it renders the <see cref="DiveMergePolicy"/> union
/// (richest-fidelity-per-field) and lists the contributing <see cref="SourceBadges"/>.
///
/// <para>Guardrail: every readout here — chart overlays, recorded NDL/ceiling/CNS — surfaces a value
/// a source <b>recorded</b>. Nothing computes tissue loading or deco.</para>
/// </summary>
public partial class DiveDetailViewModel(
    ILocalStore store,
    IApiClient api,
    UnitFormatter units,
    INavigator navigator) : ObservableObject
{
    [ObservableProperty]
    private DiveDto? _dive;

    [ObservableProperty]
    private DepthChartPath _chart = DepthChartPath.Empty;

    /// <summary>Additive recorded-channel overlays (temperature/tank lines, ceiling band, markers).</summary>
    [ObservableProperty]
    private ProfileOverlays _overlays = ProfileOverlays.Empty;

    /// <summary>
    /// Screen-reader description of the chart that reflects the recorded channels actually drawn (not a
    /// static "Depth over time"), so a non-visual user learns the chart is multi-channel and knows to open
    /// the table. WCAG 1.1.1 text alternative for the multi-overlay canvas.
    /// </summary>
    [ObservableProperty]
    private string _chartSemanticDescription =
        "Depth over time chart. Use the view-as-table button below for the data as text.";

    /// <summary>Legend flags: a non-color key beneath the chart names each overlay present (WCAG 1.4.1).</summary>
    [ObservableProperty]
    private bool _hasTemperatureOverlay;

    [ObservableProperty]
    private bool _hasTankOverlay;

    [ObservableProperty]
    private bool _hasCeilingOverlay;

    [ObservableProperty]
    private bool _hasEventMarkers;

    /// <summary>True when any overlay legend entry should show (drives the legend row's visibility).</summary>
    [ObservableProperty]
    private bool _hasOverlayLegend;

    /// <summary>Header labels for the columnar table (grows exactly with the recorded channels).</summary>
    [ObservableProperty]
    private IReadOnlyList<string> _profileTableHeaders = [];

    /// <summary>Table alternative to the chart (design language: "view as table"), unit-formatted.</summary>
    [ObservableProperty]
    private IReadOnlyList<ProfileTableRow> _profileTable = [];

    [ObservableProperty]
    private bool _isProfileTableVisible;

    /// <summary>Friendly labels of every source that contributed to this (possibly merged) dive.</summary>
    [ObservableProperty]
    private IReadOnlyList<string> _sourceBadges = [];

    /// <summary>True when more than one source recorded this physical dive (drives the "sources:" strip).</summary>
    [ObservableProperty]
    private bool _isMerged;

    /// <summary>Recorded (imported, never computed) NDL/ceiling/CNS extremes for display.</summary>
    [ObservableProperty]
    private IReadOnlyList<RecordedReadout> _recordedReadouts = [];

    /// <summary>True when at least one recorded deco channel is present (gates the readouts card).</summary>
    [ObservableProperty]
    private bool _hasRecordedReadouts;

    [ObservableProperty]
    private string _siteName = "";

    [ObservableProperty]
    private string _maxDepthText = "";

    [ObservableProperty]
    private string _durationText = "";

    [ObservableProperty]
    private string _minTempText = "";

    /// <summary>Back navigation, wired to the page's Back button.</summary>
    [RelayCommand]
    private Task Close() => navigator.GoBackAsync();

    [RelayCommand]
    private async Task LoadAsync(Guid diveId)
    {
        var dives = await store.GetDivesAsync();
        var active = dives.Where(d => !d.Deleted).ToList();
        if (active.All(d => d.Id != diveId))
        {
            await navigator.GoBackAsync();
            return;
        }

        // Presentation-layer merge (§3 / §8.2): find the cluster this dive belongs to and show its
        // richest-fidelity-per-field union. Merge is non-destructive; the underlying rows are retained.
        var cluster = DiveMergePolicy.Merge(active)
            .FirstOrDefault(m => m.Sources.Any(s => s.Id == diveId));
        var display = cluster?.Primary ?? active.First(d => d.Id == diveId);

        Dive = display;
        MaxDepthText = units.FormatDepth(display.MaxDepthM);
        DurationText = units.FormatDuration(display.DurationSec);
        MinTempText = units.FormatTemp(display.MinTempC);
        Chart = ChartGeometry.BuildDepthChart(display.ProfileSamples);
        Overlays = ChartGeometry.BuildProfileOverlays(display.ProfileSamples, Chart);

        // Non-color legend + an honest screen-reader description of the multi-channel canvas.
        HasTemperatureOverlay = Overlays.Temperature is not null;
        HasTankOverlay = Overlays.TankPressure is not null;
        HasCeilingOverlay = Overlays.CeilingBand is not null;
        HasEventMarkers = Overlays.Markers.Count > 0;
        HasOverlayLegend = HasTemperatureOverlay || HasTankOverlay || HasCeilingOverlay || HasEventMarkers;
        ChartSemanticDescription = BuildChartDescription(Overlays);

        var table = ChartGeometry.BuildProfileTable(display.ProfileSamples);
        ProfileTableHeaders = table.Headers;
        ProfileTable = FormatTable(table);

        SourceBadges = cluster?.Badges.Select(b => b.Label).ToList() ?? [];
        IsMerged = (cluster?.Sources.Count ?? 0) > 1;
        RecordedReadouts = BuildRecordedReadouts(display.ProfileSamples);
        HasRecordedReadouts = RecordedReadouts.Count > 0;

        SiteName = await ResolveSiteNameAsync(display.SiteId);
    }

    [RelayCommand]
    private void ToggleProfileTable() => IsProfileTableVisible = !IsProfileTableVisible;

    [RelayCommand]
    private async Task OpenSiteAsync()
    {
        if (Dive?.SiteId is Guid siteId)
        {
            await navigator.GoToAsync(Routes.SiteDetail, new Dictionary<string, object> { [Routes.Params.SiteId] = siteId });
        }
    }

    /// <summary>
    /// Opens the export chooser (UDDF / DL7 / PDF) for the displayed dive — the clean, ToS-free "hand a
    /// file to any agency yourself" path (aggregator architecture §2). Additive: no existing binding or
    /// readout changes; the export screen re-derives the same merged union from the dive id.
    /// </summary>
    [RelayCommand]
    private async Task ExportAsync()
    {
        if (Dive is { } dive)
        {
            await navigator.GoToAsync(
                ExportDiveViewModel.Route,
                new Dictionary<string, object> { [Routes.Params.DiveId] = dive.Id });
        }
    }

    /// <summary>
    /// Re-formats the invariant numeric cells from <see cref="ChartGeometry.BuildProfileTable"/> for
    /// the m/ft·°C/°F toggles: Elapsed → mm:ss, Depth/Ceiling → depth units, Temp → temperature units.
    /// The other recorded channels carry a fixed unit suffix so the accessible table is never ambiguous
    /// (a screen reader announces "Tank 190 bar", not a bare "190"): Tank → bar, NDL → min, HR → bpm,
    /// PpO2/Setpoint → bar, Ascent → m/min.
    /// </summary>
    private IReadOnlyList<ProfileTableRow> FormatTable(ProfileTable table)
    {
        var headers = table.Headers;
        var rows = new List<ProfileTableRow>(table.Rows.Count);
        foreach (var raw in table.Rows)
        {
            var cells = new string[raw.Count];
            for (var i = 0; i < raw.Count; i++)
            {
                cells[i] = FormatCell(headers[i], raw[i]);
            }

            rows.Add(new ProfileTableRow(cells));
        }

        return rows;
    }

    private string FormatCell(string header, string invariant)
    {
        if (string.IsNullOrEmpty(invariant))
        {
            return "";
        }

        return header switch
        {
            "Elapsed" when int.TryParse(invariant, NumberStyles.Integer, CultureInfo.InvariantCulture, out var s)
                => units.FormatElapsed(s),
            "Depth" or "Ceiling" when double.TryParse(invariant, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
                => units.FormatDepth(d),
            "Temp" when double.TryParse(invariant, NumberStyles.Float, CultureInfo.InvariantCulture, out var t)
                => units.FormatTemp(t),
            // Fixed-unit channels: append the unit so the accessible table cell is unambiguous.
            "Tank" => $"{invariant} bar",
            "NDL" => $"{invariant} min",
            "HR" => $"{invariant} bpm",
            "PpO2" or "Setpoint" => $"{invariant} bar",
            "Ascent" => $"{invariant} m/min",
            _ => invariant,
        };
    }

    /// <summary>
    /// Surfaces recorded NDL/ceiling/CNS extremes (min NDL, deepest ceiling, peak CNS) for display.
    /// These are min/max over values the computer <b>recorded</b> — no tissue loading or deco is
    /// computed here (the frozen liability line stands).
    /// </summary>
    private IReadOnlyList<RecordedReadout> BuildRecordedReadouts(IReadOnlyList<ProfileSampleDto> samples)
    {
        var readouts = new List<RecordedReadout>();

        var ndls = samples.Where(s => s.Ndl is not null).Select(s => s.Ndl!.Value).ToList();
        if (ndls.Count > 0)
        {
            readouts.Add(new RecordedReadout(
                "Min NDL (recorded)",
                string.Create(CultureInfo.InvariantCulture, $"{ndls.Min():0} min")));
        }

        var ceilings = samples.Where(s => s.CeilingM is > 0).Select(s => s.CeilingM!.Value).ToList();
        if (ceilings.Count > 0)
        {
            readouts.Add(new RecordedReadout("Max ceiling (recorded)", units.FormatDepth(ceilings.Max())));
        }

        var cns = samples.Where(s => s.Cns is not null).Select(s => s.Cns!.Value).ToList();
        if (cns.Count > 0)
        {
            readouts.Add(new RecordedReadout(
                "Max CNS (recorded)",
                string.Create(CultureInfo.InvariantCulture, $"{cns.Max():0}%")));
        }

        return readouts;
    }

    /// <summary>Names the recorded channels drawn on the shared canvas so the a11y description is truthful.</summary>
    private static string BuildChartDescription(ProfileOverlays overlays)
    {
        var channels = new List<string> { "depth" };
        if (overlays.Temperature is not null)
        {
            channels.Add("temperature");
        }

        if (overlays.TankPressure is not null)
        {
            channels.Add("tank pressure");
        }

        if (overlays.CeilingBand is not null)
        {
            channels.Add("recorded ceiling");
        }

        var joined = channels.Count == 1
            ? channels[0]
            : string.Join(", ", channels.Take(channels.Count - 1)) + " and " + channels[^1];

        var eventPart = overlays.Markers.Count switch
        {
            0 => "",
            1 => " with 1 recorded event marker",
            var n => string.Create(CultureInfo.InvariantCulture, $" with {n} recorded event markers"),
        };

        return $"{char.ToUpperInvariant(joined[0])}{joined[1..]} over time{eventPart}. " +
            "Use the view-as-table button below for the data as text.";
    }

    private async Task<string> ResolveSiteNameAsync(Guid? siteId)
    {
        if (siteId is null)
        {
            return "";
        }

        try
        {
            var site = await api.GetSiteAsync(siteId.Value);
            return site.Name;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ""; // Offline: the dive renders without site enrichment.
        }
    }
}
