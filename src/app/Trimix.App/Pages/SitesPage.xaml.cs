using Trimix.App.Controls;
using Trimix.Core.ViewModels;

namespace Trimix.App.Pages;

/// <summary>Sites code-behind (Phase B): TrimixPage screenIn entrance + the design's rowIn stagger
/// on the derived site rows.</summary>
public partial class SitesPage : TrimixPage
{
    private readonly SitesViewModel _viewModel;
    private DateTime _rowBatchStart = DateTime.MinValue;
    private int _rowBatchIndex;

    public SitesPage(SitesViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing(); // TrimixPage screenIn
        // Re-derive every appearance: sites aggregate from the local dive store (new syncs and the
        // units toggle both change what the rows show).
        await _viewModel.LoadCommand.ExecuteAsync(null);
    }

    /// <summary>rowIn stagger (design: <c>rowIn .4s ease</c>), same batching as the Logbook rows.</summary>
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

        var delay = Math.Min(_rowBatchIndex++ * 60, 540);
        row.Opacity = 0;
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
