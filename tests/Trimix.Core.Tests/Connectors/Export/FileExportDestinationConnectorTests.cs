using Trimix.Contracts.Dtos;
using Trimix.Core.Abstractions;
using Trimix.Core.Connectors.Export;
using Xunit;

namespace Trimix.Core.Tests.Connectors.Export;

/// <summary>
/// The clean file-export destination (<see cref="LegalPosture.UserFile"/>): a dive pushed through the
/// connector is rendered by the wrapped <see cref="IDiveExporter"/> and the exact bytes land in the sink.
/// No cloud, no consent, no credentials.
/// </summary>
public class FileExportDestinationConnectorTests
{
    [Fact]
    public void Descriptor_is_clean_userfile_enabled_and_follows_exporter()
    {
        var connector = new FileExportDestinationConnector(new MinimalPdfDiveExporter(), new InMemoryExportFileSink());
        var d = connector.Descriptor;

        Assert.Equal("export_pdf", d.ConnectorId);
        Assert.Equal(ConnectorKind.FileExport, d.Kind); // a destination, not a FileImport source
        Assert.Equal(LegalPosture.UserFile, d.Legal);
        Assert.Equal(AuthModel.None, d.Auth);
        Assert.True(d.EnabledByDefault);
        Assert.Equal(ProfileFidelity.SummaryOnly, d.Fidelity); // inherited from the wrapped PDF exporter
    }

    [Fact]
    public async Task Authenticate_is_granted_without_any_consent()
    {
        var connector = new FileExportDestinationConnector(new Dl7DiveExporter(), new InMemoryExportFileSink());
        var result = await connector.AuthenticateAsync();
        Assert.Equal(ConnectorAuthStatus.Granted, result.Status);
    }

    [Fact]
    public async Task Push_writes_the_exact_exporter_bytes_to_the_sink()
    {
        var exporter = new Dl7DiveExporter();
        var sink = new InMemoryExportFileSink();
        var connector = new FileExportDestinationConnector(exporter, sink);
        var dive = DiveFixture.Trimix();

        var result = await connector.PushDiveAsync(dive);

        Assert.Equal(PushStatus.Pushed, result.Status);

        var expected = exporter.Export(dive);
        var written = Assert.Single(sink.Writes);
        Assert.Equal(expected.FileName, written.FileName);
        Assert.Equal(expected.Content.ToArray(), written.Content.ToArray());
        Assert.Equal(written.FileName, result.Message);
        Assert.StartsWith("memory://", result.DestinationId);
    }

    [Fact]
    public async Task Push_maps_an_exporter_failure_to_a_failed_result()
    {
        var connector = new FileExportDestinationConnector(new ThrowingExporter(), new InMemoryExportFileSink());

        var result = await connector.PushDiveAsync(DiveFixture.AirNoCylinders());

        Assert.Equal(PushStatus.Failed, result.Status);
        Assert.Equal("export_boom", result.DestinationId);
        Assert.Contains("boom", result.Message);
    }

    private sealed class ThrowingExporter : IDiveExporter
    {
        public DiveExportFormat Format => DiveExportFormat.Pdf;

        public string ContentType => "application/pdf";

        public string FileExtension => "boom";

        public ProfileFidelity Fidelity => ProfileFidelity.SummaryOnly;

        public ExportedDive Export(DiveDto dive) => throw new InvalidOperationException("boom");
    }
}
