using Trimix.Core;
using Trimix.Core.ViewModels;

namespace Trimix.App.Pages;

/// <summary>
/// Provider sign-in sheet (prototype S6). The <c>connectorId</c> query parameter selects which
/// connector the sheet authorizes; everything else binds to <see cref="ProviderOAuthViewModel"/>.
/// </summary>
[QueryProperty(nameof(ConnectorId), Routes.Params.ConnectorId)]
public partial class ProviderOAuthPage : ContentPage
{
    private readonly ProviderOAuthViewModel _viewModel;

    public ProviderOAuthPage(ProviderOAuthViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        BindingContext = viewModel;
    }

    public string ConnectorId
    {
        set => _viewModel.ConnectorId = value;
    }
}
