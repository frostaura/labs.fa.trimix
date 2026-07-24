using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trimix.Contracts.Dtos;
using Trimix.Core.Abstractions;
using Trimix.Core.Stats;

namespace Trimix.Core.ViewModels;

/// <summary>
/// One offered export format: friendly <see cref="Label"/>/<see cref="Blurb"/> plus the clean
/// <see cref="IDiveDestinationConnector"/> (a <see cref="ConnectorKind.FileExport"/>) that renders and
/// sinks it. Carrying the connector (not a format enum) keeps the VM decoupled from the exporter set —
/// a newly-registered file exporter surfaces here automatically.
/// </summary>
public sealed record ExportFormatOption(string Extension, string Label, string Blurb, IDiveDestinationConnector Connector);

/// <summary>
/// Export a dive to a standards-based file the diver owns (aggregator architecture §2, research §2
/// Bucket-A): the honest, ToS-free answer to "push to PADI/DAN" — <em>we</em> never write to a cert
/// agency; the diver exports UDDF / DL7 / PDF and hands it over themselves. Each format is a registered
/// <see cref="FileExportDestinationConnector"/>; picking one runs <c>PushDiveAsync</c>, which renders the
/// bytes and hands them to the platform save/share sheet (the real <c>IExportFileSink</c>). No account,
/// no cloud, no consent — <see cref="LegalPosture.UserFile"/>.
///
/// <para>Exports the same richest-fidelity-per-field union the detail screen shows (§3 / §8.2), so the
/// file carries every source's best channels rather than one raw row.</para>
/// </summary>
public partial class ExportDiveViewModel(
    ILocalStore store,
    IEnumerable<IDiveDestinationConnector> destinations,
    INavigator navigator,
    IUserDialogs dialogs) : ObservableObject
{
    /// <summary>Shell route (owned here to avoid touching shared route constants).</summary>
    public const string Route = "export-dive";

    // Curated copy + order for the three clean formats, keyed by the exporter's file extension
    // (ConnectorId == "export_" + extension). Any additional registered file-export format with no
    // curated copy still surfaces (bare extension label) so the list can never silently drop a format.
    private static readonly IReadOnlyList<(string Ext, string Label, string Blurb)> Copy =
    [
        ("uddf", "UDDF", "Universal Dive Data Format — the full profile. Imports into Subsurface, MacDive, DIVEROUT and most logbooks."),
        ("dl7", "DAN DL7", "The text format DAN and PADI accept — hand it to your agency yourself."),
        ("pdf", "PDF summary", "A one-page, human-readable summary to print or share."),
    ];

    private const string ExportPrefix = "export_";

    private readonly IReadOnlyList<IDiveDestinationConnector> _fileExports =
        [.. destinations.Where(d => d.Descriptor.Kind == ConnectorKind.FileExport)];

    [ObservableProperty]
    private DiveDto? _dive;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>The clean file-export formats offered, in curated order.</summary>
    [ObservableProperty]
    private IReadOnlyList<ExportFormatOption> _formats = [];

    [RelayCommand]
    private async Task LoadAsync(Guid diveId)
    {
        var dives = await store.GetDivesAsync();
        var active = dives.Where(d => !d.Deleted).ToList();
        if (active.All(d => d.Id != diveId))
        {
            await navigator.GoBackAsync();
            return;
        }

        // Presentation-layer merge (§3 / §8.2): export the richest union of the physical dive's sources,
        // matching exactly what the detail screen renders. Non-destructive; the raw rows are untouched.
        var cluster = DiveMergePolicy.Merge(active).FirstOrDefault(m => m.Sources.Any(s => s.Id == diveId));
        Dive = cluster?.Primary ?? active.First(d => d.Id == diveId);

        Formats = BuildFormats();
    }

    /// <summary>Renders the dive as <paramref name="option"/> and hands it to the OS save/share sheet.</summary>
    [RelayCommand]
    private async Task ExportAsync(ExportFormatOption option)
    {
        ArgumentNullException.ThrowIfNull(option);
        if (Dive is not { } dive || IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var result = await option.Connector.PushDiveAsync(dive);
            if (result.Status == PushStatus.Pushed)
            {
                await dialogs.ToastAsync($"{option.Label} ready to save or share.");
            }
            else
            {
                await dialogs.AlertAsync(
                    "Export failed",
                    $"Could not create the {option.Label} file. {result.Message}".Trim());
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task Close() => navigator.GoBackAsync();

    private IReadOnlyList<ExportFormatOption> BuildFormats()
    {
        var options = new List<ExportFormatOption>();

        // Curated formats first, in order.
        foreach (var (ext, label, blurb) in Copy)
        {
            var connector = _fileExports.FirstOrDefault(c => c.Descriptor.ConnectorId == ExportPrefix + ext);
            if (connector is not null)
            {
                options.Add(new ExportFormatOption(ext, label, blurb, connector));
            }
        }

        // Any additional registered file-export format we have no curated copy for (never drop one).
        foreach (var connector in _fileExports)
        {
            if (options.Any(o => o.Connector == connector))
            {
                continue;
            }

            var id = connector.Descriptor.ConnectorId;
            var ext = id.StartsWith(ExportPrefix, StringComparison.Ordinal) ? id[ExportPrefix.Length..] : id;
            options.Add(new ExportFormatOption(ext, ext.ToUpperInvariant(), "", connector));
        }

        return options;
    }
}
