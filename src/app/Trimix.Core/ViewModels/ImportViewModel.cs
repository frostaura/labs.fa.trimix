using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trimix.Contracts;
using Trimix.Core.Abstractions;
using Trimix.Core.DiveComputer;

namespace Trimix.Core.ViewModels;

/// <summary>Import screen states (PRD Flow B).</summary>
public enum ImportStep
{
    Rationale = 0,
    PermissionDenied = 1,
    Scanning = 2,
    Importing = 3,
    Results = 4,
}

/// <summary>
/// BLE import flow (PRD Flow B — the differentiator that leads to the purchase moment):
/// permission rationale → just-in-time BLE permission (<c>ble_permission_*</c> events) → scan →
/// device list → progress → results. When the free import-session cap truncates the fetch, this
/// VM fires <c>paywall_shown</c> with <c>trigger=ble_import_cap</c> and opens the paywall.
/// </summary>
public partial class ImportViewModel(
    IImportService importService,
    IPermissions permissions,
    IAnalytics analytics,
    INavigator navigator,
    IUserDialogs dialogs) : ObservableObject
{
    [ObservableProperty]
    private ImportStep _step = ImportStep.Rationale;

    public ObservableCollection<DiscoveredComputer> Devices { get; } = [];

    [ObservableProperty]
    private string _progressStage = "";

    [ObservableProperty]
    private int _progressDone;

    [ObservableProperty]
    private int _progressTotal;

    [ObservableProperty]
    private int _importedCount;

    [ObservableProperty]
    private int _fetchedCount;

    [ObservableProperty]
    private bool _capReached;

    [ObservableProperty]
    private bool _siteMatched;

    /// <summary>Rationale-screen CTA: request BLE permission just-in-time, then scan.</summary>
    [RelayCommand]
    private async Task RequestPermissionAndScanAsync(CancellationToken cancellationToken)
    {
        analytics.Track(FunnelEvents.BlePermissionRequested, new Dictionary<string, string>
        {
            [FunnelEvents.Props.Outcome] = "requested",
        });

        var granted = await permissions.RequestBluetoothAsync(cancellationToken);
        analytics.Track(
            granted ? FunnelEvents.BlePermissionGranted : FunnelEvents.BlePermissionDenied,
            new Dictionary<string, string>
            {
                [FunnelEvents.Props.Outcome] = granted ? "granted" : "denied",
            });

        if (!granted)
        {
            Step = ImportStep.PermissionDenied;
            return;
        }

        Step = ImportStep.Scanning;
        Devices.Clear();
        await foreach (var computer in importService.ScanAsync(cancellationToken))
        {
            Devices.Add(computer);
        }
    }

    /// <summary>Device-list selection: fetch, save (cap-aware), then results / purchase moment.</summary>
    [RelayCommand]
    private async Task ImportFromAsync(DiscoveredComputer computer, CancellationToken cancellationToken)
    {
        Step = ImportStep.Importing;
        var progress = new Progress<ImportProgress>(p =>
        {
            ProgressStage = p.Stage;
            ProgressDone = p.Done;
            ProgressTotal = p.Total;
        });

        try
        {
            var result = await importService.ImportAsync(computer, onCapReached: null, progress, cancellationToken);
            ImportedCount = result.ImportedDives.Count;
            FetchedCount = result.FetchedCount;
            CapReached = result.CapReached;
            SiteMatched = result.SiteMatched;
            Step = ImportStep.Results;

            if (result.CapReached)
            {
                // THE purchase moment (PRD Layer 1): the free cap interrupted "import all my dives".
                analytics.Track(FunnelEvents.PaywallShown, new Dictionary<string, string>
                {
                    [FunnelEvents.Props.Trigger] = FunnelEvents.Values.TriggerBleImportCap,
                    [FunnelEvents.Props.EntitlementState] = FunnelEvents.Values.EntitlementStateFree,
                });
                await navigator.GoToAsync(Routes.Paywall);
            }
        }
        catch (DiveComputerProtocolException ex)
        {
            Step = ImportStep.Rationale;
            await dialogs.AlertAsync(
                "Import failed",
                $"Could not read dives from {computer.Model}: {ex.Message} Move closer to the computer and try again.");
        }
    }

    [RelayCommand]
    private Task DoneAsync() => navigator.GoBackAsync();
}
