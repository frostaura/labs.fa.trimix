using System.Globalization;
using Trimix.Contracts.Dtos;

namespace Trimix.Core.Connectors.Export;

/// <summary>A breathing-gas mixture as fractions in [0,1]. <see cref="N2"/> is the balance.</summary>
internal readonly record struct GasFraction(double O2, double He)
{
    public double N2 => Math.Max(0.0, Math.Round(1.0 - O2 - He, 4));
}

/// <summary>
/// Derives O2/He fractions for export from a dive's structured cylinders first, then the denormalized
/// <c>GasMix</c> display string ("Air", "EAN32", "21/35"), defaulting to air. Kept internal — export-only.
/// </summary>
internal static class GasMixParser
{
    private static readonly GasFraction Air = new(0.21, 0.0);

    /// <summary>The gases to write into a UDDF <c>gasdefinitions</c> block for a dive.</summary>
    public static IReadOnlyList<GasFraction> Gases(DiveDto dive)
    {
        if (dive.Cylinders is { Count: > 0 } cylinders)
        {
            var fromCylinders = cylinders
                .Where(c => c.O2 is not null || c.He is not null)
                .Select(FromCylinder)
                .ToList();
            if (fromCylinders.Count > 0)
            {
                return fromCylinders;
            }
        }

        return [Parse(dive.GasMix)];
    }

    /// <summary>Cylinder O2/He are percentages (0..100); a null O2 falls back to air's 21%.</summary>
    public static GasFraction FromCylinder(CylinderDto cylinder) =>
        new(
            O2: (cylinder.O2 ?? 21.0) / 100.0,
            He: (cylinder.He ?? 0.0) / 100.0);

    /// <summary>Parses a diver-native gas string. Unrecognized input falls back to air.</summary>
    public static GasFraction Parse(string? gasMix)
    {
        if (string.IsNullOrWhiteSpace(gasMix))
        {
            return Air;
        }

        var text = gasMix.Trim();

        if (text.Equals("air", StringComparison.OrdinalIgnoreCase))
        {
            return Air;
        }

        // Trimix "O2/He" (e.g. "21/35").
        var slash = text.IndexOf('/', StringComparison.Ordinal);
        if (slash > 0
            && double.TryParse(text[..slash], NumberStyles.Number, CultureInfo.InvariantCulture, out var o2Pct)
            && double.TryParse(text[(slash + 1)..], NumberStyles.Number, CultureInfo.InvariantCulture, out var hePct))
        {
            return new GasFraction(Clamp01(o2Pct / 100.0), Clamp01(hePct / 100.0));
        }

        // Nitrox "EANxx".
        if (text.StartsWith("EAN", StringComparison.OrdinalIgnoreCase)
            && double.TryParse(text.AsSpan(3), NumberStyles.Number, CultureInfo.InvariantCulture, out var eanPct))
        {
            return new GasFraction(Clamp01(eanPct / 100.0), 0.0);
        }

        // Bare percentage "32".
        if (double.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var pct))
        {
            return new GasFraction(Clamp01(pct / 100.0), 0.0);
        }

        return Air;
    }

    private static double Clamp01(double value) => Math.Clamp(value, 0.0, 1.0);
}
