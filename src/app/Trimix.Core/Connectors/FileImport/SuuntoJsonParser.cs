using System.Globalization;
using System.Text.Json;
using Trimix.Contracts.Dtos;
using Trimix.Core.Abstractions;

namespace Trimix.Core.Connectors.FileImport;

/// <summary>
/// Suunto-app JSON parser (architecture overview §2) — the richer per-dive export the Suunto app produces
/// (HR, tank/cylinder pressure, gas), a community-reported schema the research brief flags as undocumented
/// and subject to change without notice. Reads the metadata envelope (start time, max depth, dive time, gas
/// mixes) and the <c>samples</c> array (time, depth, temperature, cylinderPressure, heartRate). Depths are
/// metres, temperatures °C, pressures bar, gas fractions 0..1. Field lookups are case-insensitive and every
/// channel is optional, so a thinner export still yields a valid dive.
/// </summary>
public sealed class SuuntoJsonParser : IDiveFileParser
{
    public string FormatId => "suunto_json";

    public bool CanParse(PickedFile file)
    {
        var text = DiveFileText.AsUtf8(file).TrimStart();
        if (!text.StartsWith('{'))
        {
            return false;
        }

        // Require the sample series plus a Suunto-shaped marker so this doesn't swallow arbitrary JSON.
        return text.Contains("\"samples\"", StringComparison.OrdinalIgnoreCase)
            && (text.Contains("suunto", StringComparison.OrdinalIgnoreCase)
                || text.Contains("cylinderPressure", StringComparison.OrdinalIgnoreCase)
                || text.Contains("\"diveTime\"", StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<ImportedDive> Parse(PickedFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(DiveFileText.AsUtf8(file));
        }
        catch (JsonException ex)
        {
            throw new DiveFileParseException($"Suunto JSON is not valid JSON: {ex.Message}");
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (!TryProperty(root, "samples", out var samplesElement) || samplesElement.ValueKind != JsonValueKind.Array)
            {
                throw new DiveFileParseException("Suunto JSON has no 'samples' array.");
            }

            var samples = new List<ProfileSampleDto>();
            double? firstPressure = null;
            double? lastPressure = null;
            var index = 0;
            foreach (var sample in samplesElement.EnumerateArray())
            {
                var pressure = Number(sample, "cylinderPressure") ?? Number(sample, "tankPressure");
                firstPressure ??= pressure;
                if (pressure is not null)
                {
                    lastPressure = pressure;
                }

                samples.Add(new ProfileSampleDto(
                    TSeconds: (int)Math.Round(Number(sample, "time") ?? index),
                    DepthM: Number(sample, "depth") ?? 0,
                    TempC: Number(sample, "temperature"),
                    PpO2: Number(sample, "ppO2"),
                    Ndl: Number(sample, "ndl"),
                    CeilingM: Number(sample, "ceiling"),
                    AscentRateMPerMin: null,
                    Cns: Number(sample, "cns"),
                    TankPressureBar: pressure,
                    Setpoint: Number(sample, "setpoint"),
                    HeartRate: Number(sample, "heartRate")));
                index++;
            }

            if (samples.Count == 0)
            {
                throw new DiveFileParseException("Suunto JSON 'samples' array is empty.");
            }

            (double O2, double He)? gas = null;
            if (TryProperty(root, "gases", out var gases) && gases.ValueKind == JsonValueKind.Array)
            {
                var first = gases.EnumerateArray().FirstOrDefault();
                var o2 = Number(first, "oxygen") ?? Number(first, "o2");
                if (o2 is not null)
                {
                    gas = (AsPercent(o2.Value), AsPercent(Number(first, "helium") ?? Number(first, "he") ?? 0));
                }
            }

            var sampleTemps = samples.Where(s => s.TempC is not null).Select(s => s.TempC!.Value).ToList();

            return
            [
                new ImportedDive(
                    StartUtc: ParseDateTime(root, "startTime"),
                    DurationSec: (int)Math.Round(Number(root, "diveTime") ?? Number(root, "duration") ?? samples[^1].TSeconds),
                    MaxDepthM: Number(root, "maxDepth") ?? samples.Max(s => s.DepthM),
                    AvgDepthM: Number(root, "avgDepth") ?? Math.Round(samples.Average(s => s.DepthM), 2),
                    MinTempC: sampleTemps.Count > 0 ? sampleTemps.Min() : null,
                    GasMix: gas is null ? null : GasMixFormatter.Format(gas.Value.O2, gas.Value.He),
                    Samples: samples,
                    Cylinders: gas is null
                        ? null
                        : [new CylinderDto(O2: gas.Value.O2, He: gas.Value.He, StartBar: firstPressure, EndBar: lastPressure)]),
            ];
        }
    }

    private static double AsPercent(double value) => Math.Round(value <= 1.0 ? value * 100.0 : value, 2);

    private static bool TryProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static double? Number(JsonElement element, string name) =>
        TryProperty(element, name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)
            ? number
            : null;

    private static DateTime ParseDateTime(JsonElement root, string name) =>
        TryProperty(root, name, out var value)
        && value.ValueKind == JsonValueKind.String
        && DateTime.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : default;
}
