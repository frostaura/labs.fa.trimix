using Trimix.Core;
using Trimix.Core.ViewModels;

namespace Trimix.App.Pages;

/// <summary>
/// Export chooser for a single dive (UDDF / DL7 / PDF → OS save/share sheet). Reached as a modal route
/// from the dive-detail "Export" action; the target dive arrives via the shared <see cref="Routes.Params.DiveId"/>
/// query parameter and is loaded on appearance.
/// </summary>
[QueryProperty(nameof(DiveId), Routes.Params.DiveId)]
public partial class ExportDivePage : ContentPage
{
    private readonly ExportDiveViewModel _viewModel;

    public ExportDivePage(ExportDiveViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        BindingContext = viewModel;
    }

    public Guid DiveId { get; set; }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (DiveId != Guid.Empty)
        {
            await _viewModel.LoadCommand.ExecuteAsync(DiveId);
        }
    }
}
