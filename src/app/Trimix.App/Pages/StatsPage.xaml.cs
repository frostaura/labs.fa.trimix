using System.ComponentModel;
using Trimix.App.Services;
using CommunityToolkit.Mvvm.Input;
using Trimix.App.Controls;
using Trimix.Core.ViewModels;

namespace Trimix.App.Pages;

/// <summary>"You" code-behind (Phase B): TrimixPage screenIn entrance and the animated Nitrox MOD
/// fill bar (width tween on O2 change per the design; the VM's ModFillFraction drives ProgressTo so
/// the bar glides instead of jumping).</summary>
public partial class StatsPage : TrimixPage
{
    private readonly StatsViewModel _viewModel;

    public StatsPage(StatsViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        BindingContext = viewModel;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StatsViewModel.ModFillFraction))
        {
            AnimateModFill();
        }
    }

    /// <summary>Drag start: the EAN badge pops up ~30% so the eye catches the changing blend while
    /// the slider is being dragged (reduced motion snaps to the enlarged size, no tween).</summary>
    private void OnEanDragStarted(object? sender, EventArgs e)
    {
        // Grabbing and releasing the slider are both discrete choices, so they get the Selection cue —
        // the same one the section switcher and the chart scrub use.
        Feedback.Selection();

        if (MotionSettings.IsReducedMotionEnabled)
        {
            EanBadge.Scale = 1.3;
            return;
        }

        _ = EanBadge.ScaleTo(1.3, 120, Easing.CubicOut);
    }

    /// <summary>Drag end: the badge settles back to its resting size.</summary>
    private void OnEanDragCompleted(object? sender, EventArgs e)
    {
        Feedback.Selection();

        if (MotionSettings.IsReducedMotionEnabled)
        {
            EanBadge.Scale = 1.0;
            return;
        }

        _ = EanBadge.ScaleTo(1.0, 150, Easing.CubicOut);
    }

    /// <summary>Design: the MOD fill bar's width tweens .35s on every O2 change (reduced motion
    /// snaps to the final value).</summary>
    private void AnimateModFill()
    {
        if (MotionSettings.IsReducedMotionEnabled)
        {
            ModFillBar.Progress = _viewModel.ModFillFraction;
            return;
        }

        _ = ModFillBar.ProgressTo(_viewModel.ModFillFraction, 350, Easing.CubicOut);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing(); // TrimixPage screenIn
        await _viewModel.LoadCommand.ExecuteAsync(null);
    }

    /// <summary>
    /// GDPR/CCPA export (folded in from the retired Settings page): the VM serializes the local-first
    /// dive store; the App layer owns the platform half — write to app cache and open the share sheet.
    /// </summary>
    private async void OnExportClicked(object? sender, EventArgs e)
    {
        var command = (IAsyncRelayCommand)_viewModel.ExportDataCommand;
        await command.ExecuteAsync(null);
        if (command.ExecutionTask is Task<string> { IsCompletedSuccessfully: true } exportTask)
        {
            var path = Path.Combine(FileSystem.CacheDirectory, "trimix-export.json");
            await File.WriteAllTextAsync(path, exportTask.Result);
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "Trimix data export",
                File = new ShareFile(path, "application/json"),
            });
        }
    }
}
