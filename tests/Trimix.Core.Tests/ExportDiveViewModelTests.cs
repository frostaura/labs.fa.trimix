using Trimix.Contracts.Dtos;
using Trimix.Core.Abstractions;
using Trimix.Core.Connectors.Export;
using Trimix.Core.Storage;
using Trimix.Core.Tests.Fakes;
using Trimix.Core.ViewModels;
using Xunit;
using static Trimix.Core.Tests.Stats.StatsTestFactory;

namespace Trimix.Core.Tests;

/// <summary>
/// The clean file-export chooser: a dive is exported to a standards-based file (UDDF / DL7 / PDF) the
/// diver owns and routes via the OS save/share sheet. Guardrail: only the clean
/// <see cref="ConnectorKind.FileExport"/> (<see cref="LegalPosture.UserFile"/>) destinations are offered —
/// dark, ToS-risk push connectors never appear — and the pushed bytes are exactly the exporter's output.
/// </summary>
public class ExportDiveViewModelTests
{
    private readonly InMemoryLocalStore _store = new();
    private readonly RecordingSink _sink = new();
    private readonly FakeNavigator _navigator = new();
    private readonly FakeDialogs _dialogs = new();

    /// <summary>The three registered clean file-export connectors, sharing one recording sink.</summary>
    private IReadOnlyList<IDiveDestinationConnector> FileExports() =>
    [
        new FileExportDestinationConnector(new UddfDiveExporter(), _sink),
        new FileExportDestinationConnector(new Dl7DiveExporter(), _sink),
        new FileExportDestinationConnector(new MinimalPdfDiveExporter(), _sink),
    ];

    private ExportDiveViewModel CreateViewModel(IEnumerable<IDiveDestinationConnector>? destinations = null) =>
        new(_store, destinations ?? FileExports(), _navigator, _dialogs);

    [Fact]
    public async Task Load_offers_the_three_clean_formats_in_order_and_excludes_dark_push_connectors()
    {
        var dive = Dive(samples: [Sample(0, 0, tank: 200), Sample(60, 30, tank: 150)]);
        await _store.UpsertDiveAsync(dive);

        // A ToS-risk push destination (ships dark) is present in the DI enumerable but must NOT be offered.
        var destinations = new List<IDiveDestinationConnector>(FileExports()) { new DarkPushConnector() };

        var vm = CreateViewModel(destinations);
        await vm.LoadCommand.ExecuteAsync(dive.Id);

        Assert.Equal(3, vm.Formats.Count);
        Assert.Equal(["uddf", "dl7", "pdf"], vm.Formats.Select(f => f.Extension).ToArray());
        Assert.Equal("UDDF", vm.Formats[0].Label);
        Assert.All(vm.Formats, f => Assert.Equal(ConnectorKind.FileExport, f.Connector.Descriptor.Kind));
        Assert.DoesNotContain(vm.Formats, f => f.Connector is DarkPushConnector);
    }

