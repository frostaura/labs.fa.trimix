using System.ComponentModel;
using Trimix.App.Controls;
using Trimix.Core.ViewModels;

namespace Trimix.App.Pages;

/// <summary>
/// Transparent modal that hosts the Logbook's add + streaming-sync bottom sheets over the whole
/// window (above the Shell tab bar). The IsOpen flags stay TwoWay-bound to the <see cref="LogbookViewModel"/>
/// (still the single source of truth for which sheet is up); the binding context is attached on
/// appearing so the slide-up animates AFTER the modal is on screen rather than snapping into place
/// pre-render. Dismissing the sync sheet cancels the run; the page pops itself once both sheets have
/// finished their exit animation. Pushed by <see cref="LogbookPage"/> when either flag rises.
/// </summary>
public partial class SheetOverlayPage : TransparentModalPage
{
    private const string SpinAnimationHandle = "trimix.spinCw";

    private readonly LogbookViewModel _viewModel;
    private bool _bound;

    public SheetOverlayPage(LogbookViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();

        // Every dismissal path of the sync sheet (scrim tap, ✕, swipe-down) cancels the run.
        // Idempotent: after a natural completion close the VM has no live run to cancel.
        SyncSheet.Closed += (_, _) => _viewModel.Sync.CancelSyncCommand.Execute(null);

        AddSheet.Closed += OnSheetClosed;
        SyncSheet.Closed += OnSheetClosed;

        _viewModel.Sync.PropertyChanged += OnSyncPropertyChanged;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Bind now (not in the ctor): the IsOpen bindings resolve true only once the modal is on
        // screen, so BottomSheet's slide-up plays instead of snapping into place before first render.
        if (!_bound)
        {
            _bound = true;
            BindingContext = _viewModel;
        }

        UpdateSyncSpinner();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.Sync.PropertyChanged -= OnSyncPropertyChanged;
        SyncSpinnerIcon.AbortAnimation(SpinAnimationHandle);
    }

    private void OnSyncPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SyncSessionViewModel.IsSyncing))
        {
            UpdateSyncSpinner();
        }
    }

    /// <summary>Once BOTH sheets have finished closing, tear the overlay down. The guard keeps an
    /// add → sync hand-off (add closes while sync is opening) from popping the still-needed overlay.</summary>
    private async void OnSheetClosed(object? sender, EventArgs e)
    {
        // Pop only when BOTH sheets are closed AND this overlay is the TOP modal. The top-modal check
        // is the safety net: if a navigation already pushed another page (or the navigator tore this
        // overlay down first), a blind PopModalAsync would remove the wrong page. Popping only when we
        // are on top means we never dismiss anything but ourselves.
        if (!AddSheet.IsOpen && !SyncSheet.IsOpen
            && Navigation.ModalStack.Count > 0
            && ReferenceEquals(Navigation.ModalStack[^1], this))
        {
            await Navigation.PopModalAsync(false);
        }
    }

    /// <summary>The design's <c>spinCw 1.4s linear infinite</c> on the syncing icon.</summary>
    private void UpdateSyncSpinner()
    {
        SyncSpinnerIcon.AbortAnimation(SpinAnimationHandle);

        if (_viewModel.Sync.IsSyncing && !MotionSettings.IsReducedMotionEnabled)
        {
            var spin = new Animation(v => SyncSpinnerIcon.Rotation = v * 360);
            spin.Commit(
                SyncSpinnerIcon, SpinAnimationHandle, rate: 16, length: 1400, Easing.Linear,
                // Re-check reduced motion each cycle, not just at start: toggling the accessibility setting
                // mid-sync should stop the spin, and the old predicate kept it running until IsSyncing changed.
                repeat: () => _viewModel.Sync.IsSyncing && !MotionSettings.IsReducedMotionEnabled);
        }
        else
        {
            SyncSpinnerIcon.Rotation = 0;
        }
    }
}
