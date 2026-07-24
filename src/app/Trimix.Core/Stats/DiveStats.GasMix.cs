using System.Globalization;
using System.Text.RegularExpressions;
using Trimix.Contracts.Dtos;

namespace Trimix.Core.Stats;

public static partial class DiveStats
{
    /// <summary>Breathing-gas class for the <see cref="GasMixBreakdown"/> tally.</summary>
    private enum GasClass
    {
        Unknown,
        Air,
        Nitrox,
        Trimix,
    }

    // "21/35" O2/He notation → trimix.
    private static readonly Regex SlashMix = new(@"\d+\s*/\s*\d+", RegexOptions.CultureInvariant);

    // A bare / suffixed oxygen percentage (e.g. "ean32", "32%", "32").
    private static readonly Regex PercentMix = new(@"(\d+(?:\.\d+)?)\s*%?", RegexOptions.CultureInvariant);

    /// <summary>Classify a dive's gas: structured cylinders (O2/He) first, denormalized <c>GasMix</c> string fallback.</summary>
    private static GasClass Classify(DiveDto dive)
    {
        return ClassifyFromCylinders(dive.Cylinders) ?? ClassifyFromString(dive.GasMix);
    }

    private static GasClass? ClassifyFromCylinders(IReadOnlyList<CylinderDto>? cylinders)
    {
        if (cylinders is null || cylinders.Count == 0)
        {
            return null;
        }

        // Any helium content makes it trimix regardless of the O2 fraction.
        if (cylinders.Any(c => c.He is > 0))
        {
            return GasClass.Trimix;
        }

        var oxygen = cylinders.Where(c => c.O2 is not null).Select(c => c.O2!.Value).ToList();
        if (oxygen.Count == 0)
        {
            // Only helium fields were present and all null/zero — inconclusive; defer to the string.
            return null;
        }

        var maxO2 = oxygen.Max();
        if (maxO2 > 22.0)
        {
            return GasClass.Nitrox;
        }

        // ~21% O2 with no helium is air; a sub-air O2 fraction with no helium is a non-air mix.
        return maxO2 >= 20.0 ? GasClass.Air : GasClass.Nitrox;
    }

    private static GasClass ClassifyFromString(string? gasMix)
    {
        if (string.IsNullOrWhiteSpace(gasMix))
        {
            return GasClass.Unknown;
        }

        var g = gasMix.Trim().ToLowerInvariant();
        if (g.Contains("trimix") || g.Contains("tx") || g.Contains("heliox") || SlashMix.IsMatch(g))
        {
            return GasClass.Trimix;
        }

        if (g.Contains("air"))
        {
            return GasClass.Air;
        }

        if (g.Contains("nitrox") || g.Contains("ean") || g.Contains("nx"))
        {
            return GasClass.Nitrox;
        }

        var match = PercentMix.Match(g);
        if (match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var pct))
        {
            if (pct > 22.0)
            {
                return GasClass.Nitrox;
            }

            if (pct >= 20.0)
            {
                return GasClass.Air;
            }
        }

        return GasClass.Unknown;
    }
}
