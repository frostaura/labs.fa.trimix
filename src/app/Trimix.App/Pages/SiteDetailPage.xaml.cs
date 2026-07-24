using Trimix.Core;
using Trimix.Core.ViewModels;

namespace Trimix.App.Pages;

[QueryProperty(nameof(SiteId), Routes.Params.SiteId)]
public partial class SiteDetailPage : ContentPage
{
    private readonly SiteDetailViewModel _viewModel;

    public SiteDetailPage(SiteDetailViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        BindingContext = viewModel;
    }

    public Guid SiteId { get; set; }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (SiteId != Guid.Empty)
        {
            await _viewModel.LoadCommand.ExecuteAsync(SiteId);
        }
    }
}
