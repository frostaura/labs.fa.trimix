using System.ComponentModel;
using Trimix.App.Controls;
using Trimix.Core;
using Trimix.Core.ViewModels;

namespace Trimix.App.Pages;

[QueryProperty(nameof(DiveId), Routes.Params.DiveId)]
public partial class DiveDetailPage : ContentPage
{
    private readonly DiveDetailViewModel _viewModel;
    private readonly DepthChartDrawable _drawable = new();
    private bool _chartAnimated;

    public DiveDetailPage(DiveDetailViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        BindingContext = viewModel;
        ChartView.Drawable = _drawable;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
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

        // Design doc motion: the profile chart draws in over 400ms ONCE per view, honoring
        // reduced-motion (render complete immediately when reduced).
        if (!_chartAnimated && !MotionSettings.IsReducedMotionEnabled && !_viewModel.Chart.IsEmpty)
        {
            _chartAnimated = true;
            _drawable.Progress = 0;
            this.Animate(
                "TrimixChartDrawIn",
                new Animation(progress =>
                {
                    _drawable.Progress = progress;
                    ChartView.Invalidate();
                }),
                length: 400,
                easing: Easing.CubicOut);
        }
        else
        {
            _drawable.Progress = 1;
            ChartView.Invalidate();
        }
    }
}
