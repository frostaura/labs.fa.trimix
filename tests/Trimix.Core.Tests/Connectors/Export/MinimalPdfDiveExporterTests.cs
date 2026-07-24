using System.Globalization;
using System.Text;
using Trimix.Contracts.Dtos;
using Trimix.Core.Abstractions;
using Trimix.Core.Connectors.Export;
using Xunit;

namespace Trimix.Core.Tests.Connectors.Export;

/// <summary>
/// Dependency-free PDF summary export: a valid <c>%PDF-1.4</c> header, a single-page object graph
/// (Catalog → Pages/Count 1 → Page → Font → content stream), and a byte-accurate xref table.
/// </summary>
public class MinimalPdfDiveExporterTests
{
    // Latin-1 decode: the exporter writes 1 byte per char so string indices == byte offsets (xref invariant).
    private static string ExportText(DiveDto dive) =>
        Encoding.Latin1.GetString([.. new MinimalPdfDiveExporter().Export(dive).Content]);

    [Fact]
    public void Descriptor_metadata_is_pdf_summary()
    {
        var ex = new MinimalPdfDiveExporter();
        Assert.Equal(DiveExportFormat.Pdf, ex.Format);
        Assert.Equal("pdf", ex.FileExtension);
        Assert.Equal("application/pdf", ex.ContentType);
        Assert.Equal(ProfileFidelity.SummaryOnly, ex.Fidelity);
    }

    [Fact]
    public void Starts_with_pdf_header()
    {
        var text = ExportText(DiveFixture.Trimix());
        Assert.StartsWith("%PDF-1.4", text);
        Assert.Equal("%PDF-", text[..5]);
    }

    [Fact]
    public void Has_single_page_object_structure()
    {
        var text = ExportText(DiveFixture.Trimix());
        Assert.Contains("/Type /Catalog", text);
        Assert.Contains("/Type /Pages /Kids [3 0 R] /Count 1", text); // exactly one page
        Assert.Contains("/Type /Page ", text);                        // the page object (note trailing space, not "/Pages")
        Assert.Contains("/BaseFont /Helvetica", text);
        Assert.EndsWith("%%EOF\n", text);
    }

    [Fact]
    public void Xref_startxref_offset_points_at_the_xref_table()
    {
        var text = ExportText(DiveFixture.Trimix());
        const string marker = "startxref\n";
        var idx = text.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(idx >= 0, "startxref marker present");

        var after = text[(idx + marker.Length)..];
        var offset = int.Parse(after[..after.IndexOf('\n')], CultureInfo.InvariantCulture);

        // 1 byte per char => string index equals byte offset; the recorded offset must land on the table.
        Assert.StartsWith("xref\n0 6\n", text[offset..]);
    }

    [Fact]
    public void Summary_fields_are_rendered_as_page_text()
    {
        var text = ExportText(DiveFixture.Trimix());
        Assert.Contains("(Trimix Dive Log)", text);
        Assert.Contains("Max depth:", text);
        Assert.Contains("Bottom time:", text);
    }

    [Fact]
    public void Parentheses_and_backslash_in_text_are_escaped()
    {
        // Fixture Notes == "Great (test) dive \ with (parens)"
        var text = ExportText(DiveFixture.Trimix());
        Assert.Contains("\\(test\\)", text); // ( ) escaped
        Assert.Contains("\\\\", text);       // \ escaped
    }
}
