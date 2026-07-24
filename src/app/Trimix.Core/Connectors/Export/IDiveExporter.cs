using Trimix.Contracts.Dtos;
using Trimix.Core.Abstractions;

namespace Trimix.Core.Connectors.Export;

/// <summary>The clean, standards-based file formats a diver can export and hand to any agency themselves.</summary>
public enum DiveExportFormat
{
    /// <summary>Universal Dive Data Format (XML) — broadest interop (Subsurface, MacDive, DIVEROUT, …).</summary>
    Uddf,

    /// <summary>DAN DL7 (HL7-style text) — the only format PADI/DAN ever ingested.</summary>
    Dl7,

    /// <summary>A minimal, dependency-free single-page PDF summary (human-readable share).</summary>
    Pdf,
}

/// <summary>A rendered export artifact — the bytes a diver saves or shares.</summary>
public sealed record ExportedDive(
    DiveExportFormat Format,
    string FileName,
    string ContentType,
    IReadOnlyList<byte> Content);

/// <summary>
/// Renders a canonical <see cref="DiveDto"/> into a standards-based file the diver controls. This is the
/// honest, ToS-clean answer to "push to PADI/DAN": <em>we</em> never write to a cert agency; the diver
/// exports a file and hands it over themselves. Pure — no I/O, no platform dependency, fully unit-testable.
/// </summary>
public interface IDiveExporter
{
    DiveExportFormat Format { get; }

    /// <summary>MIME type of the produced artifact.</summary>
    string ContentType { get; }

    /// <summary>Lowercase file extension without a dot (e.g. <c>uddf</c>).</summary>
    string FileExtension { get; }

    /// <summary>Fidelity this format preserves — UDDF/DL7 carry the full profile; PDF is a summary.</summary>
    ProfileFidelity Fidelity { get; }

    /// <summary>Renders <paramref name="dive"/> to bytes. Deterministic for a given dive.</summary>
    ExportedDive Export(DiveDto dive);
}
