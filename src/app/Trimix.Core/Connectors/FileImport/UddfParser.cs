using System.Globalization;
using System.Xml.Linq;
using Trimix.Contracts.Dtos;
using Trimix.Core.Abstractions;

namespace Trimix.Core.Connectors.FileImport;

/// <summary>
/// UDDF (Universal Dive Data Format, XML) parser (architecture overview §2) — the open, vendor-neutral
/// interchange format Subsurface/MacDive export, and a universal fallback for every closed vendor. Walks
/// <c>profiledata/repetitiongroup/dive</c>, reading each <c>samples/waypoint</c> (depth, divetime,
/// temperature, tankpressure, gas switch) and the per-dive gas <c>mix</c> definitions. UDDF is strict SI:
/// depth in metres, divetime in seconds, temperature in <b>Kelvin</b>, pressure in <b>Pascal</b>, gas
/// fractions 0..1 — all converted to the app's units (°C, bar, %). Namespace-agnostic (matches by local name)
/// so it tolerates UDDF 3.x documents with or without a default namespace.
/// </summary>
public sealed class UddfParser : IDiveFileParser
{
    public string FormatId => "uddf";

    public bool CanParse(PickedFile file) =>
        DiveFileText.HasExtension(file, ".uddf")
        || DiveFileText.AsUtf8(file).Contains("<uddf", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<ImportedDive> Parse(PickedFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        XDocument doc;
        try
        {
            doc = XDocument.Parse(DiveFileText.AsUtf8(file));
        }
        catch (System.Xml.XmlException ex)
        {
            throw new DiveFileParseException($"UDDF file is not well-formed XML: {ex.Message}");
        }

        var root = doc.Root ?? throw new DiveFileParseException("UDDF file has no root element.");

        // gasdefinitions/mix id → (O2%, He%).
        var gasById = new Dictionary<string, (double O2, double He)>(StringComparer.OrdinalIgnoreCase);
        foreach (var mix in Descendants(root, "mix"))
        {
            var id = mix.Attribute("id")?.Value;
            if (id is null)
            {
                continue;
            }

            var o2 = ParseDouble(Child(mix, "o2"));
            var he = ParseDouble(Child(mix, "he"));
            if (o2 is not null)
            {
                gasById[id] = (AsPercent(o2.Value), AsPercent(he ?? 0));
            }
        }

        var dives = new List<ImportedDive>();
        foreach (var dive in Descendants(root, "dive"))
        {
            var parsed = ParseDive(dive, gasById);
            if (parsed is not null)
            {
                dives.Add(parsed);
            }
        }

        if (dives.Count == 0)
        {
            throw new DiveFileParseException("UDDF file contains no dives with sample data.");
        }

        return dives;
    }

    private static ImportedDive? ParseDive(XElement dive, IReadOnlyDictionary<string, (double O2, double He)> gasById)
    {
        var waypoints = Descendants(dive, "waypoint").ToList();
        if (waypoints.Count == 0)
        {
            return null;
        }

        // Gas can be declared once on the dive or switched at a waypoint; track the running mix.
        (double O2, double He)? currentGas = ResolveMix(Child(dive, "switchmix")?.Attribute("ref")?.Value, gasById);
        var gasesUsed = new List<(double O2, double He)>();
        if (currentGas is not null)
        {
            gasesUsed.Add(currentGas.Value);
        }

        var samples = new List<ProfileSampleDto>(waypoints.Count);
        double? firstPressure = null;
        double? lastPressure = null;
        foreach (var waypoint in waypoints)
        {
            var switched = ResolveMix(Child(waypoint, "switchmix")?.Attribute("ref")?.Value, gasById);
            if (switched is not null)
            {
                currentGas = switched;
                gasesUsed.Add(switched.Value);
            }

            var tankPa = ParseDouble(Child(waypoint, "tankpressure"));
            double? tankBar = tankPa is null ? null : tankPa.Value / 100_000.0; // Pa → bar
            firstPressure ??= tankBar;
            if (tankBar is not null)
            {
                lastPressure = tankBar;
            }

            var kelvin = ParseDouble(Child(waypoint, "temperature"));
            samples.Add(new ProfileSampleDto(
                TSeconds: (int)Math.Round(ParseDouble(Child(waypoint, "divetime")) ?? 0),
                DepthM: ParseDouble(Child(waypoint, "depth")) ?? 0,
                TempC: kelvin is null ? null : Math.Round(kelvin.Value - 273.15, 2),
                PpO2: PascalToBar(ParseDouble(Child(waypoint, "po2"))),
                Ndl: null,
                CeilingM: null,
                AscentRateMPerMin: null,
                Cns: null,
                TankPressureBar: tankBar,
                Setpoint: PascalToBar(ParseDouble(Child(waypoint, "setpo2"))),
                HeartRate: ParseDouble(Child(waypoint, "heartrate"))));
        }

        var after = Child(dive, "informationafterdive");
        double maxDepth = ParseDouble(Child(after, "greatestdepth")) ?? samples.Max(s => s.DepthM);
        int durationSec = (int)Math.Round(ParseDouble(Child(after, "diveduration"))
            ?? samples[^1].TSeconds);
        double? afterMinTemp = ParseDouble(Child(after, "lowesttemperature"));
        var sampleTemps = samples.Where(s => s.TempC is not null).Select(s => s.TempC!.Value).ToList();
        double? minTemp = afterMinTemp is not null
            ? Math.Round(afterMinTemp.Value - 273.15, 2)
            : sampleTemps.Count > 0 ? sampleTemps.Min() : null;

        var start = ParseDateTime(Child(Child(dive, "informationbeforedive"), "datetime"));

        var gas = gasesUsed.Count > 0 ? gasesUsed[0] : ((double O2, double He)?)null;
        // gasesUsed is an append log of every <switchmix> occurrence — some exporters stamp the mix on EVERY
        // waypoint, and an A→B→A switch repeats a mix — so materialize cylinders from the DISTINCT gases
        // (first-seen order preserved). Otherwise one physical cylinder becomes many duplicate CylinderDto rows.
        var distinctGases = gasesUsed.Distinct().ToList();
        IReadOnlyList<CylinderDto>? cylinders = distinctGases.Count == 0
            ? null
            : [.. distinctGases.Select((g, i) => new CylinderDto(
                O2: g.O2,
                He: g.He,
                StartBar: i == 0 ? firstPressure : null,
                EndBar: i == 0 ? lastPressure : null))];

        return new ImportedDive(
            StartUtc: start,
            DurationSec: durationSec,
            MaxDepthM: maxDepth,
            AvgDepthM: Math.Round(samples.Average(s => s.DepthM), 2),
            MinTempC: minTemp,
            GasMix: gas is null ? null : GasMixFormatter.Format(gas.Value.O2, gas.Value.He),
            Samples: samples,
            Cylinders: cylinders);
    }

    private static (double O2, double He)? ResolveMix(string? reference, IReadOnlyDictionary<string, (double O2, double He)> gasById) =>
        reference is not null && gasById.TryGetValue(reference, out var gas) ? gas : null;

    private static double AsPercent(double fraction) => Math.Round(fraction <= 1.0 ? fraction * 100.0 : fraction, 2);

    private static double? PascalToBar(double? pascal) => pascal is null ? null : pascal.Value / 100_000.0;

    private static IEnumerable<XElement> Descendants(XElement? parent, string localName) =>
        parent?.Descendants().Where(e => e.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase))
        ?? [];

    private static XElement? Child(XElement? parent, string localName) =>
        parent?.Elements().FirstOrDefault(e => e.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase));

    private static double? ParseDouble(XElement? element) =>
        element is not null && double.TryParse(element.Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static DateTime ParseDateTime(XElement? element) =>
        element is not null && DateTime.TryParse(
            element.Value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var value)
            ? value
            : default;
}
