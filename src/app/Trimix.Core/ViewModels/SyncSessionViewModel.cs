using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trimix.Core.Abstractions;
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
/// surface: it is toasted so the diver knows which platform needs a fresh sign-in.</item>
/// </list>
/// </summary>
public partial class SyncSessionViewModel(
    ISyncCoordinator coordinator,
    IEnumerable<IDiveSourceConnector> sources,
    IConnectorConsentStore consent,
    INavigator navigator,
    IUserDialogs dialogs,
    IPreferences preferences,
    IClock clock) : ObservableObject
{
    private string _linkedNames = "providers";

    /// <summary>Delay between the first successful provider link and its auto-started sync (~900ms
    /// per the prototype). Tests set 0.</summary>
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
    /// First-link auto-sync (prototype: the first successful provider link starts a sync ~900ms
    /// later). Fire-and-forget from the sign-in sheet; owned here so it survives that VM's disposal.
    /// </summary>
    public async Task AutoSyncAfterFirstLinkAsync()
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

    private async Task RunAsync()
    {
        var linked = sources
            .Where(s => consent.HasConsent(s.Descriptor.ConnectorId))
            .Select(s => ProviderCatalog.NameFor(s.Descriptor.ConnectorId))
            .ToList();
        _linkedNames = linked.Count > 0 ? string.Join(", ", linked) : "providers";

        IsSyncing = true;
        Progress = 0.05;
        SyncLabel = "Checking providers";
        SyncSub = $"0 imported · checking {_linkedNames} · PADI & SSI kept in sync";

        try
        {
            // Not Progress<T>: this wrapper posts to the captured (UI) context when one exists but
            // reports INLINE when there is none, so tests observe ticks deterministically.
            var progress = new ContextAwareProgress(OnTick);
            var report = await coordinator.SyncNowAsync(onCapReached: null, progress);

            preferences.Set(
                PreferenceKeys.LastSyncUtc,
                clock.UtcNow.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
            RefreshLastSyncLabel();

            // Relink surface: a consented source whose auth expired needs a fresh sign-in.
            var needsRelink = report.Sources
                .Where(s => s.Auth == ConnectorAuthStatus.NeedsInteraction)
                .Select(s => ProviderCatalog.NameFor(s.ConnectorId))
                .ToList();
            if (needsRelink.Count > 0)
            {
                await dialogs.ToastAsync($"Relink {string.Join(", ", needsRelink)} to keep syncing");
            }

            Progress = 1;
            SyncLabel = "Finishing up";
            if (DoneLingerMs > 0)
            {
                await Task.Delay(DoneLingerMs);
            }

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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Every provider endpoint is fragile; a failed run must never strand the sheet open.
            await dialogs.ToastAsync("Sync failed — check your connection and try again.");
            _ = ex;
        }
        finally
        {
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
