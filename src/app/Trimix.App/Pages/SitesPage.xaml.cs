using Trimix.Core.ViewModels;

namespace Trimix.App.Pages;

public partial class SitesPage : ContentPage
{
    private readonly SitesViewModel _viewModel;

    public SitesPage(SitesViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // Re-derive every appearance: sites aggregate from the local dive store (new syncs and the
        // units toggle both change what the rows show).
        await _viewModel.LoadCommand.ExecuteAsync(null);
    }
}
