using System.ComponentModel;
using Trimix.Core.ViewModels;

namespace Trimix.App.Pages;

public partial class StatsPage : ContentPage
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
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadCommand.ExecuteAsync(null);
    }
}
