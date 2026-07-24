using System.Globalization;
using Trimix.Core.Abstractions;

namespace Trimix.Core.Formatting;

/// <summary>Depth unit preference values.</summary>
public static class DepthUnits
{
    public const string Meters = "m";
    public const string Feet = "ft";
}

/// <summary>Temperature unit preference values.</summary>
public static class TempUnits
{
    public const string Celsius = "c";
    public const string Fahrenheit = "f";
}

/// <summary>
/// Formats instrument readouts (depth, temperature, duration) honoring the m/ft and °C/°F unit
/// preferences (Settings). Storage stays metric everywhere — units are a display concern only.
/// </summary>
public sealed class UnitFormatter(IPreferences preferences)
{
    public const double FeetPerMeter = 3.28084;

    public string DepthUnit => preferences.Get(PreferenceKeys.DepthUnit) ?? DepthUnits.Meters;

    public string TempUnit => preferences.Get(PreferenceKeys.TempUnit) ?? TempUnits.Celsius;

    public string FormatDepth(double? meters)
    {
        if (meters is null)
        {
            return "—";
        }

        return DepthUnit == DepthUnits.Feet
            ? string.Create(CultureInfo.InvariantCulture, $"{meters.Value * FeetPerMeter:0.#} ft")
            : string.Create(CultureInfo.InvariantCulture, $"{meters.Value:0.#} m");
    }

    public string FormatTemp(double? celsius)
    {
        if (celsius is null)
        {
            return "—";
        }

        return TempUnit == TempUnits.Fahrenheit
            ? string.Create(CultureInfo.InvariantCulture, $"{celsius.Value * 9 / 5 + 32:0.#} °F")
            : string.Create(CultureInfo.InvariantCulture, $"{celsius.Value:0.#} °C");
    }

    /// <summary>Whole-dive duration, e.g. "45 min".</summary>
    public string FormatDuration(int seconds) =>
        string.Create(CultureInfo.InvariantCulture, $"{Math.Round(seconds / 60.0):0} min");

    /// <summary>Point-in-dive elapsed time for the profile table, e.g. "12:30".</summary>
    public string FormatElapsed(int seconds) =>
        string.Create(CultureInfo.InvariantCulture, $"{seconds / 60}:{seconds % 60:00}");
}
