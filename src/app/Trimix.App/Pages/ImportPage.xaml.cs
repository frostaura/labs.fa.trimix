using Trimix.Core.ViewModels;

namespace Trimix.App.Pages;

public partial class ImportPage : ContentPage
{
    public ImportPage(ImportViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    private void OnOpenSettingsClicked(object? sender, EventArgs e) =>
        Microsoft.Maui.ApplicationModel.AppInfo.Current.ShowSettingsUI();
}
