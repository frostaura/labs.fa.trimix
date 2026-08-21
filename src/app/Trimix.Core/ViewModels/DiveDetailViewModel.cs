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

/// <summary>One label/value row of provider-reported detail, e.g. "Water density" / "1025 kg/m³".</summary>
public sealed record DiveFact(string Label, string Value);

/// <summary>
/// A titled group of <see cref="DiveFact"/>s. A group is only produced when it has at least one fact, and a
/// fact is only produced when the provider actually reported the value — so a provider that reports nothing
/// in a category makes the whole card disappear rather than rendering blanks or zeroes. This is the entire
/// per-provider graceful-degradation mechanism on the detail page.
/// </summary>
public sealed record DiveFactGroup(string Title, IReadOnlyList<DiveFact> Facts);

/// <summary>One timestamped event a dive computer raised, formatted for the events list.</summary>
public sealed record DiveEventItem(string TimeText, string Label, string Detail);

/// <summary>
/// One tappable source of an overlapping (merged) dive — one provider's own recording of the same physical
/// dive. Tapping swaps the whole detail view to this source's version in place (no navigation).
/// <see cref="IsPrimary"/> is the priority-winning source shown by default; <see cref="IsSelected"/> is the
/// one currently on screen.
/// </summary>
public sealed record SourceOption(Guid Id, string Label, bool IsPrimary, bool IsSelected);

/// <summary>
/// One tappable entry in the section switcher above the page dots.
///
/// <para>Tapping is the PRIMARY way to change section, not a fallback. A swipe gesture inside a scrolling pane
/// fires on Android but is swallowed by UIScrollView's pan on iOS, so shipping swipe-only would have meant a
/// carousel that silently does not work on one of the two platforms.</para>
/// </summary>
public sealed record DivePaneChip(DivePaneKind Kind, string Label, bool IsSelected);

/// <summary>
/// The swipeable sections of the dive detail page. The page presents these as a carousel rather than one long
/// scroll, so a dive with a lot of provider detail does not read as an intimidating wall of cards.
/// </summary>
public enum DivePaneKind
{
    /// <summary>Tiles, the depth chart, the temperature lane and the accessible table.</summary>
    Profile,

    /// <summary>Provider-reported fact groups, tags and the entry-point card.</summary>
    Details,

    /// <summary>Recorded computer events and the recorded-channel extremes.</summary>
    Events,

