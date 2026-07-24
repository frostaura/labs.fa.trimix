namespace Trimix.Core.Abstractions;

/// <summary>
/// Decodes a Garmin-format <c>.FIT</c> activity file (raw bytes) into <see cref="ImportedDive"/>s. This is the
/// seam the cloud connectors (Garmin, Suunto) depend on for the byte-level FIT decode: a cloud connector's job
/// ends at "downloaded FIT bytes"; it hands them here and receives <see cref="ImportedDive"/>s.
///
/// <para><b>Integrator note:</b> the file-import branch ships a FIT decoder
/// (<c>Connectors/FileImport/FitReader</c> / <c>GarminFitParser</c>) whose <c>IDiveFileParser</c> takes a picked
/// file. Provide the real implementation of this port by adapting that byte-level reader (it already produces
/// <see cref="ImportedDive"/>), and drop the <c>PendingFitActivityParser</c> placeholder. Nothing here
/// reimplements FIT parsing.</para>
/// </summary>
public interface IFitActivityParser
{
    /// <summary>Parses the dives contained in one <c>.FIT</c> file (usually exactly one dive per activity).</summary>
    IReadOnlyList<ImportedDive> Parse(ReadOnlyMemory<byte> fitBytes);
}
