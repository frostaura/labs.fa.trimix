using Trimix.Core.ViewModels;

namespace Trimix.App.Pages;

public partial class ManualEntryPage : ContentPage
{
    private readonly ManualEntryViewModel _viewModel;

    public ManualEntryPage(ManualEntryViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // The DatePicker shows today by default; mirror that into the VM's nullable Date so the
        // visible value and the validated value can never disagree.
        _viewModel.Date ??= DateTime.Today;
    }
}
