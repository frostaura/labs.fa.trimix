using System.Globalization;
using System.Text;
using Trimix.Contracts.Dtos;
using Trimix.Core.Abstractions;

namespace Trimix.Core.Connectors.Export;

/// <summary>
/// Renders a single-page, human-readable PDF dive summary with <em>zero</em> dependencies — a hand-rolled
/// PDF 1.4 document (Catalog → Pages → Page → Helvetica font → one content stream) with a byte-accurate
/// cross-reference table, so the output opens in any reader. Summary fidelity (headline stats, not the
/// profile series). Content is Latin-1 so every char is one byte and the xref offsets stay exact.
/// </summary>
public sealed class MinimalPdfDiveExporter : IDiveExporter
{
    public DiveExportFormat Format => DiveExportFormat.Pdf;

    public string ContentType => "application/pdf";

    public string FileExtension => "pdf";

    public ProfileFidelity Fidelity => ProfileFidelity.SummaryOnly;

    public ExportedDive Export(DiveDto dive)
    {
        ArgumentNullException.ThrowIfNull(dive);

        var content = BuildContentStream(dive);
        var bytes = Assemble(content);
        var fileName = ExportNaming.FileName(dive, FileExtension);
        return new ExportedDive(Format, fileName, ContentType, bytes);
    }

    private static string BuildContentStream(DiveDto dive)
    {
        var lines = new List<(int Size, string Text)>
        {
            (20, "Trimix Dive Log"),
            (11, "Date: " + dive.StartUtc.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)),
            (11, "Max depth: " + Num(dive.MaxDepthM) + " m"),
            (11, "Bottom time: " + Duration(dive.DurationSec)),
        };

        if (dive.AvgDepthM is { } avg)
        {
            lines.Add((11, "Avg depth: " + Num(avg) + " m"));
        }

        if (dive.MinTempC is { } min)
        {
            lines.Add((11, "Min temp: " + Num(min) + " C"));
        }

        lines.Add((11, "Gas: " + (string.IsNullOrWhiteSpace(dive.GasMix) ? "Air" : dive.GasMix!)));

        if (dive.Rating is { } rating)
        {
            lines.Add((11, "Rating: " + rating.ToString(CultureInfo.InvariantCulture) + "/5"));
        }

        if (!string.IsNullOrWhiteSpace(dive.BuddiesCsv))
        {
            lines.Add((11, "Buddies: " + dive.BuddiesCsv!));
        }

        if (!string.IsNullOrWhiteSpace(dive.Notes))
        {
            lines.Add((11, "Notes: " + Truncate(dive.Notes!, 90)));
        }

        var sb = new StringBuilder();
        sb.Append("BT\n");
        var y = 740;
        var first = true;
        foreach (var (size, text) in lines)
        {
            // First line positions the text cursor absolutely; later lines step down with TL/T*.
            if (first)
            {
                sb.Append(CultureInfo.InvariantCulture, $"/F1 {size} Tf\n");
                sb.Append("28 TL\n");
                sb.Append(CultureInfo.InvariantCulture, $"72 {y} Td\n");
                sb.Append('(').Append(EscapePdfText(text)).Append(") Tj\n");
                first = false;
            }
            else
            {
                sb.Append(CultureInfo.InvariantCulture, $"/F1 {size} Tf\n");
                sb.Append("T*\n");
                sb.Append('(').Append(EscapePdfText(text)).Append(") Tj\n");
            }
        }

        sb.Append("ET");
        return sb.ToString();
    }

    private static byte[] Assemble(string contentStream)
    {
        var latin1 = Encoding.Latin1;
        using var stream = new MemoryStream();
        var offsets = new long[6]; // index 0 unused (free object).

        void Write(string text) => stream.Write(latin1.GetBytes(text));

        // Header (binary comment marks the file as containing binary data).
        Write("%PDF-1.4\n");
        stream.Write([(byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n']);

        offsets[1] = stream.Position;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        offsets[2] = stream.Position;
        Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        offsets[3] = stream.Position;
        Write("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] " +
              "/Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>\nendobj\n");

        offsets[4] = stream.Position;
        Write("4 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n");

        offsets[5] = stream.Position;
        var contentBytes = latin1.GetBytes(contentStream);
        Write(string.Create(CultureInfo.InvariantCulture, $"5 0 obj\n<< /Length {contentBytes.Length} >>\nstream\n"));
        stream.Write(contentBytes);
        Write("\nendstream\nendobj\n");

        var xrefOffset = stream.Position;
        Write("xref\n0 6\n");
        Write("0000000000 65535 f \n");
        for (var i = 1; i <= 5; i++)
        {
            Write(string.Create(CultureInfo.InvariantCulture, $"{offsets[i]:D10} 00000 n \n"));
        }

        Write("trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n");
        Write(xrefOffset.ToString(CultureInfo.InvariantCulture));
        Write("\n%%EOF\n");

        return stream.ToArray();
    }

    private static string EscapePdfText(string text)
    {
        var sb = new StringBuilder(text.Length + 8);
        foreach (var ch in text)
        {
            switch (ch)
            {
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '(':
                    sb.Append("\\(");
                    break;
                case ')':
                    sb.Append("\\)");
                    break;
                default:
                    // Latin-1 only; drop anything a single byte can't represent.
                    sb.Append(ch <= 'ÿ' ? ch : '?');
                    break;
            }
        }

        return sb.ToString();
    }

    private static string Truncate(string text, int max)
    {
        var flattened = text.Replace('\r', ' ').Replace('\n', ' ');
        return flattened.Length <= max ? flattened : flattened[..max] + "...";
    }

    private static string Num(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Duration(int seconds) =>
        string.Create(CultureInfo.InvariantCulture, $"{Math.Round(seconds / 60.0):0} min");
}
