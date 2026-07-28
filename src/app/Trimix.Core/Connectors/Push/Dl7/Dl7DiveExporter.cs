using System.Globalization;
using System.Text;
using Trimix.Contracts.Dtos;
using Trimix.Core.Abstractions;

namespace Trimix.Core.Connectors.Push;

/// <summary>
/// Exports a dive to DAN DL7 — the HL7-derived Divelog format and the only ingest format PADI/DAN ever
/// accepted. Structure: an <c>FSH</c> file header (declaring the HL7 encoding chars <c>^~\&amp;</c>), a
/// <c>ZAR</c> application/diver record, then per dive a <c>ZDH</c> header, one <c>ZDP</c> profile record
/// per sample, and a <c>ZDT</c> trailer. Fields are <c>|</c>-delimited; segments end in CRLF. Metric
/// units (depth m, temp °C). This is a faithful DL7 subset — optional agency-specific fields are omitted.
/// </summary>
public sealed class Dl7DiveExporter : IDiveExporter
{
    private const string FieldSep = "|";
    private const string Encoding = "^~\\&";
    private const string SegmentEnd = "\r\n";

    public DiveExportFormat Format => DiveExportFormat.Dl7;

    public string ContentType => "application/octet-stream";

    public string FileExtension => "dl7";

    public ProfileFidelity Fidelity => ProfileFidelity.FullProfile;

    public ExportedDive Export(DiveDto dive)
    {
        ArgumentNullException.ThrowIfNull(dive);

        var sb = new StringBuilder();

        // FSH — File Segment Header. The encoding-characters field (^~\&) is emitted RAW (it declares the
        // HL7 delimiters and must not be sanitized); remaining fields are sending app | facility | datetime
        // | (security) | file type.
        sb.Append("FSH").Append(FieldSep).Append(Encoding);
        AppendFields(sb, "Trimix", "FrostAura", Stamp(dive.StartUtc), string.Empty, "DL7");
        sb.Append(SegmentEnd);

        // ZAR — Application/diver record (record id | app name | app version).
        Segment(sb, "ZAR", "1", "Trimix", "1.0");

        // ZDH — Dive Header: dive# | record type | start datetime | gas mix | max depth | duration(min).
        Segment(
            sb,
            "ZDH",
            "1",
            "1",
            Stamp(dive.StartUtc),
            dive.GasMix ?? "Air",
            Num(dive.MaxDepthM),
            Int(DurationMinutes(dive.DurationSec)));

        // ZDP — one profile record per sample: elapsed(min) | depth(m) | temp(°C).
        foreach (var s in dive.ProfileSamples)
        {
            Segment(
                sb,
                "ZDP",
                Num(s.TSeconds / 60.0),
                Num(s.DepthM),
                s.TempC is { } t ? Num(t) : string.Empty);
        }

        // ZDT — Dive Trailer: max depth | duration(min) | min temp(°C) | avg depth(m).
        Segment(
            sb,
            "ZDT",
            Num(dive.MaxDepthM),
            Int(DurationMinutes(dive.DurationSec)),
            dive.MinTempC is { } min ? Num(min) : string.Empty,
            dive.AvgDepthM is { } avg ? Num(avg) : string.Empty);

        var bytes = System.Text.Encoding.ASCII.GetBytes(sb.ToString());
        var fileName = ExportNaming.FileName(dive, FileExtension);
        return new ExportedDive(Format, fileName, ContentType, bytes);
    }

    private static void Segment(StringBuilder sb, string id, params string[] fields)
    {
        sb.Append(id);
        AppendFields(sb, fields);
        sb.Append(SegmentEnd);
    }

    /// <summary>Appends <c>|</c>-prefixed, delimiter-sanitized fields (no trailing segment terminator).</summary>
    private static void AppendFields(StringBuilder sb, params string[] fields)
    {
        foreach (var field in fields)
        {
            sb.Append(FieldSep).Append(Sanitize(field));
        }
    }

    /// <summary>Strips the HL7 delimiter characters so free text can't corrupt the segment grammar.</summary>
    private static string Sanitize(string value) =>
        value.Replace('|', ' ').Replace('^', ' ').Replace('~', ' ').Replace('\r', ' ').Replace('\n', ' ');

    private static int DurationMinutes(int durationSec) =>
        (int)Math.Round(durationSec / 60.0, MidpointRounding.AwayFromZero);

    private static string Num(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Stamp(DateTime utc) =>
        utc.ToUniversalTime().ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
}
