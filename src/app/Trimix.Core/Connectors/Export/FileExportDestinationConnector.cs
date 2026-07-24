using Trimix.Contracts.Dtos;
using Trimix.Core.Abstractions;

namespace Trimix.Core.Connectors.Export;

/// <summary>
/// The honest, ToS-clean "push to PADI/DAN" (architecture overview §2, research §2 Bucket-A): renders a
/// dive to a standards-based file (UDDF / DL7 / PDF) the diver controls, then hands it to the platform
/// share/save sink. No cloud, no vendor ToS, no credentials — <see cref="LegalPosture.UserFile"/>, ships
/// on. One connector is registered per <see cref="IDiveExporter"/> format; the descriptor's fidelity and
/// id follow the wrapped exporter.
/// </summary>
public sealed class FileExportDestinationConnector(IDiveExporter exporter, IExportFileSink sink)
    : IDiveDestinationConnector
{
    public ConnectorDescriptor Descriptor { get; } = new(
        ConnectorId: "export_" + exporter.FileExtension,
        Kind: ConnectorKind.FileExport, // a destination, not a FileImport source (was mislabelled)
        Legal: LegalPosture.UserFile,
        Fidelity: exporter.Fidelity,
        Auth: AuthModel.None,
        Platforms: Platforms.Both,
        EnabledByDefault: true);

    /// <summary>No auth: exporting the diver's own dive to a file needs no account or consent.</summary>
    public Task<ConnectorAuthResult> AuthenticateAsync(CancellationToken ct = default) =>
        Task.FromResult(new ConnectorAuthResult(ConnectorAuthStatus.Granted));

    public async Task<PushResult> PushDiveAsync(DiveDto dive, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dive);
        try
        {
            var file = exporter.Export(dive);
            var location = await sink.WriteAsync(file, ct).ConfigureAwait(false);
            return new PushResult(PushStatus.Pushed, DestinationId: location, Message: file.FileName);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new PushResult(PushStatus.Failed, DestinationId: Descriptor.ConnectorId, Message: ex.Message);
        }
    }
}
