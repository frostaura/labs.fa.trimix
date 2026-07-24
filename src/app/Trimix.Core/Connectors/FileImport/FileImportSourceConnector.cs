using System.Globalization;
using Trimix.Core.Abstractions;

namespace Trimix.Core.Connectors.FileImport;

/// <summary>
/// The user-supplied file-import source connector (architecture overview §2). The diver moves their <em>own</em>
/// data — a Garmin Descent <c>.FIT</c>, a UDDF/DL7 export, or a Suunto-app JSON — through the platform picker;
/// no automated access to any vendor server, so this connector is <see cref="LegalPosture.UserFile"/> and ships
/// on. It is the only clean Garmin path and a universal fallback for every closed vendor.
///
/// Flow mirrors <c>BleDeviceSourceConnector</c>: <see cref="ListDivesSinceAsync"/> picks a file, sniffs the
/// format via the registered <see cref="IDiveFileParser"/>s, parses to <see cref="ImportedDive"/>s and buffers
/// them; <see cref="FetchProfileAsync"/> returns a buffered dive by index. From the <see cref="ImportedDive"/>
/// boundary the existing <c>ImportService.ImportFromConnectorAsync</c> downstream runs unchanged. Per-session
/// state (<c>_buffer</c>) matches the single-import-at-a-time singleton model.
/// </summary>
public sealed class FileImportSourceConnector(
    IFilePicker picker,
    IEnumerable<IDiveFileParser> parsers) : IDiveSourceConnector
{
    private IReadOnlyList<ImportedDive> _buffer = [];

    public ConnectorDescriptor Descriptor { get; } = new(
        ConnectorId: "file_import",
        Kind: ConnectorKind.FileImport,
        Legal: LegalPosture.UserFile,
        Fidelity: ProfileFidelity.FullProfile,
        Auth: AuthModel.None,
        Platforms: Platforms.Both,
        EnabledByDefault: true);

    /// <summary>User-file import needs no auth (the picker's OS consent covers file access).</summary>
    public Task<ConnectorAuthResult> AuthenticateAsync(CancellationToken ct = default) =>
        Task.FromResult(new ConnectorAuthResult(ConnectorAuthStatus.Granted));

    public async Task<IReadOnlyList<DiveRef>> ListDivesSinceAsync(SyncCursor cursor, CancellationToken ct = default)
    {
        var file = await picker.PickDiveFileAsync(ct).ConfigureAwait(false);
        if (file is null)
        {
            _buffer = []; // diver cancelled the picker
            return [];
        }

        var parser = parsers.FirstOrDefault(p => p.CanParse(file))
            ?? throw new DiveFileParseException(
                $"Unrecognized dive-log file '{file.FileName}'. Supported: Garmin .FIT, UDDF, DAN DL7, Suunto JSON.");

        _buffer = parser.Parse(file);

        var refs = new List<DiveRef>(_buffer.Count);
        for (var i = 0; i < _buffer.Count; i++)
        {
            var dive = _buffer[i];
            refs.Add(new DiveRef(
                Descriptor.ConnectorId,
                i.ToString(CultureInfo.InvariantCulture),
                dive.StartUtc,
                dive.DurationSec,
                parser.FormatId)); // format id rides on Label for provenance/UX
        }

        return refs;
    }

    public Task<ImportedDive> FetchProfileAsync(DiveRef diveRef, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(diveRef);
        var index = int.Parse(diveRef.SourceId, CultureInfo.InvariantCulture);
        return Task.FromResult(_buffer[index]);
    }
}
