using Trimix.App.Controls;
using Trimix.App.Panes;
using Trimix.Core;
using Trimix.Core.ViewModels;

namespace Trimix.App.Pages;

[QueryProperty(nameof(DiveId), Routes.Params.DiveId)]
public partial class DiveDetailPage : TrimixPage
{
    private readonly DiveDetailViewModel _viewModel;

    /// <summary>Which dive has already been loaded, so the two entry points below load exactly once.</summary>
    private Guid _loadedDiveId;

    /// <summary>Section slide duration — matched to Motion.ScreenInMs so page and pane motion agree.</summary>
    private const uint PaneSlideMs = Motion.ScreenInMs;

    /// <summary>Previous pane position, which is the only thing that can tell us slide direction.</summary>
    private int _lastPaneIndex;

    public DiveDetailPage(DiveDetailViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        BindingContext = viewModel;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    /// <summary>
    /// Reveals the sections once — and only once — the view model has finished loading, with the design's
    /// screenIn motion. The container is <c>IsVisible=false</c> until then, so there is no empty-skeleton flash.
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(DiveDetailViewModel.IsLoaded) when _viewModel.IsLoaded:
                PinPanesToTop();
                ShowPane(_viewModel.CurrentPane, animated: false);
                _lastPaneIndex = _viewModel.PaneIndex;
                RevealSections();
                break;

            // Switching section must also start at the top — a pane keeps its own scroll offset otherwise.
            case nameof(DiveDetailViewModel.PaneIndex):
                PinPanesToTop();
                ShowPane(_viewModel.CurrentPane, animated: true);
                _lastPaneIndex = _viewModel.PaneIndex;
                break;
        }
    }

    /// <summary>Maps a section to its pane view. The four panes are static siblings sharing one Grid cell.</summary>
    private View PaneFor(DivePaneKind kind) => kind switch
    {
        DivePaneKind.Details => Details,
        DivePaneKind.Events => Events,
        DivePaneKind.Notes => Notes,
        _ => Profile,
    };

    /// <summary>
    /// Brings <paramref name="kind"/> on screen with a directional slide + fade — forward slides in from the
    /// right, back from the left — so changing section reads as movement rather than a hard cut.
    ///
    /// <para>Direction comes from the change in <c>PaneIndex</c> (position in the LIVE pane list), not from
    /// the pane kind, because a dive with no events has a different pane list and "Notes" can be index 1 on
    /// one dive and index 3 on another.</para>
    ///
    /// <para>This is a transition, NOT a finger-tracking carousel: a drag-follows-thumb implementation needs a
    /// PanGestureRecognizer, which would fight the chart's scrub gesture and iOS's UIScrollView pan — the
    /// very conflict that made swipe unreliable and put a tappable switcher on this page in the first place.</para>
    /// </summary>
    private void ShowPane(DivePaneKind kind, bool animated)
    {
        var incoming = PaneFor(kind);
        var outgoing = Sections.Children
            .OfType<View>()
            .FirstOrDefault(v => v.IsVisible && !ReferenceEquals(v, incoming));

        if (!animated || MotionSettings.IsReducedMotionEnabled || Sections.Width <= 0)
        {
            foreach (var pane in Sections.Children.OfType<View>())
            {
                pane.TranslationX = 0;
                pane.Opacity = 1;
                pane.IsVisible = ReferenceEquals(pane, incoming);
            }

            return;
        }

        if (ReferenceEquals(outgoing, incoming) || outgoing is null)
        {
            incoming.IsVisible = true;
            return;
        }

        var forward = _viewModel.PaneIndex >= _lastPaneIndex;
        var width = Sections.Width;
        var enterFrom = forward ? width : -width;

        incoming.TranslationX = enterFrom;
        incoming.Opacity = 0;
        incoming.IsVisible = true;

        // Both panes move together; the outgoing one is hidden and reset only once it is off screen, so it
        // is clean for its next turn.
        _ = incoming.TranslateTo(0, 0, PaneSlideMs, Easing.CubicOut);
        _ = incoming.FadeTo(1, PaneSlideMs, Easing.CubicOut);
        _ = outgoing.TranslateTo(-enterFrom, 0, PaneSlideMs, Easing.CubicOut)
            .ContinueWith(
                _ =>
                {
                    outgoing.IsVisible = false;
                    outgoing.TranslationX = 0;
                    outgoing.Opacity = 1;
                },
                TaskScheduler.FromCurrentSynchronizationContext());
        _ = outgoing.FadeTo(0, PaneSlideMs, Easing.CubicOut);
    }

    /// <summary>
    /// Reveals the sections with a pure FADE — deliberately not the app's screenIn motion, which also slides
    /// TranslationY 10→0. Any vertical movement at the moment real content replaces the skeleton reads as the
    /// page bumping or settling, which is exactly what this transition must not do.
    /// </summary>
    private void RevealSections()
    {
        if (MotionSettings.IsReducedMotionEnabled)
        {
            Sections.Opacity = 1;
            return;
        }

        Sections.Opacity = 0;
        Sections.TranslationY = 0;
        _ = Sections.FadeTo(1, 160, Easing.CubicOut);
    }

    /// <summary>Pins every pane's scroll to the top, so no section is ever revealed mid-page.</summary>
    private void PinPanesToTop()
    {
        foreach (var pane in Sections.Children.OfType<IDivePane>())
        {
            pane.ScrollToTop();
        }
    }

    public Guid DiveId { get; set; }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // The drawables resolve theme tokens at draw time; redraw when the OS theme flips (e.g. a sunset auto
        // dark→light) so neither canvas keeps a stale-theme (low-contrast) colour.
        if (Application.Current is { } app)
        {
            app.RequestedThemeChanged -= OnRequestedThemeChanged;
            app.RequestedThemeChanged += OnRequestedThemeChanged;
        }

        await LoadIfReadyAsync();
    }

    /// <summary>
    /// Shell applies <c>[QueryProperty]</c> values around navigation, and <see cref="OnAppearing"/> is not
    /// guaranteed to run after that has happened — when it does not, <see cref="DiveId"/> is still
    /// <see cref="Guid.Empty"/> and the dive silently never loads, leaving a blank page. Loading from
    /// <see cref="OnNavigatedTo"/> as well closes that window; <see cref="_loadedDiveId"/> keeps it to one load.
    /// </summary>
    protected override async void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);
        await LoadIfReadyAsync();
    }

    private async Task LoadIfReadyAsync()
    {
        if (DiveId == Guid.Empty || _loadedDiveId == DiveId)
        {
            return;
        }

        _loadedDiveId = DiveId;
        try
        {
            await _viewModel.LoadCommand.ExecuteAsync(DiveId);
        }
        catch (Exception ex)
        {
            // An AsyncRelayCommand swallows exceptions, so a failure here leaves a blank page and no trace.
            System.Diagnostics.Debug.WriteLine($"[TRIMIX] dive detail load failed: {ex}");
            throw;
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (Application.Current is { } app)
        {
            app.RequestedThemeChanged -= OnRequestedThemeChanged;
        }
    }

    private void OnRequestedThemeChanged(object? sender, AppThemeChangedEventArgs e) => Profile.OnThemeChanged();
}