    /// <summary>The diver's own notes.</summary>
    Notes,
}

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
    IProviderPriority priority,
    IEnumerable<IDiveDestinationConnector> destinations,
    IMapLauncher maps) : ObservableObject
{
    /// <summary>The merged cluster the opened dive belongs to — retained so a source swap needs no reload.</summary>
    private MergedDive? _cluster;

    /// <summary>Active dives snapshot from the last load — used to compute a swapped source's sequence number.</summary>
    private List<DiveDto> _activeDives = [];

    /// <summary>Id of the source whose version is currently on screen (the primary until the diver taps another).</summary>
    private Guid _displayedSourceId;
    /// <summary>
    /// False until <c>LoadAsync</c> has finished populating everything. The page keeps its content hidden until
    /// this flips, then fades it in — otherwise the sections render against default view-model state and the
    /// diver sees an empty skeleton pop into real data.
    /// </summary>
    [ObservableProperty]
    private bool _isLoaded;

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

    /// <summary>
    /// The decimated copy of <see cref="Chart"/> that the drawable renders. <see cref="Chart"/> stays full
    /// fidelity because the scrub interpolation, the overlays and the max-depth label all read from it.
    /// </summary>
    [ObservableProperty]
    private DepthChartPath _chartRender = DepthChartPath.Empty;

    /// <summary>The temperature mini-lane drawn under the depth chart; null when no sample carried a temp.</summary>
    [ObservableProperty]
    private TemperatureLane? _temperatureLane;

    [ObservableProperty]
    private bool _hasTemperatureLane;

    /// <summary>Eyebrow value for the lane: the temperatures actually observed, e.g. "26 °C → 28 °C".</summary>
    [ObservableProperty]
    private string _tempRangeText = "";

    /// <summary>Screen-reader description for the lane's own canvas (it is a second focusable element).</summary>
    [ObservableProperty]
    private string _temperatureLaneSemanticDescription = "";

    /// <summary>The dotted average-depth reference line; null when no average is known.</summary>
    [ObservableProperty]
    private DepthReference? _averageDepth;

    [ObservableProperty]
    private bool _hasAverageDepth;

    /// <summary>Legend flags: a non-color key beneath the chart names each overlay present (WCAG 1.4.1).</summary>
    [ObservableProperty]
    private bool _hasTemperatureOverlay;

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

    /// <summary>
    /// The tappable per-source options for an overlapping dive, priority order (primary first). Each swaps the
    /// detail view to that provider's own recording in place. Single-source dives yield one entry (IsMerged false).
    /// </summary>
    [ObservableProperty]
    private IReadOnlyList<SourceOption> _sourceOptions = [];

    /// <summary>True when more than one source recorded this physical dive (drives the "sources:" strip).</summary>
    [ObservableProperty]
    private bool _isMerged;

    /// <summary>
    /// Provider-reported detail, grouped into cards. Only groups with at least one reported value appear,
    /// so a low-fidelity provider simply yields fewer cards.
    /// </summary>
    [ObservableProperty]
    private IReadOnlyList<DiveFactGroup> _factGroups = [];

    [ObservableProperty]
    private bool _hasFactGroups;

    /// <summary>Provider-reported dive tags ("WRECK", "NIGHT"), rendered as chips. Empty hides the row.</summary>
    [ObservableProperty]
    private IReadOnlyList<string> _tags = [];

    [ObservableProperty]
    private bool _hasTags;

    /// <summary>True when the provider reported entry coordinates (gates the whole entry-point card).</summary>
    [ObservableProperty]
    private bool _hasEntryPoint;

    /// <summary>The entry coordinates, formatted for display, e.g. "-27.5225, 32.6866".</summary>
    [ObservableProperty]
    private string _entryPointText = "";

    /// <summary>Timestamped computer events (gas switch, safety stop, max depth). Empty hides the card.</summary>
    [ObservableProperty]
    private IReadOnlyList<DiveEventItem> _events = [];

    [ObservableProperty]
    private bool _hasEvents;

    /// <summary>Recorded (imported, never computed) NDL/ceiling/CNS extremes for display.</summary>
    [ObservableProperty]
    private IReadOnlyList<RecordedReadout> _recordedReadouts = [];

    /// <summary>True when at least one recorded deco channel is present (gates the readouts card).</summary>
    [ObservableProperty]
    private bool _hasRecordedReadouts;

    /// <summary>
    /// The panes the carousel shows, in order. Built per dive: Profile and Notes always exist, while Details and
    /// Events appear only when the provider actually supplied something for them, so a summary-only dive gets a
    /// two-pane carousel rather than two empty sections.
    /// </summary>
    [ObservableProperty]
    private IReadOnlyList<DivePaneKind> _panes = [DivePaneKind.Profile, DivePaneKind.Notes];

    /// <summary>Bound two-way to the carousel position; also drives the page-dot indicator.</summary>
    [ObservableProperty]
    private int _paneIndex;

    /// <summary>Pane count, for the "Section N of M" accessibility announcement.</summary>
    public int PaneCount => Panes.Count;

    /// <summary>The pane currently shown. The page shows exactly one; visibility is driven off this.</summary>
    public DivePaneKind CurrentPane =>
        PaneIndex >= 0 && PaneIndex < Panes.Count ? Panes[PaneIndex] : DivePaneKind.Profile;

    public bool IsProfilePaneVisible => CurrentPane == DivePaneKind.Profile;

    public bool IsDetailsPaneVisible => CurrentPane == DivePaneKind.Details;

    public bool IsEventsPaneVisible => CurrentPane == DivePaneKind.Events;

    public bool IsNotesPaneVisible => CurrentPane == DivePaneKind.Notes;

    /// <summary>The section switcher's entries, rebuilt whenever the pane list or the selection changes.</summary>
    [ObservableProperty]
    private IReadOnlyList<DivePaneChip> _paneChips = [];

    private static string LabelFor(DivePaneKind kind) => kind switch
    {
        DivePaneKind.Details => "DETAILS",
        DivePaneKind.Events => "EVENTS",
        DivePaneKind.Notes => "NOTES",
        _ => "PROFILE",
    };

    /// <summary>Jumps straight to a section from the switcher.</summary>
    [RelayCommand]
    private void SelectPane(DivePaneKind kind)
    {
        var index = Panes.ToList().IndexOf(kind);
        if (index >= 0)
        {
            PaneIndex = index;
        }
    }

    partial void OnPanesChanged(IReadOnlyList<DivePaneKind> value)
    {
        OnPropertyChanged(nameof(PaneCount));

        // Clamp: the pane list is dynamic, so a shorter list must not leave the pager on a position that no
        // longer exists.
        if (PaneIndex >= value.Count)
        {
            PaneIndex = Math.Max(0, value.Count - 1);
        }

        NotifyPaneVisibility();
    }

    partial void OnPaneIndexChanged(int value) => NotifyPaneVisibility();

    private void NotifyPaneVisibility()
    {
        PaneChips = [.. Panes.Select(k => new DivePaneChip(k, LabelFor(k), k == CurrentPane))];
        OnPropertyChanged(nameof(CurrentPane));
        OnPropertyChanged(nameof(IsProfilePaneVisible));
        OnPropertyChanged(nameof(IsDetailsPaneVisible));
        OnPropertyChanged(nameof(IsEventsPaneVisible));
        OnPropertyChanged(nameof(IsNotesPaneVisible));
    }

    /// <summary>Swipe left → next section. Clamped, because the pager does not loop.</summary>
    [RelayCommand]
    private void NextPane()
    {
        if (PaneIndex < Panes.Count - 1)
        {
            PaneIndex++;
        }
    }

    /// <summary>Swipe right → previous section.</summary>
    [RelayCommand]
    private void PreviousPane()
    {
        if (PaneIndex > 0)
        {
            PaneIndex--;
        }
    }

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

    /// <summary>Depth-axis label at the plot's TOP hairline — the surface, i.e. "0 m".</summary>
    [ObservableProperty]
    private string _depthSurfaceText = "";

    /// <summary>
    /// Depth-axis label at the plot's BOTTOM hairline — the deepest sample.
    ///
    /// <para>Derived from <c>Chart.MaxDepthM</c> (the max over samples), NOT from the dive's summary
    /// <c>MaxDepthM</c>: the plot floor <em>is</em> the deepest sample, so labelling it with the provider's
    /// summary number would annotate the line with a value it does not represent. The MAX DEPTH tile keeps the
    /// summary figure — that is the diver's logbook number, and on a merged cross-source dive the two may
    /// legitimately differ by tenths.</para>
    /// </summary>
    [ObservableProperty]
    private string _depthFloorText = "";

    [ObservableProperty]
    private bool _hasDepthFloorLabel;

    /// <summary>Average depth, unit-formatted — the chart legend and the MAX DEPTH tile's subtext.</summary>
    [ObservableProperty]
    private string _avgDepthText = "";

    /// <summary>Average water temperature, unit-formatted — the MIN TEMP tile's subtext.</summary>
    [ObservableProperty]
    private string _avgTempText = "";

    [ObservableProperty]
    private bool _hasAverageTemp;

    /// <summary>
    /// One-shot announcement for the MAX DEPTH tile, average included. The tile's children are excluded from the
    /// accessibility tree so it reads as a single unit — which means the average has to be folded in here rather
    /// than left as a separate label a screen reader would skip.
    /// </summary>
    public string MaxDepthAccessibleText => HasAverageDepth
        ? $"Max depth {MaxDepthText}, average {AvgDepthText}"
        : $"Max depth {MaxDepthText}";

    /// <summary>One-shot announcement for the MIN TEMP tile, average included.</summary>
    public string MinTempAccessibleText => HasAverageTemp
        ? $"Minimum temperature {MinTempText}, average {AvgTempText}"
        : $"Minimum temperature {MinTempText}";

    partial void OnMaxDepthTextChanged(string value) => OnPropertyChanged(nameof(MaxDepthAccessibleText));

    partial void OnAvgDepthTextChanged(string value) => OnPropertyChanged(nameof(MaxDepthAccessibleText));

    partial void OnHasAverageDepthChanged(bool value) => OnPropertyChanged(nameof(MaxDepthAccessibleText));

    partial void OnMinTempTextChanged(string value) => OnPropertyChanged(nameof(MinTempAccessibleText));

    partial void OnAvgTempTextChanged(string value) => OnPropertyChanged(nameof(MinTempAccessibleText));

    partial void OnHasAverageTempChanged(bool value) => OnPropertyChanged(nameof(MinTempAccessibleText));

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

    /// <summary>Normalized scrub Y ∈ [0,1] on the temperature lane at <see cref="ScrubX"/>.</summary>
    [ObservableProperty]
    private double _scrubTempY;

    /// <summary>Tooltip: unit-formatted temperature at the scrub point (blank when there is no lane).</summary>
    [ObservableProperty]
    private string _scrubTempText = "";

    [ObservableProperty]
    private bool _hasScrubTemp;

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

        // One touch reads all three channels. Interpolate from the full-fidelity lane so the readout is the
        // recorded temperature at that instant, not whatever survived the lane's drawing decimation.
        if (ChartGeometry.TempAtX(TemperatureLane, x) is { } tempC && TemperatureLane is { } lane)
        {
            var range = lane.AxisMaxC - lane.AxisMinC;
            ScrubTempY = range <= 0 ? 0.5 : Math.Clamp((lane.AxisMaxC - tempC) / range, 0, 1);
            ScrubTempText = units.FormatTemp(tempC);
            HasScrubTemp = true;
        }
        else
        {
            ScrubTempText = "";
            HasScrubTemp = false;
        }

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

    /// <summary>
    /// Opens the dive's entry coordinates in the device's maps app, pinned with the dive's name. No-ops when the
    /// provider reported no coordinates, so the command is safe to bind unconditionally.
    /// </summary>
    [RelayCommand]
    private Task OpenEntryPointAsync()
    {
        if (Dive?.Environment is not { EntryLatitude: { } lat, EntryLongitude: { } lon })
        {
            return Task.CompletedTask;
        }

        return maps.OpenAsync(lat, lon, Title, CancellationToken.None);
    }

    /// <summary>
    /// Everything <see cref="LoadAsync"/> computes off the UI thread, handed back in one piece so the
    /// observable properties can be assigned in a single pass on the UI thread (assignment must stay there —
    /// only the arithmetic moves).
    /// </summary>
    private sealed record LoadedDive(
        DiveDto Display,
        MergedDive? Cluster,
        int Sequence,
        double? AverageDepthM,
        double? AverageTempC,
        DepthChartPath Chart,
        ProfileOverlays Overlays,
        TemperatureLane? TemperatureLane,
        DepthReference? AverageDepth,
        DepthChartPath ChartRender);

    [RelayCommand]
    private async Task LoadAsync(Guid diveId)
    {
        IsLoaded = false;
        var dives = await store.GetDivesAsync();
        var active = dives.Where(d => !d.Deleted).ToList();
        if (active.All(d => d.Id != diveId))
        {
            await navigator.GoBackAsync();
            return;
        }

        // The user's provider priority decides which source wins the overlapping-dive merge (and thus which
        // version is shown by default). Snapshot the delegate for this pass.
        var priorityFidelity = priority.FidelityDelegate();

        // ---- Everything expensive happens OFF the UI thread ------------------------------------------
        // A 1 Hz Garmin profile is ~5,600 samples, and the four geometry builders plus the cross-source
        // merge are pure arithmetic over it. Run on the UI thread they stall the very thread driving the
        // page-push animation, so the transition stutters — the shimmer hid the empty content but never the
        // jank. Everything inside this lambda MUST stay pure: it runs on the thread pool, and the only
        // reason it is safe is that it neither reads nor writes shared mutable state.
        var loaded = await Task.Run(() =>
        {
            // Presentation-layer merge (§3 / §8.2): find the cluster this dive belongs to and show its
            // priority-winning union. Merge is non-destructive; the underlying rows are retained.
            var cluster = DiveMergePolicy.Merge(active, priorityFidelity)
                .FirstOrDefault(m => m.Sources.Any(s => s.Id == diveId));
            var displayDive = cluster?.Primary ?? active.First(d => d.Id == diveId);
            return BuildLoaded(displayDive, cluster, active);
        });

        // Retain the cluster + active snapshot so tapping another source swaps the view with no reload.
        _cluster = loaded.Cluster;
        _activeDives = active;
        _displayedSourceId = loaded.Display.Id;

        ApplyLoaded(loaded);

        // Last statement in the load: everything above is populated, so the page can reveal itself.
        IsLoaded = true;
    }

    /// <summary>
    /// Swap the detail view to another provider's recording of the same physical dive, in place — no
    /// navigation, no store round-trip. The cluster is already in hand from the load; only this source's
    /// chart geometry is recomputed (off the UI thread), then the same bind pass runs.
    /// </summary>
    [RelayCommand]
    private async Task SelectSourceAsync(Guid sourceId)
    {
        var cluster = _cluster;
        if (cluster is null || sourceId == _displayedSourceId)
        {
            return;
        }

        var source = cluster.Sources.FirstOrDefault(s => s.Id == sourceId);
        if (source is null)
        {
            return;
        }

        var active = _activeDives;
        var loaded = await Task.Run(() => BuildLoaded(source, cluster, active));
        _displayedSourceId = source.Id;
        ApplyLoaded(loaded);
    }

    /// <summary>Pure geometry/summary build for one source's version — safe to run on the thread pool.</summary>
    private static LoadedDive BuildLoaded(DiveDto display, MergedDive? cluster, List<DiveDto> active)
    {
        // Tile subtexts: sourced through DiveStats so the "provider value, else sample mean" fallback lives
        // in one tested place rather than being re-implemented per tile.
        var avgDepthM = DiveStats.TryAverageDepth(display);
        var avgTempC = DiveStats.TryAverageTempC(display);

        // Chronological sequence number, used only when the provider reported no dive number.
        var seq = active
            .OrderBy(d => d.StartUtc)
            .ToList()
            .FindIndex(d => d.Id == display.Id) + 1;

        var chart = ChartGeometry.BuildDepthChart(display.ProfileSamples);
        return new LoadedDive(
            display,
            cluster,
            seq,
            avgDepthM,
            avgTempC,
            chart,
            ChartGeometry.BuildProfileOverlays(display.ProfileSamples, chart),
            ChartGeometry.BuildTemperatureLane(display.ProfileSamples, chart),
            ChartGeometry.BuildAverageDepthReference(chart, avgDepthM),
            ChartGeometry.BuildRenderPath(chart));
    }

    /// <summary>The UI-thread bind pass: assigns every observable from a computed <see cref="LoadedDive"/>.
    /// Shared by the initial load and an in-place source swap.</summary>
    private void ApplyLoaded(LoadedDive loaded)
    {
        var display = loaded.Display;

        Dive = display;
        MaxDepthText = units.FormatDepth(display.MaxDepthM);
        DurationText = units.FormatDuration(display.DurationSec);
        MinTempText = units.FormatTemp(display.MinTempC);
        GasText = display.GasMix ?? "AIR";
        BottomTimeText = units.FormatElapsed(display.DurationSec);
        AxisMidText = units.FormatElapsed(display.DurationSec / 2);
        AxisEndText = BottomTimeText;

        AvgDepthText = loaded.AverageDepthM is { } avgDepth ? units.FormatDepth(avgDepth) : "";
        HasAverageTemp = loaded.AverageTempC is not null;
        AvgTempText = loaded.AverageTempC is { } avgTemp ? units.FormatTemp(avgTemp) : "";

        // Title "Dive #N" + date meta line — N is the chronological sequence number. (The dive-site
        // parts of the title/meta went with the Sites capability.)
        // Prefer the provider's own dive number so the title matches the diver's logbook, their computer and
        // the logbook list; fall back to chronological position when no provider reported one.
        var number = display.DiveNumber ?? Math.Max(1, loaded.Sequence);
        Title = string.Create(CultureInfo.InvariantCulture, $"Dive #{number}");
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

        Chart = loaded.Chart;
        Overlays = loaded.Overlays;
        TemperatureLane = loaded.TemperatureLane;
        AverageDepth = loaded.AverageDepth;

        // ChartRender is assigned AFTER Chart so a listener reacting to either already sees both. The page
        // keys its drawable off ChartRender for exactly that reason — preserve this order.
        ChartRender = loaded.ChartRender;

        // Depth axis ends, replacing the old 35 %/65 % gridlines. See DepthFloorText's remarks for why this
        // reads Chart.MaxDepthM rather than the dive's summary max depth.
        DepthSurfaceText = units.FormatDepth(0);
        DepthFloorText = units.FormatDepth(Chart.MaxDepthM);
        HasDepthFloorLabel = Chart.MaxDepthM > 0.05;

        HasAverageDepth = AverageDepth is not null;

        HasTemperatureLane = TemperatureLane is not null;
        TempRangeText = TemperatureLane is { } lane
            ? $"{units.FormatTemp(lane.ObservedMinC)} → {units.FormatTemp(lane.ObservedMaxC)}"
            : "";
        TemperatureLaneSemanticDescription = TemperatureLane is { } described
            ? "Water temperature over the same time axis, "
                + $"{units.FormatTemp(described.ObservedMinC)} to {units.FormatTemp(described.ObservedMaxC)}."
            : "";

        // Non-color legend + an honest screen-reader description of what is actually drawn.
        HasTemperatureOverlay = HasTemperatureLane;
        HasCeilingOverlay = Overlays.CeilingBands.Count > 0;
        HasEventMarkers = Overlays.Markers.Count > 0;
        HasOverlayLegend = HasAverageDepth || HasCeilingOverlay;
        ChartSemanticDescription = BuildChartDescription(Overlays, DepthFloorText, AvgDepthText);

        // The accessible table is built on first open, NOT here: it is up to 150 rows x 10 unit-formatted
        // cells that a BindableLayout materializes even while collapsed, so building it on load costs the
        // diver a visible delay for something they have not asked to see.
        ProfileTableHeaders = [];
        ProfileTable = [];
        _tableBuilt = false;

        SourceBadges = loaded.Cluster?.Badges.Select(b => b.Label).ToList() ?? [];
        IsMerged = (loaded.Cluster?.Sources.Count ?? 0) > 1;
        SourceOptions = BuildSourceOptions(loaded.Cluster, display);
        RecordedReadouts = BuildRecordedReadouts(display.ProfileSamples);
        HasRecordedReadouts = RecordedReadouts.Count > 0;

        FactGroups = BuildFactGroups(display);
        HasFactGroups = FactGroups.Count > 0;
        Tags = display.Tags ?? [];
        HasTags = Tags.Count > 0;
        Events = BuildEvents(display);
        HasEvents = Events.Count > 0;

        HasEntryPoint = display.Environment is { EntryLatitude: not null, EntryLongitude: not null };
        EntryPointText = display.Environment is { EntryLatitude: { } lat, EntryLongitude: { } lon }
            ? string.Create(CultureInfo.InvariantCulture, $"{lat:0.0000}, {lon:0.0000}")
            : "";

        // Build the pane list LAST, so it can be decided from the flags computed above.
        var panes = new List<DivePaneKind> { DivePaneKind.Profile };
        if (HasFactGroups || HasTags || HasEntryPoint)
        {
            panes.Add(DivePaneKind.Details);
        }

        if (HasEvents || HasRecordedReadouts)
        {
            panes.Add(DivePaneKind.Events);
        }

        panes.Add(DivePaneKind.Notes);
        Panes = panes;
        PaneIndex = 0;
    }

    /// <summary>
    /// The tappable per-source options for the "sources" strip, in priority order (primary first) so the
    /// winning provider leads. A single-source dive yields one entry. The currently-displayed source is
    /// flagged <see cref="SourceOption.IsSelected"/>.
    /// </summary>
    private IReadOnlyList<SourceOption> BuildSourceOptions(MergedDive? cluster, DiveDto display)
    {
        if (cluster is null || cluster.Sources.Count == 0)
        {
            return [new SourceOption(display.Id, DiveMergePolicy.BadgeLabel(display), true, true)];
        }

        var primaryId = cluster.Primary.Id;
        var rank = priority.FidelityDelegate();
        return cluster.Sources
            .OrderByDescending(rank)
            .Select(s => new SourceOption(
                s.Id,
                DiveMergePolicy.BadgeLabel(s),
                s.Id == primaryId,
                s.Id == _displayedSourceId))
            .ToList();
    }

    /// <summary>
    /// Builds the provider-reported detail cards. Each <c>Add</c> is a no-op when the provider did not report
    /// the value, and an empty group is dropped — so Garmin yields every card while a summary-only provider
    /// yields few or none, with no per-provider branching anywhere in the UI.
    /// </summary>
    private IReadOnlyList<DiveFactGroup> BuildFactGroups(DiveDto dive)
    {
        var groups = new List<DiveFactGroup>();

        void Group(string title, Action<List<DiveFact>> build)
        {
            var facts = new List<DiveFact>();
            build(facts);
            if (facts.Count > 0)
            {
                groups.Add(new DiveFactGroup(title, facts));
            }
        }

        static void Add(List<DiveFact> facts, string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                facts.Add(new DiveFact(label, value));
            }
        }

        static string? Num(double? value, string unit, string format = "0.#") =>
            value is { } v ? string.Create(CultureInfo.InvariantCulture, $"{v.ToString(format, CultureInfo.InvariantCulture)} {unit}") : null;

        static string? Range(double? from, double? to, string unit) =>
            from is { } f && to is { } t
                ? string.Create(CultureInfo.InvariantCulture, $"{f:0.#}{unit} → {t:0.#}{unit}")
                : null;

        Group("Dive", facts =>
        {
            Add(facts, "Type", dive.DiveTypeLabel);
            Add(facts, "Dive number", dive.DiveNumber?.ToString(CultureInfo.InvariantCulture));
            Add(facts, "Bottom time", dive.BottomTimeSec is { } bt ? units.FormatElapsed(bt) : null);
            Add(facts, "Surface interval", dive.SurfaceIntervalSec is { } si ? units.FormatDuration(si) : null);
            Add(facts, "Decompression", dive.IsDeco switch { true => "Yes", false => "No", null => null });
            Add(facts, "Time zone", dive.TimezoneId);
        });

        Group("Conditions", facts =>
        {
            Add(facts, "Water", dive.WaterType);
            Add(facts, "Temperature", dive.MinTempC is not null || dive.Environment?.MaxTempC is not null
                ? FormatTempRange(dive.MinTempC, dive.Environment?.MaxTempC)
                : null);
            Add(facts, "Average temp", dive.Environment?.AvgTempC is { } avg ? units.FormatTemp(avg) : null);
            Add(facts, "Water density", Num(dive.Environment?.WaterDensityKgM3, "kg/m³", "0"));

            // Entry coordinates are deliberately NOT a fact row: they get their own tappable card that opens
            // the platform maps app (and embeds a map on iOS). Emitting them here too would show them twice.
        });

        // "Recorded" is load-bearing wording: these are values the computer reported, not values Trimix
        // derived. Trimix never models tissue loading.
        Group("Exposure (recorded)", facts =>
        {
            Add(facts, "CNS", Range(dive.Exposure?.CnsStartPercent, dive.Exposure?.CnsEndPercent, "%"));
            Add(facts, "Nitrogen", Range(dive.Exposure?.N2StartPercent, dive.Exposure?.N2EndPercent, "%"));
            Add(facts, "O₂ toxicity", Num(dive.Exposure?.O2ToxicityPercent, "%", "0"));
            Add(facts, "Average ppO₂", Num(dive.Exposure?.AvgPo2, "bar", "0.00"));
            Add(facts, "Max ppO₂", Num(dive.Exposure?.MaxPo2, "bar", "0.00"));
        });

        Group("Computer", facts =>
        {
            Add(facts, "Device", dive.Computer?.DeviceName);
            Add(facts, "Tissue model", dive.Computer?.TissueModel);
            Add(facts, "Gradient factors", dive.Computer is { GradientFactorLow: { } lo, GradientFactorHigh: { } hi }
                ? string.Create(CultureInfo.InvariantCulture, $"{lo}/{hi}")
                : null);
        });

        Group("Cylinders", facts =>
        {
            var cylinders = dive.Cylinders ?? [];
            for (var i = 0; i < cylinders.Count; i++)
            {
                var c = cylinders[i];
                var label = cylinders.Count == 1
                    ? "Gas"
                    : string.Create(CultureInfo.InvariantCulture, $"Cylinder {i + 1}");

                var mix = c.O2 is { } o2
                    ? c.He is > 0
                        ? string.Create(CultureInfo.InvariantCulture, $"{o2:0}/{c.He:0} O₂/He")
                        : string.Create(CultureInfo.InvariantCulture, $"{o2:0}% O₂")
                    : null;
                Add(facts, label, mix);

                Add(facts, label + " pressure", c.StartBar is { } start
                    ? c.EndBar is { } end
                        ? string.Create(CultureInfo.InvariantCulture, $"{start:0} → {end:0} bar")
                        : string.Create(CultureInfo.InvariantCulture, $"{start:0} bar")
                    : null);
            }
        });

        return groups;
    }

    private string FormatTempRange(double? min, double? max) =>
        min is { } lo && max is { } hi && Math.Abs(hi - lo) > 0.05
            ? $"{units.FormatTemp(lo)} → {units.FormatTemp(hi)}"
            : units.FormatTemp(min ?? max);

    /// <summary>Formats provider-reported computer events onto the dive's elapsed timeline.</summary>
    private IReadOnlyList<DiveEventItem> BuildEvents(DiveDto dive) =>
        (dive.Alerts ?? [])
            .OrderBy(a => a.TSeconds)
            .Select(a => new DiveEventItem(
                units.FormatElapsed(Math.Max(0, a.TSeconds)),
                Capitalize(a.Type),
                a.Detail ?? ""))
            .ToList();

    private static string Capitalize(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();

    private bool _tableBuilt;

    [RelayCommand]
    private void ToggleProfileTable()
    {
        if (!IsProfileTableVisible && !_tableBuilt && Dive is { } dive)
        {
            var table = ChartGeometry.BuildProfileTable(dive.ProfileSamples);
            ProfileTableHeaders = table.Headers;
            ProfileTable = FormatTable(table);
            _tableBuilt = true;
        }

        IsProfileTableVisible = !IsProfileTableVisible;
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

    /// <summary>
    /// Describes what is drawn on the MAIN canvas, truthfully. Temperature is not named here — it has its own
    /// canvas and its own <see cref="TemperatureLaneSemanticDescription"/>; tank pressure and event markers are
    /// not drawn at all. It states the depth range and the average, which a sighted user reads off the axis
    /// labels but which were previously announced to nobody.
    /// </summary>
    private static string BuildChartDescription(ProfileOverlays overlays, string floorText, string avgText)
    {
        var parts = new List<string>
        {
            floorText.Length > 0 ? $"Depth over time, surface to {floorText}" : "Depth over time",
        };

        if (avgText.Length > 0)
        {
            parts.Add($"Average depth {avgText}");
        }

        if (overlays.CeilingBands.Count > 0)
        {
            parts.Add("Recorded ceiling shown");
        }

        parts.Add("Use the view-as-table button below for the data as text");
        return string.Join(". ", parts) + ".";
    }

}
