using Trimix.Core.ViewModels;

namespace Trimix.App.Pages;

public partial class LogbookPage : ContentPage
{
    private readonly LogbookViewModel _viewModel;

    public LogbookPage(LogbookViewModel viewModel)
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
