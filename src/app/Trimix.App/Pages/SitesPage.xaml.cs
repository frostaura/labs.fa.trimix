using Trimix.Core.ViewModels;

namespace Trimix.App.Pages;

public partial class SitesPage : ContentPage
{
    private readonly SitesViewModel _viewModel;
    private bool _loadedOnce;

    public SitesPage(SitesViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!_loadedOnce)
        {
            _loadedOnce = true;
            await _viewModel.SearchCommand.ExecuteAsync(null); // Initial nearby list.
        }
    }
}
