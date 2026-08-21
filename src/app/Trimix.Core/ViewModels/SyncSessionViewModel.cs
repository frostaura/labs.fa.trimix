using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trimix.Core.Abstractions;
using Trimix.Core.Platform;
using Trimix.Core.Connectors;

namespace Trimix.Core.ViewModels;

/// <summary>
/// The app-wide "Sync now" session (prototype C5) — a DI SINGLETON so the add-sheet, the Providers
/// radar button and the streaming sync bottom sheet all observe one run. Wraps
/// <see cref="ISyncCoordinator.SyncNowAsync"/>:
///
/// <list type="bullet">
/// <item><see cref="SyncNowCommand"/> — unlinked taps route to Providers with the red Connect nudge;
/// linked taps land on the logbook and stream the run into the bound sheet.</item>
/// <item><see cref="SyncProgress"/> ticks map onto <see cref="SyncLabel"/> / <see cref="SyncSub"/>
/// ("N imported · checking … · PADI &amp; SSI kept in sync") and the <see cref="Progress"/> bar.</item>
/// <item>On <see cref="SyncReport.CapReached"/> the paywall is presented (trigger
/// <c>cloud_sync_cap</c> — the <c>paywall_shown</c> event itself is emitted by the import
/// pipeline, not here) carrying the count of dives left waiting on providers.</item>
/// <item>A <see cref="ConnectorAuthStatus.NeedsInteraction"/> source outcome is the relink
/// surface: it is published as <see cref="NeedsRelink"/> state (the design has NO toasts —
/// state changes render inline/animated, e.g. a relink chip on the provider row).</item>
/// <item>The run is CANCELLABLE: dismissing the sync sheet invokes <see cref="CancelSyncCommand"/>,
/// which cancels the coordinator's token and hides the sheet.</item>
/// </list>
/// </summary>
public partial class SyncSessionViewModel(
    ISyncCoordinator coordinator,
    IEnumerable<IDiveSourceConnector> sources,
    IConnectorConsentStore consent,
    INavigator navigator,
    IPreferences preferences,
    IClock clock,
    // Optional so the existing test call sites keep compiling; the App always supplies the real one and
    // Core's DI registers a no-op default, so a null here only ever means "headless host".
    IHaptics? haptics = null) : ObservableObject
{
    private readonly IHaptics _haptics = haptics ?? new NullHaptics();

    private CancellationTokenSource? _syncCts;

    private string _linkedNames = "providers";

    /// <summary>Delay between a successful provider link and its auto-started sync (~900ms per the
    /// prototype, so the sign-in sheet finishes dismissing first). Tests set 0.</summary>
    public int AutoSyncDelayMs { get; set; } = 900;

    /// <summary>How long the completed sheet lingers on "finishing up" before closing. Tests set 0.</summary>
    public int DoneLingerMs { get; set; } = 900;

    [ObservableProperty]
    private bool _isSyncing;

    /// <summary>Sheet headline detail: "Garmin Dive · importing" / "Finishing up".</summary>
    [ObservableProperty]
    private string _syncLabel = "";

    /// <summary>Sheet sub-line: "N imported · checking … · PADI &amp; SSI kept in sync".</summary>
    [ObservableProperty]
    private string _syncSub = "";

    /// <summary>0..1 fraction for the sheet's progress bar.</summary>
    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _lastSyncLabel = "never synced";

    /// <summary>Providers whose auth expired mid-run and need a fresh sign-in (display names).
    /// Inline relink state — the design removed toasts; the Providers surface renders this per-row.</summary>
    [ObservableProperty]
    private IReadOnlyList<string> _needsRelink = [];

    /// <summary>Radar-button enablement — false dims it (and an unlinked tap routes to Providers).</summary>
    public bool HasLinkedSources => coordinator.HasLinkedSources;

    /// <summary>Re-reads link state (Providers raises this after connect/disconnect).</summary>
    public void RefreshLinkState() => OnPropertyChanged(nameof(HasLinkedSources));

    /// <summary>Refreshes <see cref="LastSyncLabel"/> from the persisted last-run instant.</summary>
    public void RefreshLastSyncLabel()
    {
        var stored = preferences.Get(PreferenceKeys.LastSyncUtc);
        if (stored is null || !DateTime.TryParse(
                stored, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var lastUtc))
        {
            LastSyncLabel = "never synced";
            return;
        }

        var age = clock.UtcNow.UtcDateTime - DateTime.SpecifyKind(lastUtc, DateTimeKind.Utc);
        LastSyncLabel = age < TimeSpan.FromMinutes(2) ? "last sync just now"
            : age < TimeSpan.FromHours(1) ? $"last sync {Math.Max(1, (int)age.TotalMinutes)}m ago"
            : age < TimeSpan.FromDays(1) ? $"last sync {(int)age.TotalHours}h ago"
            : $"last sync {(int)age.TotalDays}d ago";
    }

    /// <summary>
    /// The single "Sync now" entry point (add-sheet row + radar button). Unlinked: route to
    /// Providers with the Connect nudge. Linked: land on the logbook (the sheet renders there)
    /// and run the coordinator.
    /// </summary>
    [RelayCommand]
    private async Task SyncNowAsync()
    {
        if (IsSyncing)
        {
            return;
        }

        if (!coordinator.HasLinkedSources)
        {
            await navigator.GoToAsync(Routes.Providers, new Dictionary<string, object>
            {
                [Routes.Params.Nudge] = "1",
            });
            return;
        }

        await navigator.GoToAsync(Routes.Logbook);
        await RunAsync();
    }

    /// <summary>
    /// Post-link auto-sync: a successful provider link starts a sync ~900ms later. Fire-and-forget from
    /// the sign-in sheet; owned here so it survives that VM's disposal. Runs the coordinator over ALL
    /// linked sources (idempotent upsert), so it also serves as the newly-linked provider's first pull.
    /// </summary>
    public async Task AutoSyncAfterLinkAsync()
    {
        if (AutoSyncDelayMs > 0)
        {
            await Task.Delay(AutoSyncDelayMs);
        }

        if (IsSyncing || !coordinator.HasLinkedSources)
        {
            return;
        }

        await navigator.GoToAsync(Routes.Logbook);
        await RunAsync();
    }

    /// <summary>
    /// Cancels the running sync (the sync sheet's dismissal path — scrim tap, ✕, swipe-down) and
    /// hides the sheet. Idempotent: a no-op when no run is active.
    /// </summary>
    [RelayCommand]
    private void CancelSync()
    {
        _syncCts?.Cancel();
        IsSyncing = false;
    }

    private async Task RunAsync()
    {
        var linked = sources
            .Where(s => consent.HasConsent(s.Descriptor.ConnectorId))
            .Select(s => ProviderCatalog.NameFor(s.Descriptor.ConnectorId))
            .ToList();
        _linkedNames = linked.Count > 0 ? string.Join(", ", linked) : "providers";

        _syncCts = new CancellationTokenSource();
        NeedsRelink = [];
        IsSyncing = true;
        Progress = 0.05;
        SyncLabel = "Checking providers";
        SyncSub = $"0 imported · checking {_linkedNames} · PADI & SSI kept in sync";

        try
        {
            // Not Progress<T>: this wrapper posts to the captured (UI) context when one exists but
            // reports INLINE when there is none, so tests observe ticks deterministically.
            var progress = new ContextAwareProgress(OnTick);
            var report = await coordinator.SyncNowAsync(onCapReached: null, progress, _syncCts.Token);

            preferences.Set(
                PreferenceKeys.LastSyncUtc,
                clock.UtcNow.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
            RefreshLastSyncLabel();

            // Relink surface: a consented source whose auth expired needs a fresh sign-in.
            // No toast (the design removed them) — inline state the provider rows render.
            NeedsRelink = report.Sources
                .Where(s => s.Auth == ConnectorAuthStatus.NeedsInteraction)
                .Select(s => ProviderCatalog.NameFor(s.ConnectorId))
                .ToList();

            Progress = 1;
            SyncLabel = "Finishing up";
            if (DoneLingerMs > 0)
            {
                await Task.Delay(DoneLingerMs);
            }

            // The run finished: Warning when it finished by hitting the free-tier cap (a refusal the
            // diver is about to be shown), Success otherwise. Terminal cues are never coalesced, so this
            // is heard even though the cap path navigates immediately afterwards.
            _haptics.Perform(report.CapReached ? HapticCue.Warning : HapticCue.Success);

            if (report.CapReached)
            {
                // The paywall_shown(trigger=cloud_sync_cap) event was already emitted by ImportService
                // when the truncation happened — this only PRESENTS the purchase moment.
                var pending = report.Sources.Sum(s => Math.Max(0, s.Fetched - s.Imported));
                await navigator.GoToAsync(Routes.Paywall, new Dictionary<string, object>
                {
                    [Routes.Params.PendingDives] = pending.ToString(CultureInfo.InvariantCulture),
                });
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelled from the sheet (CancelSync) — the sheet is already closing; nothing to show.
        }
        catch (Exception)
        {
            // Every provider endpoint is fragile; a failed run must never strand the sheet open.
            // No toast (design): the failure renders inline in the sheet, then the sheet closes.
            SyncLabel = "Sync failed";
            SyncSub = "Check your connection and try again.";
            Progress = 0;
            // The design removed toasts, so the failure is reported inline and then the sheet closes —
            // which is easy to miss. The haptic is the part the diver cannot look away from.
            _haptics.Perform(HapticCue.Error);
            if (DoneLingerMs > 0)
            {
                await Task.Delay(DoneLingerMs);
            }
        }
        finally
        {
            // Null before dispose so a late CancelSync (the sheet's Closed fires after the exit
            // animation) no-ops instead of touching a disposed source.
            var cts = _syncCts;
            _syncCts = null;
            cts?.Dispose();
            IsSyncing = false;
        }
    }

    private void OnTick(SyncProgress tick)
    {
        var name = tick.ConnectorId is null ? null : ProviderCatalog.NameFor(tick.ConnectorId);
        switch (tick.Stage)
        {
            case SyncStage.Importing:
                SyncLabel = name is null ? "Importing" : $"{name} · importing";
                // Import leg spans 0 → 0.85 of the bar; the push leg + finish take the rest.
                Progress = Math.Clamp(
                    0.05 + (tick.SourcesTotal == 0 ? 0 : 0.8 * tick.SourcesDone / tick.SourcesTotal), 0.05, 0.85);
                break;
            case SyncStage.Pushing:
                SyncLabel = name is null ? "Pushing" : $"{name} · pushing";
                Progress = 0.92;
                break;
            default:
                SyncLabel = "Finishing up";
                Progress = 1;
                break;
        }

        SyncSub = $"{tick.ImportedSoFar} imported · checking {_linkedNames} · PADI & SSI kept in sync";
    }

    /// <summary>Marshals ticks to the captured UI context when present; reports inline otherwise.</summary>
    private sealed class ContextAwareProgress(Action<SyncProgress> handler) : IProgress<SyncProgress>
    {
        private readonly SynchronizationContext? _context = SynchronizationContext.Current;

        public void Report(SyncProgress value)
        {
            if (_context is null)
            {
                handler(value);
            }
            else
            {
                _context.Post(state => handler((SyncProgress)state!), value);
            }
        }
    }
}
