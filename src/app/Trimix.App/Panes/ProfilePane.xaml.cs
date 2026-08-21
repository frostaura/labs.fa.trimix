using System.ComponentModel;
using Trimix.App.Controls;
using Trimix.App.Services;
using Trimix.Core.ViewModels;

namespace Trimix.App.Panes;

/// <summary>
/// The profile pane's view concerns: both chart drawables, the shared touch-to-scrub gesture across the two
/// canvases, the tooltip slide, and the depth chart's one-shot draw-in animation.
/// </summary>
public partial class ProfilePane : ContentView, IDivePane
{
    private readonly DepthChartDrawable _depth = new();
    private readonly TemperatureLaneDrawable _lane = new();

    private DiveDetailViewModel? _viewModel;
    private bool _chartAnimated;

    public ProfilePane()
    {
        InitializeComponent();

        ChartView.Drawable = _depth;
        TempLaneView.Drawable = _lane;

        // Both canvases scrub: a drag starting on either one is the same time position.
        foreach (var view in new[] { ChartView, TempLaneView })
        {
            // Selection (not Tap) on scrub start, and only on start: a tick per DragInteraction would
            // buzz continuously across a 100-minute profile. Grabbing the crosshair is one selection.
            view.StartInteraction += (_, _) =>
            {
                // Claim the gesture BEFORE any movement is reported, so the scroll container never gets the
                // chance to intercept partway through the drag.
                SetScrollLock(true);
                Feedback.Selection();
            };
            view.StartInteraction += OnChartTouch;
            view.DragInteraction += OnChartTouch;
            view.EndInteraction += OnChartTouchEnded;
            view.CancelInteraction += OnChartTouchCancelled;
        }

        BindingContextChanged += OnBindingContextChanged;
    }

