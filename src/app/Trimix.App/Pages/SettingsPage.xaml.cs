using CommunityToolkit.Mvvm.Input;
using Trimix.App.Controls;
using Trimix.Core.ViewModels;

namespace Trimix.App.Pages;

/// <summary>Settings — the reference adoption of the shared UI foundation (TrimixPage screenIn
/// entrance + PageHeader with back + AppIcon glyphs + DEBUG-only developer card).</summary>
public partial class SettingsPage : TrimixPage
{
    private readonly SettingsViewModel _viewModel;

    public SettingsPage(SettingsViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        BindingContext = viewModel;
    }

    /// <summary>
    /// GDPR/CCPA export: the VM serializes the local-first dive store; the App layer owns the
    /// platform half — write to app cache and open the share sheet.
    /// </summary>
    private async void OnExportClicked(object? sender, EventArgs e)
    {
        var command = (IAsyncRelayCommand)_viewModel.ExportDataCommand;
        await command.ExecuteAsync(null);
        if (command.ExecutionTask is Task<string> { IsCompletedSuccessfully: true } exportTask)
        {
            var path = Path.Combine(FileSystem.CacheDirectory, "trimix-export.json");
            await File.WriteAllTextAsync(path, exportTask.Result);
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "Trimix data export",
                File = new ShareFile(path, "application/json"),
            });
        }
    }

    private async void OnLicensesClicked(object? sender, EventArgs e) =>
        await DisplayAlert(
            "Open-source licenses",
            "Trimix is built with open-source software (OSS by default — FrostAura doctrine):\n\n" +
            "• .NET MAUI — MIT License\n" +
            "• CommunityToolkit.Mvvm — MIT License\n" +
            "• sqlite-net-pcl — MIT License\n" +
            "• SQLitePCLRaw — Apache License 2.0\n" +
            "• Google Play Billing client binding — Apache License 2.0\n\n" +
            "Full license texts ship in the repository alongside each dependency.",
            "Close");
}
