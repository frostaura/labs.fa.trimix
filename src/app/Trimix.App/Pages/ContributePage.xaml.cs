using Trimix.Core;
using Trimix.Core.ViewModels;

namespace Trimix.App.Pages;

[QueryProperty(nameof(SiteId), Routes.Params.SiteId)]
public partial class ContributePage : ContentPage
{
    private readonly ContributeViewModel _viewModel;

    public ContributePage(ContributeViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        BindingContext = viewModel;
    }

    /// <summary>Optional: absent proposes a brand-new site (VM semantics).</summary>
    public Guid SiteId
    {
        get => _viewModel.SiteId ?? Guid.Empty;
        set => _viewModel.SiteId = value == Guid.Empty ? null : value;
    }
}
