using System.ComponentModel;
using Trimix.App.Controls;
using Trimix.Core.ViewModels;

namespace Trimix.App.Pages;

/// <summary>"You" code-behind (Phase B): TrimixPage screenIn entrance, inline name-edit focus/blur
/// wiring, and the animated Nitrox MOD fill bar (width tween on O2 change per the design; the VM's
/// ModFillFraction drives ProgressTo so the bar glides instead of jumping).</summary>
public partial class StatsPage : TrimixPage
{
    private readonly StatsViewModel _viewModel;

    public StatsPage(StatsViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        BindingContext = viewModel;

        // Inline name edit (C8): blur saves (like the prototype); entering edit mode focuses the field.
        NameEntry.Unfocused += (_, _) =>
        {
            if (_viewModel.IsNameEditing)
            {
                _viewModel.SaveNameCommand.Execute(null);
            }
        };
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StatsViewModel.IsNameEditing) && _viewModel.IsNameEditing)
        {
            // Focus after the Entry becomes visible.
            Dispatcher.Dispatch(() => NameEntry.Focus());
        }

        if (e.PropertyName == nameof(StatsViewModel.ModFillFraction))
        {
            AnimateModFill();
        }
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
}