    private void OnBindingContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = BindingContext as DiveDetailViewModel;
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        // The carousel may realise this pane after the view model already loaded, so seed from current state
        // rather than waiting for a change notification that has already been raised.
        _lane.Lane = _viewModel.TemperatureLane;
        _depth.AverageDepth = _viewModel.AverageDepth;
        _depth.Overlays = _viewModel.Overlays;
        ApplyRenderPath();
    }

    private void OnChartTouch(object? sender, TouchEventArgs e)
    {
        if (_viewModel is null || sender is not View view || e.Touches.Length == 0)
        {
            return;
        }

        var pad = ChartMetrics.PlotPad;
        var width = view.Width;
        if (width <= pad * 2)
        {
            return;
        }

        var x = e.Touches[0].X;
        var fraction = Math.Clamp((x - pad) / (width - (pad * 2)), 0, 1);
        _viewModel.UpdateScrub(fraction);

        _depth.ScrubX = _viewModel.ScrubX;
        _depth.ScrubY = _viewModel.ScrubY;
        _lane.ScrubX = _viewModel.ScrubX;
        _lane.ScrubY = _viewModel.ScrubTempY;
        InvalidateCharts();

        // Slide the tooltip with the finger; flip to the left near the right edge. Keyed off the depth chart's
        // width because the tooltip lives in that plot's grid cell.
        var chartWidth = ChartView.Width > 0 ? ChartView.Width : width;
        var tipWidth = ScrubTip.Width > 0 ? ScrubTip.Width : 120;
        ScrubTip.TranslationX = x > chartWidth * 0.68
            ? Math.Max(0, x - tipWidth - 12)
            : Math.Min(chartWidth - tipWidth, x + 12);
    }

    private void OnChartTouchEnded(object? sender, TouchEventArgs e) => EndScrub();

    private void OnChartTouchCancelled(object? sender, EventArgs e) => EndScrub();

    /// <summary>
    /// Hands the touch stream to the chart for the duration of a scrub, and gives it back afterwards.
    ///
    /// <para>Without this the scrub is genuinely unusable: the chart sits inside a vertical
    /// <c>ScrollView</c>, and the moment a dragging finger drifts even slightly off horizontal the scroll
    /// container claims the gesture, the <c>GraphicsView</c> gets a CancelInteraction and the crosshair
    /// vanishes. Nobody drags a perfectly straight line across a phone, so it let go constantly — the
    /// reported "the graph regularly loses focus so it's not fun to use".</para>
    ///
    /// <para>Each platform has its own mechanism and neither has a cross-platform equivalent: Android asks
    /// the ancestors not to intercept, iOS disables the scroll view's pan outright. Both are released on
    /// EndInteraction AND CancelInteraction, so a gesture that ends abnormally cannot leave the page
    /// permanently unscrollable.</para>
    /// </summary>
    private void SetScrollLock(bool locked)
    {
#if ANDROID
        if (ChartView.Handler?.PlatformView is Android.Views.View native)
        {
            native.Parent?.RequestDisallowInterceptTouchEvent(locked);
        }
#elif IOS
        if (Scroller.Handler?.PlatformView is UIKit.UIScrollView scroll)
        {
            scroll.ScrollEnabled = !locked;
        }
#endif
    }

    private void EndScrub()
    {
        SetScrollLock(false);
        _viewModel?.EndScrub();
        _depth.ScrubX = null;
        _lane.ScrubX = null;
        InvalidateCharts();
    }

    /// <summary>Both canvases redraw together — one finger, one theme flip, one repaint of each.</summary>
    private void InvalidateCharts()
    {
        ChartView.Invalidate();
        TempLaneView.Invalidate();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(DiveDetailViewModel.Overlays):
                _depth.Overlays = _viewModel.Overlays;
                ChartView.Invalidate();
                break;
            case nameof(DiveDetailViewModel.AverageDepth):
                _depth.AverageDepth = _viewModel.AverageDepth;
                ChartView.Invalidate();
                break;
            case nameof(DiveDetailViewModel.TemperatureLane):
                _lane.Lane = _viewModel.TemperatureLane;
                TempLaneView.Invalidate();
                break;

            // Keyed off ChartRender, not Chart: the drawable renders the decimated path, and keying off the
            // render path removes any dependence on the order the two are assigned in.
            case nameof(DiveDetailViewModel.ChartRender):
                ApplyRenderPath();
                break;
        }
    }

    private void ApplyRenderPath()
    {
        if (_viewModel is null)
        {
            return;
        }

        _depth.Path = _viewModel.ChartRender;

        // Design motion (Interactive.dc.html): drawLine 1.8s ease with a .15s delay, then fillFade .8s ease
        // starting at 1.4s — one composite 2.2s timeline, ONCE per view, honoring reduced motion (render
        // complete immediately when reduced).
        if (!_chartAnimated && !MotionSettings.IsReducedMotionEnabled && !_viewModel.ChartRender.IsEmpty)
        {
            _chartAnimated = true;
            _depth.Progress = 0;
            _depth.FillOpacity = 0;

            const double total = 2200.0;
            var timeline = new Animation();
            timeline.Add(150 / total, 1950 / total, new Animation(
                progress => _depth.Progress = progress, 0, 1, Easing.CubicOut));
            timeline.Add(1400 / total, 1.0, new Animation(
                opacity => _depth.FillOpacity = opacity, 0, 1, Easing.CubicOut));
            timeline.Add(0, 1, new Animation(_ => ChartView.Invalidate(), 0, 1));
            timeline.Commit(this, "TrimixChartDrawIn", rate: 16, length: (uint)total, Easing.Linear);
        }
        else
        {
            _depth.Progress = 1;
            _depth.FillOpacity = 1;
            InvalidateCharts();
        }
    }

    /// <summary>Redraws both canvases when the OS theme flips, so neither keeps a stale-theme colour.</summary>
    public void OnThemeChanged() => InvalidateCharts();

    /// <inheritdoc />
    public void ScrollToTop() => Scroller.ScrollToAsync(0, 0, false);
}
