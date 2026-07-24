using Trimix.Contracts.Dtos;
using Trimix.Core.Abstractions;

namespace Trimix.Core.Connectors.FileImport;

/// <summary>
/// Garmin Descent <c>.FIT</c> parser (architecture overview §2) — the only clean Garmin path, since Garmin
/// deliberately blocks third-party BLE dive download and its cloud API excludes diving. Reconstructs the dive
/// from the FIT <c>record</c> (msg 20: depth/temp/NDL/ceiling/CNS per ~1 s sample), <c>dive_gas</c> (msg 259:
/// O2/He per cylinder) and <c>dive_summary</c> (msg 268: avg/max depth, bottom time) messages that
/// <see cref="FitReader"/> surfaces. A Garmin export is one dive per file; multiple <c>dive_gas</c> messages
/// become multiple <see cref="CylinderDto"/>s (multi-gas / trimix). Imported NDL/ceiling/CNS are displayed
/// data, never recomputed (the deco liability line stands).
/// </summary>
public sealed class GarminFitParser : IDiveFileParser
{
    // FIT epoch is 1989-12-31T00:00:00Z; add this to a FIT timestamp to get Unix seconds.
    private const long FitEpochUnixSeconds = 631_065_600L;

    private const int MsgRecord = 20;
    private const int MsgDiveGas = 259;
    private const int MsgDiveSummary = 268;

    // record (msg 20) dive field numbers, from the public FIT Profile.
    private const int RecTemperature = 13;   // sint8, °C
    private const int RecDepth = 91;         // uint32, scale 1000 → m
    private const int RecNextStopDepth = 92; // uint32, scale 1000 → m (ceiling)
    private const int RecNdlTime = 95;       // uint32, s
    private const int RecCnsLoad = 96;       // uint8, %

    // dive_gas (msg 259).
    private const int GasHelium = 0;  // uint8, %
    private const int GasOxygen = 1;  // uint8, %
    private const int GasStatus = 2;  // enum: 0 disabled, 1 enabled, 2 backup

    // dive_summary (msg 268).
    private const int SumAvgDepth = 2;    // uint32, scale 1000 → m
    private const int SumMaxDepth = 3;    // uint32, scale 1000 → m
    private const int SumBottomTime = 11; // uint32, scale 1000 → s

    public string FormatId => "garmin_fit";

    public bool CanParse(PickedFile file)
    {
        if (DiveFileText.HasExtension(file, ".fit"))
        {
            return true;
        }

        var bytes = file.Contents;
        return bytes.Length >= 12 && bytes[8] == 0x2E && bytes[9] == 0x46 && bytes[10] == 0x49 && bytes[11] == 0x54;
    }

    public IReadOnlyList<ImportedDive> Parse(PickedFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var messages = FitReader.Read(file.Contents);

        var records = messages.Where(m => m.GlobalMessageNumber == MsgRecord && m.Timestamp is not null).ToList();
        if (records.Count == 0)
        {
            throw new DiveFileParseException("FIT file contains no dive record messages (not a dive, or a non-dive activity).");
        }

        var cylinders = messages
            .Where(m => m.GlobalMessageNumber == MsgDiveGas
                && m.Fields.ContainsKey(GasOxygen)
                && (!m.Fields.TryGetValue(GasStatus, out var status) || status != 0)) // drop disabled slots
            .Select(m => new CylinderDto(
                O2: m.Fields[GasOxygen],
                He: m.Fields.TryGetValue(GasHelium, out var he) ? he : 0))
            .ToList();

        var summary = messages.FirstOrDefault(m => m.GlobalMessageNumber == MsgDiveSummary);

        long startTs = records[0].Timestamp!.Value;
        var samples = new List<ProfileSampleDto>(records.Count);
        foreach (var record in records)
        {
            var fields = record.Fields;
            samples.Add(new ProfileSampleDto(
                TSeconds: (int)(record.Timestamp!.Value - startTs),
                DepthM: fields.TryGetValue(RecDepth, out var depth) ? depth / 1000.0 : 0,
                TempC: fields.TryGetValue(RecTemperature, out var temp) ? temp : null,
                PpO2: null,
                Ndl: fields.TryGetValue(RecNdlTime, out var ndl) ? ndl / 60.0 : null, // seconds → minutes
                CeilingM: fields.TryGetValue(RecNextStopDepth, out var ceiling) ? ceiling / 1000.0 : null,
                AscentRateMPerMin: null,
                Cns: fields.TryGetValue(RecCnsLoad, out var cns) ? cns : null,
                TankPressureBar: null,
                Setpoint: null,
                HeartRate: null));
        }

        double maxDepth = Scaled(summary, SumMaxDepth, 1000.0) ?? samples.Max(s => s.DepthM);
        double avgDepth = Scaled(summary, SumAvgDepth, 1000.0) ?? Math.Round(samples.Average(s => s.DepthM), 2);
        double? bottomTime = Scaled(summary, SumBottomTime, 1000.0);
        int durationSec = bottomTime is not null
            ? (int)Math.Round(bottomTime.Value)
            : (int)(records[^1].Timestamp!.Value - startTs);

        var temps = samples.Where(s => s.TempC is not null).Select(s => s.TempC!.Value).ToList();

        return
        [
            new ImportedDive(
                StartUtc: DateTimeOffset.FromUnixTimeSeconds(FitEpochUnixSeconds + startTs).UtcDateTime,
                DurationSec: durationSec,
                MaxDepthM: maxDepth,
                AvgDepthM: avgDepth,
                MinTempC: temps.Count > 0 ? temps.Min() : null,
                GasMix: cylinders.Count > 0 ? GasMixFormatter.Format(cylinders[0].O2, cylinders[0].He) : null,
                Samples: samples,
                Cylinders: cylinders.Count > 0 ? cylinders : null),
        ];
    }

    /// <summary>Reads a dive_summary field and applies its FIT scale, or null when the summary/field is absent.</summary>
    private static double? Scaled(FitReader.FitMessage? summary, int field, double scale) =>
        summary is not null && summary.Fields.TryGetValue(field, out var value) ? value / scale : null;
}
