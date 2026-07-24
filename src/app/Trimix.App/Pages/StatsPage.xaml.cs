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
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadCommand.ExecuteAsync(null);
    }
}
