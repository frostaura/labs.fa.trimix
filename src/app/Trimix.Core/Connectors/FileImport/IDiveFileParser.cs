using Trimix.Core.Abstractions;

namespace Trimix.Core.Connectors.FileImport;

/// <summary>
/// A dependency-free parser mapping the bytes of ONE dive-log file format to <see cref="ImportedDive"/>(s)
/// — the frozen seam boundary (architecture overview §2). Pure C#: no I/O, no network, fully unit-testable
/// against committed fixtures. Parsers are registered multi-instance; <see cref="FileImportSourceConnector"/>
/// sniffs the format via <see cref="CanParse"/> and delegates to the first match. Map every rich channel the
/// format carries (NDL, ceiling, CNS, tank pressure, HR, gas cylinders) and leave the rest null.
/// </summary>
public interface IDiveFileParser
{
    /// <summary>Stable format id for provenance/UX (e.g. <c>garmin_fit</c>, <c>uddf</c>, <c>dan_dl7</c>, <c>suunto_json</c>).</summary>
    string FormatId { get; }

    /// <summary>Cheap content/extension sniff: true when this parser recognizes the file. Never throws.</summary>
    bool CanParse(PickedFile file);

    /// <summary>Parses every dive in the file. Throws <see cref="DiveFileParseException"/> on malformed input.</summary>
    IReadOnlyList<ImportedDive> Parse(PickedFile file);
}

/// <summary>A dive-log file was malformed, truncated, or not the format the parser expected.</summary>
public sealed class DiveFileParseException(string message) : Exception(message);
