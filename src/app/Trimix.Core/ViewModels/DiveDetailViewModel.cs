using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trimix.Contracts.Dtos;
using Trimix.Core.Abstractions;
using Trimix.Core.Charts;
using Trimix.Core.Connectors;
using Trimix.Core.Connectors.Infra;
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
    UnitFormatter units,
    INavigator navigator,
    IClock clock,
    IConnectorConsentStore consent,
    IEnumerable<IDiveDestinationConnector> destinations) : ObservableObject
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

    /// <summary>Screen title — "Dive #N" (the Sites capability that titled this by dive-site is gone).</summary>
    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private string _maxDepthText = "";

    [ObservableProperty]
    private string _durationText = "";

    [ObservableProperty]
    private string _minTempText = "";

    /// <summary>Gas tile value ("EAN32" / "AIR").</summary>
    [ObservableProperty]
    private string _gasText = "AIR";

    /// <summary>Bottom-time tile numeral, prototype format m:ss (e.g. "47:00").</summary>
    [ObservableProperty]
    private string _bottomTimeText = "";

    /// <summary>Header meta line: "Dive #N · 14 Jun 2026 · Tulamben".</summary>
    [ObservableProperty]
    private string _metaText = "";

    /// <summary>Origin chip + route strip "FROM {VENDOR}" label (uppercase vendor).</summary>
    [ObservableProperty]
    private string _originLabel = "";

    /// <summary>Route strip destination chips, each "PADI ✓" (linked push providers).</summary>
    [ObservableProperty]
    private IReadOnlyList<string> _routeDestinations = [];

    [ObservableProperty]
    private bool _hasRouteDestinations;

    /// <summary>X-axis mid label (half the bottom time, m:ss).</summary>
    [ObservableProperty]
    private string _axisMidText = "";

    /// <summary>X-axis end label (total bottom time, m:ss).</summary>
    [ObservableProperty]
    private string _axisEndText = "";

    /// <summary>Depth value of the upper dashed gridline (35% of max depth), unit-formatted.</summary>
    [ObservableProperty]
    private string _gridDepth1Text = "";

    /// <summary>Depth value of the lower dashed gridline (65% of max depth), unit-formatted.</summary>
    [ObservableProperty]
    private string _gridDepth2Text = "";

    // ── Touch-to-scrub (C6): crosshair + "depth · time" tooltip at the touched point ──

    /// <summary>True while a finger is on the chart (shows the crosshair + tooltip).</summary>
    [ObservableProperty]
    private bool _isScrubbing;

    /// <summary>Normalized scrub X ∈ [0,1] (chart space; the page maps touch → this).</summary>
    [ObservableProperty]
    private double _scrubX;

    /// <summary>Normalized scrub Y ∈ [0,1] on the depth polyline at <see cref="ScrubX"/>.</summary>
    [ObservableProperty]
    private double _scrubY;

    /// <summary>Tooltip: unit-formatted depth at the scrub point.</summary>
    [ObservableProperty]
    private string _scrubDepthText = "";

    /// <summary>Tooltip: elapsed time (m:ss) at the scrub point.</summary>
    [ObservableProperty]
    private string _scrubTimeText = "";

    /// <summary>
    /// Maps a normalized touch X onto the depth polyline: interpolated crosshair Y, unit-formatted
    /// depth and elapsed-time tooltip values. Pure display math over recorded samples.
    /// </summary>
    public void UpdateScrub(double normalizedX)
    {
        if (Chart.IsEmpty)
        {
            return;
        }

        var x = Math.Clamp(normalizedX, 0, 1);
        var y = ChartGeometry.YAtX(Chart, x);
        ScrubX = x;
        ScrubY = y;
        ScrubDepthText = units.FormatDepth(y * Chart.MaxDepthM);
        ScrubTimeText = units.FormatElapsed((int)Math.Round(x * Chart.DurationSeconds));
        IsScrubbing = true;
    }

    /// <summary>Ends the scrub gesture (hides the crosshair + tooltip).</summary>
    public void EndScrub() => IsScrubbing = false;

    // ── Notes (C6): view / edit / save / empty — persisted locally on the dive ──

    [ObservableProperty]
    private bool _isNotesEditing;

    [ObservableProperty]
    private string _notesDraft = "";

    [ObservableProperty]
    private string _notesText = "";

    /// <summary>True when the dive has saved notes and the editor is closed (view state).</summary>
    [ObservableProperty]
    private bool _hasNotes;

    /// <summary>True when the dive has no notes and the editor is closed ("+ Add notes").</summary>
    [ObservableProperty]
    private bool _isNotesEmpty = true;

    [RelayCommand]
    private void StartNotesEdit()
    {
        NotesDraft = NotesText;
        IsNotesEditing = true;
        HasNotes = false;
        IsNotesEmpty = false;
    }

    /// <summary>"Done": persists the draft onto the dive row (local-only, UpdatedAtUtc bumped).</summary>
    [RelayCommand]
    private async Task SaveNotesAsync()
    {
        if (Dive is not { } dive)
        {
            IsNotesEditing = false;
            return;
        }

        var text = NotesDraft.Trim();
        var updated = dive with
        {
            Notes = text.Length == 0 ? null : text,
            UpdatedAtUtc = clock.UtcNow.UtcDateTime,
        };
        await store.UpsertDiveAsync(updated);
        Dive = updated;
        NotesText = text;
        IsNotesEditing = false;
        HasNotes = text.Length > 0;
        IsNotesEmpty = text.Length == 0;
    }

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
        GasText = display.GasMix ?? "AIR";
        BottomTimeText = units.FormatElapsed(display.DurationSec);
        AxisMidText = units.FormatElapsed(display.DurationSec / 2);
        AxisEndText = BottomTimeText;
        GridDepth1Text = units.FormatDepth(display.MaxDepthM * 0.35);
        GridDepth2Text = units.FormatDepth(display.MaxDepthM * 0.65);

        // Title "Dive #N" + date meta line — N is the chronological sequence number. (The dive-site
        // parts of the title/meta went with the Sites capability.)
        var sequence = active
            .OrderBy(d => d.StartUtc)
            .ToList()
            .FindIndex(d => d.Id == display.Id) + 1;
        Title = string.Create(CultureInfo.InvariantCulture, $"Dive #{Math.Max(1, sequence)}");
        MetaText = display.StartUtc.ToString("d MMM yyyy", CultureInfo.InvariantCulture);

        // Origin chip + FROM {VENDOR} → PADI ✓ SSI ✓ route strip (linked push providers).
        OriginLabel = ProviderCatalog.ShortLabelFor(display.SourceVendor);
        RouteDestinations = destinations
            .Where(d => consent.HasConsent(d.Descriptor.ConnectorId))
            .Select(d => ProviderCatalog.ShortLabelFor(d.Descriptor.ConnectorId) + " ✓")
            .ToList();
        HasRouteDestinations = RouteDestinations.Count > 0;

        // Notes state (view / edit / empty).
        NotesText = display.Notes?.Trim() ?? "";
        IsNotesEditing = false;
        HasNotes = NotesText.Length > 0;
        IsNotesEmpty = NotesText.Length == 0;
        IsScrubbing = false;

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
    }

    [RelayCommand]
    private void ToggleProfileTable() => IsProfileTableVisible = !IsProfileTableVisible;

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

}