    [Fact]
    public async Task Export_writes_the_exact_exporter_bytes_to_the_sink_and_confirms()
    {
        var dive = Dive(samples: [Sample(0, 0, tank: 200), Sample(60, 30, tank: 150)]);
        await _store.UpsertDiveAsync(dive);

        var vm = CreateViewModel();
        await vm.LoadCommand.ExecuteAsync(dive.Id);
        var uddf = vm.Formats.Single(f => f.Extension == "uddf");
        await vm.ExportCommand.ExecuteAsync(uddf);

        // The bytes that reached the sink are exactly the UDDF exporter's render of the loaded dive.
        var expected = new UddfDiveExporter().Export(vm.Dive!);
        var written = Assert.Single(_sink.Writes);
        Assert.Equal(expected.FileName, written.FileName);
        Assert.Equal(expected.Content.ToArray(), written.Content.ToArray());
        Assert.EndsWith(".uddf", written.FileName);

        // Success is confirmed to the diver (the OS sheet handles the actual save/share), no error alert.
        Assert.Single(_dialogs.Toasts);
        Assert.Empty(_dialogs.Alerts);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Export_surfaces_an_alert_and_writes_nothing_when_the_exporter_fails()
    {
        var dive = Dive();
        await _store.UpsertDiveAsync(dive);

        var vm = CreateViewModel([new FileExportDestinationConnector(new ThrowingExporter(), _sink)]);
        await vm.LoadCommand.ExecuteAsync(dive.Id);
        await vm.ExportCommand.ExecuteAsync(vm.Formats.Single());

        Assert.Empty(_sink.Writes);
        Assert.Empty(_dialogs.Toasts);
        var alert = Assert.Single(_dialogs.Alerts);
        Assert.Equal("Export failed", alert.Title);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Load_exports_the_richest_merged_union_not_one_raw_row()
    {
        // Two rows for one physical dive: a full-fidelity Shearwater (tank pressure) + a depth-only Apple
        // Watch twin. Opening via the depth-only id must still export the merged union (tank retained).
        var shearwater = Dive(
            start: BaseUtc, durationSec: 3000, maxDepthM: 30, sourceVendor: "shearwater",
            samples: [Sample(0, 0, temp: 21, tank: 200), Sample(60, 30, temp: 19, tank: 150)]);
        var appleWatch = Dive(
            start: BaseUtc.AddSeconds(10), durationSec: 2990, maxDepthM: 30.5,
            sourceVendor: "apple_health", samples: [Sample(0, 0, temp: 21)]);
        await _store.UpsertDiveAsync(shearwater);
        await _store.UpsertDiveAsync(appleWatch);

        var vm = CreateViewModel();
        await vm.LoadCommand.ExecuteAsync(appleWatch.Id);

        Assert.NotNull(vm.Dive);
        Assert.Contains(vm.Dive!.ProfileSamples, s => s.TankPressureBar is not null);
    }

    [Fact]
    public async Task Missing_dive_navigates_back_without_offering_formats()
    {
        var vm = CreateViewModel();
        await vm.LoadCommand.ExecuteAsync(Guid.NewGuid());

        Assert.Equal(1, _navigator.BackCount);
        Assert.Empty(vm.Formats);
        Assert.Null(vm.Dive);
    }

    /// <summary>Records exported artifacts instead of touching the filesystem or an OS share sheet.</summary>
    private sealed class RecordingSink : IExportFileSink
    {
        public List<ExportedDive> Writes { get; } = [];

        public Task<string> WriteAsync(ExportedDive file, CancellationToken ct = default)
        {
            Writes.Add(file);
            return Task.FromResult("test://" + file.FileName);
        }
    }

    /// <summary>An exporter that always throws — drives the connector's Failed → alert path.</summary>
    private sealed class ThrowingExporter : IDiveExporter
    {
        public DiveExportFormat Format => DiveExportFormat.Uddf;

        public string ContentType => "application/xml";

        public string FileExtension => "uddf";

        public ProfileFidelity Fidelity => ProfileFidelity.FullProfile;

        public ExportedDive Export(DiveDto dive) => throw new InvalidOperationException("render blew up");
    }

    /// <summary>A ToS-risk push destination that ships dark — must never be offered by the export chooser.</summary>
    private sealed class DarkPushConnector : IDiveDestinationConnector
    {
        public ConnectorDescriptor Descriptor { get; } = new(
            "padi_push",
            ConnectorKind.CloudApi,
            LegalPosture.ToSRiskReverseEng,
            ProfileFidelity.SummaryOnly,
            AuthModel.UserCredential,
            Platforms.Both,
            EnabledByDefault: false);

        public Task<ConnectorAuthResult> AuthenticateAsync(CancellationToken ct = default) =>
            Task.FromResult(new ConnectorAuthResult(ConnectorAuthStatus.Unavailable));

        public Task<PushResult> PushDiveAsync(DiveDto dive, CancellationToken ct = default) =>
            Task.FromResult(new PushResult(PushStatus.Unsupported));
    }
}
