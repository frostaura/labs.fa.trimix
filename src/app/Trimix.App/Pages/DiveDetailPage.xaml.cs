using System.ComponentModel;
using Trimix.App.Controls;
using Trimix.Core;
using Trimix.Core.ViewModels;

namespace Trimix.App.Pages;

[QueryProperty(nameof(DiveId), Routes.Params.DiveId)]
public partial class DiveDetailPage : TrimixPage
{
    /// <summary>Must mirror <see cref="DepthChartDrawable"/>'s plot padding for touch→chart mapping.</summary>
    private const double ChartPad = 8.0;

    private readonly DiveDetailViewModel _viewModel;
    private readonly DepthChartDrawable _drawable = new();
    private bool _chartAnimated;
    private IDispatcherTimer? _factTimer;

    public DiveDetailPage(DiveDetailViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        BindingContext = viewModel;
        ChartView.Drawable = _drawable;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;

        // Touch-to-scrub (C6): drag a crosshair + tooltip along the profile.
        ChartView.StartInteraction += OnChartTouch;
        ChartView.DragInteraction += OnChartTouch;
        ChartView.EndInteraction += OnChartTouchEnded;
        ChartView.CancelInteraction += OnChartTouchCancelled;
    }

    public Guid DiveId { get; set; }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // The drawable resolves theme tokens at draw time; redraw when the OS theme flips (e.g. a
        // sunset auto dark→light) so the chart line never keeps a stale-theme (low-contrast) color.
        if (Application.Current is { } app)
        {
            app.RequestedThemeChanged -= OnRequestedThemeChanged;
            app.RequestedThemeChanged += OnRequestedThemeChanged;
        }

        // "NEAR THIS SITE" carousel: auto-rotate ~4s (paused while the notes editor is open).
        _factTimer ??= Dispatcher.CreateTimer();
        _factTimer.Interval = TimeSpan.FromSeconds(4);
        _factTimer.Tick += OnFactTimerTick;
        _factTimer.Start();

        if (DiveId != Guid.Empty)
        {
            await _viewModel.LoadCommand.ExecuteAsync(DiveId);
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (Application.Current is { } app)
        {
            app.RequestedThemeChanged -= OnRequestedThemeChanged;
        }

        if (_factTimer is { } timer)
        {
            timer.Stop();
            timer.Tick -= OnFactTimerTick;
        }
    }

    private void OnFactTimerTick(object? sender, EventArgs e)
    {
        if (_viewModel.HasSite && !_viewModel.IsNotesEditing)
        {
            _viewModel.NextFactCommand.Execute(null);
        }
    }

    private void OnChartTouch(object? sender, TouchEventArgs e)
    {
        var width = ChartView.Width;
        if (width <= ChartPad * 2 || e.Touches.Length == 0)
        {
            return;
        }

        var x = e.Touches[0].X;
        var fraction = Math.Clamp((x - ChartPad) / (width - ChartPad * 2), 0, 1);
        _viewModel.UpdateScrub(fraction);

        _drawable.ScrubX = _viewModel.ScrubX;
        _drawable.ScrubY = _viewModel.ScrubY;
        ChartView.Invalidate();

        // Slide the tooltip with the finger; flip to the left near the right edge.
        var tipWidth = ScrubTip.Width > 0 ? ScrubTip.Width : 90;
        ScrubTip.TranslationX = x > width * 0.68
            ? Math.Max(0, x - tipWidth - 12)
            : Math.Min(width - tipWidth, x + 12);
    }

    private void OnChartTouchEnded(object? sender, TouchEventArgs e) => EndScrub();

    private void OnChartTouchCancelled(object? sender, EventArgs e) => EndScrub();

    private void EndScrub()
    {
        _viewModel.EndScrub();
        _drawable.ScrubX = null;
        ChartView.Invalidate();
    }

    private void OnRequestedThemeChanged(object? sender, AppThemeChangedEventArgs e) =>
        ChartView.Invalidate();

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // The recorded-channel overlays are additive geometry drawn after the depth line — feed them
        // through and redraw without touching the draw-in animation.
        if (e.PropertyName == nameof(DiveDetailViewModel.Overlays))
        {
            _drawable.Overlays = _viewModel.Overlays;
            ChartView.Invalidate();
            return;
        }

        if (e.PropertyName != nameof(DiveDetailViewModel.Chart))
        {
            return;
        }

        _drawable.Path = _viewModel.Chart;

        // Design motion (Interactive.dc.html): drawLine 1.8s ease with a .15s delay, then
        // fillFade .8s ease starting at 1.4s — one composite 2.2s timeline, ONCE per view,
        // honoring reduced-motion (render complete immediately when reduced).
        if (!_chartAnimated && !MotionSettings.IsReducedMotionEnabled && !_viewModel.Chart.IsEmpty)
        {
            _chartAnimated = true;
            _drawable.Progress = 0;
            _drawable.FillOpacity = 0;

            const double total = 2200.0;
            var timeline = new Animation();
            // drawLine: 150ms → 1950ms of the timeline.
            timeline.Add(150 / total, 1950 / total, new Animation(
                progress => _drawable.Progress = progress, 0, 1, Easing.CubicOut));
            // fillFade: 1400ms → 2200ms of the timeline.
            timeline.Add(1400 / total, 1.0, new Animation(
                opacity => _drawable.FillOpacity = opacity, 0, 1, Easing.CubicOut));
            // One shared invalidate per tick.
            timeline.Add(0, 1, new Animation(_ => ChartView.Invalidate(), 0, 1));
            timeline.Commit(this, "TrimixChartDrawIn", rate: 16, length: (uint)total, Easing.Linear);
        }
        else
        {
            _drawable.Progress = 1;
            _drawable.FillOpacity = 1;
            ChartView.Invalidate();
        }
    }
}
