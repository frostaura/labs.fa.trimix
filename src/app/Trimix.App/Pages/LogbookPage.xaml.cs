using System.ComponentModel;
using Trimix.App.Controls;
using Trimix.Core.ViewModels;

namespace Trimix.App.Pages;

/// <summary>
/// Logbook code-behind (Phase B): TrimixPage screenIn entrance, the design's rowIn stagger on dive
/// rows, and pushing the bottom sheets. The add + streaming-sync sheets live in
/// <see cref="SheetOverlayPage"/>, a transparent modal shown over the whole window (above the Shell
/// tab bar) so each card overlays the tab bar with the menu dimmed underneath — a page-hosted sheet
/// can't (the tab bar sits below it on Android and floats over it on iOS). This page just pushes the
/// overlay when a sheet's VM flag rises; the overlay owns the slide, the spinner, cancel-on-close and
/// pops itself once both sheets close.
/// </summary>
public partial class LogbookPage : TrimixPage
{
    private readonly LogbookViewModel _viewModel;
    private DateTime _rowBatchStart = DateTime.MinValue;
    private int _rowBatchIndex;
    private SheetOverlayPage? _overlay;
    private bool _returningFromOverlay;

    public LogbookPage(LogbookViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        BindingContext = viewModel;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.Sync.PropertyChanged += OnSyncPropertyChanged;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing(); // TrimixPage screenIn (suppressed on overlay return, see EnsureOverlayForState)

        // Returning from the sheet overlay is NOT a fresh screen enter: the page stayed on screen under
        // a transparent modal, and the list is already current (a sync reloads itself in OnSyncProperty-
        // Changed; a plain add-close changed nothing). Re-running the entrance + reload here is what made
        // the header slide back in on close, so skip both for the round-trip.
        if (_returningFromOverlay)
        {
            _returningFromOverlay = false;
            return;
        }

        _viewModel.Sync.RefreshLinkState(); // the radar button dims/pulses per current link state
        await _viewModel.LoadCommand.ExecuteAsync(null);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LogbookViewModel.IsAddSheetOpen))
        {
            EnsureOverlayForState();
        }
    }

    private void OnSyncPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SyncSessionViewModel.IsSyncing))
        {
            EnsureOverlayForState();

            // A finished (or cancelled) run may have imported dives while this page was already
            // visible — OnAppearing won't re-fire, so reload here or the logbook stays stale.
            if (!_viewModel.Sync.IsSyncing)
            {
                _ = _viewModel.LoadCommand.ExecuteAsync(null);
            }
        }
        else if (e.PropertyName == nameof(SyncSessionViewModel.SyncSub) && _viewModel.Sync.IsSyncing)
        {
            // Streaming UX: each progress tick may carry newly-imported dives — refresh the list so
            // rows stream in behind the sheet (design: dives appear as they sync).
            _ = _viewModel.LoadCommand.ExecuteAsync(null);
        }
    }

    /// <summary>Present the sheet overlay whenever either sheet's flag is up. It pops itself once both
    /// close, so we only ever need to PUSH here; a single instance is guarded against double-push, and
    /// an add → sync hand-off reuses the same overlay rather than stacking a second one.</summary>
    private void EnsureOverlayForState()
    {
        var anyOpen = _viewModel.IsAddSheetOpen || _viewModel.Sync.IsSyncing;
        if (anyOpen && _overlay is null)
        {
            _overlay = new SheetOverlayPage(_viewModel);
            _overlay.Disappearing += OnOverlayDisappearing;

            // The overlay covers this page until it pops. The very next OnAppearing is that pop-return,
            // so arm both suppressions now: skip the screenIn entrance and the redundant reload so the
            // page doesn't visibly re-render (header sliding in) when the sheet closes.
            SuppressNextScreenIn();
            _returningFromOverlay = true;

            _ = Navigation.PushModalAsync(_overlay, false);
        }
    }

    private void OnOverlayDisappearing(object? sender, EventArgs e)
    {
        if (sender is SheetOverlayPage page)
        {
            page.Disappearing -= OnOverlayDisappearing;
        }

        _overlay = null;
    }

    /// <summary>
    /// rowIn stagger (design: <c>rowIn .4s ease</c> per row): rows loading within one batch window
    /// stagger 60ms apart. Recycled containers don't re-fire Loaded, so settled rows stay put.
    /// </summary>
    private void OnRowLoaded(object? sender, EventArgs e)
    {
        if (sender is not VisualElement row || MotionSettings.IsReducedMotionEnabled)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if ((now - _rowBatchStart).TotalMilliseconds > 600)
        {
            _rowBatchStart = now;
            _rowBatchIndex = 0;
        }

        // Cap the delay so long lists don't leave late rows invisible for noticeable time.
        var delay = Math.Min(_rowBatchIndex++ * 60, 540);
        row.Opacity = 0; // hide before the delayed entrance so the row never flashes at full opacity
        _ = StaggerRowAsync(row, delay);
    }

    private static async Task StaggerRowAsync(VisualElement row, int delayMs)
    {
        if (delayMs > 0)
        {
            await Task.Delay(delayMs);
        }

        await Motion.RowStaggerAsync([row]);
    }
}
