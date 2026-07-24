using System.Globalization;
using Trimix.Contracts.Dtos;
using Trimix.Core.Abstractions;

namespace Trimix.Core.Connectors.FileImport;

/// <summary>
/// DAN DL7 (Diver's Alert Network Data Language, text) parser (architecture overview §2) — the format DAN's
/// research logbook and several desktop tools exchange. DL7 is HL7-flavoured: <c>|</c>-delimited segments,
/// one per line. This parser targets the dive-carrying segments and tolerates DL7's real-world variance by
/// reading only the fields a dive log needs:
/// <list type="bullet">
/// <item><c>ZDH</c> dive header: <c>ZDH|seq|diveNumber|mode|startDateTime(yyyyMMddHHmmss)|gasName</c>.</item>
/// <item>a profile block delimited by a <c>ZDP{</c> line … <c>}ZDP</c> line, whose rows are tab/whitespace
/// -separated <c>elapsedSeconds  depthMetres  temperatureC</c> waypoints.</item>
/// <item><c>ZDT</c> dive trailer: <c>ZDT|maxDepthMetres|bottomTimeSeconds|minTempC</c>.</item>
/// </list>
/// Metric units are assumed (DL7 can carry imperial; that flag is out of the fixture's scope and noted for
/// later hardening). Each <c>ZDH</c> starts a new dive, so multi-dive files parse.
/// </summary>
public sealed class DanDl7Parser : IDiveFileParser
{
    public string FormatId => "dan_dl7";

    public bool CanParse(PickedFile file)
    {
        if (DiveFileText.HasExtension(file, ".dl7", ".zxu", ".zxl"))
        {
            return true;
        }

        var text = DiveFileText.AsUtf8(file);
        return text.Contains("ZDP", StringComparison.Ordinal)
            && (text.Contains("ZDH", StringComparison.Ordinal) || text.Contains("FSH|", StringComparison.Ordinal));
    }

    public IReadOnlyList<ImportedDive> Parse(PickedFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var lines = DiveFileText.AsUtf8(file).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        var dives = new List<ImportedDive>();

        DateTime start = default;
        (double O2, double He)? gas = null;
        List<ProfileSampleDto>? samples = null;
        double? trailerMaxDepth = null;
        int? trailerBottomTime = null;
        double? trailerMinTemp = null;
        var inProfile = false;

        void Flush()
        {
            if (samples is null || samples.Count == 0)
            {
                return;
            }

            var sampleTemps = samples.Where(s => s.TempC is not null).Select(s => s.TempC!.Value).ToList();
            dives.Add(new ImportedDive(
                StartUtc: start,
                DurationSec: trailerBottomTime ?? samples[^1].TSeconds,
                MaxDepthM: trailerMaxDepth ?? samples.Max(s => s.DepthM),
                AvgDepthM: Math.Round(samples.Average(s => s.DepthM), 2),
                MinTempC: trailerMinTemp ?? (sampleTemps.Count > 0 ? sampleTemps.Min() : null),
                GasMix: gas is null ? null : GasMixFormatter.Format(gas.Value.O2, gas.Value.He),
                Samples: samples,
                Cylinders: gas is null ? null : [new CylinderDto(O2: gas.Value.O2, He: gas.Value.He)]));

            samples = null;
            gas = null;
            trailerMaxDepth = null;
            trailerBottomTime = null;
            trailerMinTemp = null;
        }

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("ZDH", StringComparison.Ordinal))
            {
                Flush(); // a new dive header closes the previous dive
                inProfile = false; // …and ends any profile block left unclosed by a missing '}ZDP'
                var fields = line.Split('|');
                start = ParseDl7DateTime(Field(fields, 4));
                gas = ParseGas(Field(fields, 5));
                samples = [];
                continue;
            }

            if (line.StartsWith("ZDP", StringComparison.Ordinal) && line.Contains('{'))
            {
                inProfile = true;
                samples ??= [];
                continue;
            }

            if (line.Contains('}') && line.Contains("ZDP", StringComparison.Ordinal))
            {
                inProfile = false;
                continue;
            }

            // Segment records (ZDT trailer, FSH file header) are checked BEFORE the profile-row branch and
            // exit the profile — so a DL7 file that omits the '}ZDP' end-marker (real-world variance) does not
            // route its ZDT trailer into the numeric-row parser, which would drop max-depth / bottom-time / min-temp.
            if (line.StartsWith("ZDT", StringComparison.Ordinal))
            {
                inProfile = false;
                var fields = line.Split('|');
                trailerMaxDepth = ParseDouble(Field(fields, 1));
                var bottom = ParseDouble(Field(fields, 2));
                trailerBottomTime = bottom is null ? null : (int)Math.Round(bottom.Value);
                trailerMinTemp = ParseDouble(Field(fields, 3));
                continue;
            }

            if (line.StartsWith("FSH", StringComparison.Ordinal))
            {
                inProfile = false; // a file-header segment likewise ends any unclosed profile block
                continue;
            }

            if (inProfile)
            {
                var columns = line.Split(['\t', ' '], StringSplitOptions.RemoveEmptyEntries);
                if (columns.Length < 2)
                {
                    continue;
                }

                samples ??= [];
                samples.Add(new ProfileSampleDto(
                    TSeconds: (int)Math.Round(ParseDouble(columns[0]) ?? samples.Count),
                    DepthM: ParseDouble(columns[1]) ?? 0,
                    TempC: columns.Length > 2 ? ParseDouble(columns[2]) : null,
                    PpO2: null));
                continue;
            }
        }

        Flush();

        if (dives.Count == 0)
        {
            throw new DiveFileParseException("DL7 file contains no dive profile (no ZDH / ZDP block found).");
        }

        return dives;
    }

    private static string? Field(string[] fields, int index) =>
        index < fields.Length ? fields[index].Trim() : null;

    private static (double O2, double He)? ParseGas(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var token = name.Trim().ToUpperInvariant();
        if (token is "AIR")
        {
            return (21, 0);
        }

        if (token.StartsWith("EAN", StringComparison.Ordinal)
            && double.TryParse(token[3..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var o2))
        {
            return (o2, 0);
        }

        if (token.StartsWith("TMX", StringComparison.Ordinal))
        {
            token = token[3..];
        }

        var parts = token.Split('/');
        if (parts.Length == 2
            && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var tmxO2)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var tmxHe))
        {
            return (tmxO2, tmxHe);
        }

        return null;
    }

    private static DateTime ParseDl7DateTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return default;
        }

        var formats = new[] { "yyyyMMddHHmmss", "yyyyMMddHHmm", "yyyyMMdd" };
        return DateTime.TryParseExact(
            value.Trim(), formats, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : default;
    }

    private static double? ParseDouble(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
}
