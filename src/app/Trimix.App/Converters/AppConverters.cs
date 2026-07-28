using System.Globalization;
using System.Text.Json;
using Trimix.Contracts;
using Trimix.Contracts.Dtos;
using Trimix.Core.Formatting;

namespace Trimix.App.Converters;

/// <summary>Resolves Core's <see cref="UnitFormatter"/> (m/ft · °C/°F preference) for XAML converters.</summary>
internal static class Units
{
    public static UnitFormatter? Formatter =>
        IPlatformApplication.Current?.Services.GetService<UnitFormatter>();
}

/// <summary>True when the bound enum's name equals the converter parameter (e.g. ImportStep panes).</summary>
public sealed class EnumEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>True when the bound int equals the converter parameter (onboarding pane switching).</summary>
public sealed class IntEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int i && int.TryParse(parameter?.ToString(), out var expected) && i == expected;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;
}

/// <summary>Carousel dot emphasis (C6): active dot fully opaque, inactive dots dimmed.</summary>
public sealed class BoolToDotOpacityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? 1.0 : 0.3;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class IntGreaterThanZeroConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int i && i > 0;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class StringNotEmptyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        !string.IsNullOrWhiteSpace(value as string);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Entry text ⇄ nullable double (manual-entry numeric fields).</summary>
public sealed class NullableDoubleConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double d ? d.ToString(culture) : "";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        double.TryParse(value as string, NumberStyles.Float, culture, out var d)
            ? d
            : double.TryParse(value as string, NumberStyles.Float, CultureInfo.InvariantCulture, out var inv) ? inv : null;
}

/// <summary>Entry text ⇄ nullable int (manual-entry duration).</summary>
public sealed class NullableIntConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int i ? i.ToString(culture) : "";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        int.TryParse(value as string, NumberStyles.Integer, culture, out var i) ? i : null;
}

/// <summary>DatePicker.Date (non-nullable) ⇄ the VM's nullable date.</summary>
public sealed class NullableDateConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is DateTime d ? d : DateTime.Today;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is DateTime d ? d : DateTime.Today;
}

/// <summary>Metres → display depth in the preferred unit ("32.4 m" / "106.3 ft").</summary>
public sealed class DepthFormatConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Units.Formatter?.FormatDepth(value as double? ?? (value is double d ? d : null)) ?? "";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Duration seconds → "45 min".</summary>
public sealed class DurationFormatConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int seconds ? Units.Formatter?.FormatDuration(seconds) ?? "" : "";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Total bottom time seconds → compact stat ("12.4 h" once above an hour, else minutes).</summary>
public sealed class BottomTimeFormatConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not int seconds)
        {
            return "";
        }

        return seconds >= 3600
            ? string.Create(CultureInfo.InvariantCulture, $"{seconds / 3600.0:0.#} h")
            : Units.Formatter?.FormatDuration(seconds) ?? "";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>UTC timestamp → local date line ("12 Jun 2026").</summary>
public sealed class DateFormatConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is DateTime utc
            ? DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime().ToString("d MMM yyyy", culture)
            : "";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Rating 1–5 → filled/empty stars text; SemanticProperties on the label carries the words.</summary>
public sealed class RatingToStarsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int rating and >= 1 and <= 5
            ? new string('★', rating) + new string('☆', 5 - rating)
            : "";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Rating 1–5 → screen-reader words ("Rated 4 of 5").</summary>
public sealed class RatingToWordsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int rating and >= 1 and <= 5 ? $"Rated {rating} of 5" : "Not rated";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>CSV column ("gear, buddies") → items for chip BindableLayouts.</summary>
public sealed class CsvToListConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value as string)?
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList() ?? [];

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Site search row subtitle: "Reef · 12–30 m · 2.3 km away".</summary>
public sealed class SiteSubtitleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not SiteSummaryDto site)
        {
            return "";
        }

        var parts = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(site.Type))
        {
            parts.Add(site.Type);
        }

        if (DepthRange(site.DepthMinM, site.DepthMaxM) is { Length: > 0 } range)
        {
            parts.Add(range);
        }

        if (site.DistanceKm is { } km)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{km:0.#} km away"));
        }

        return string.Join(" · ", parts);
    }

    internal static string DepthRange(double? minM, double? maxM)
    {
        var units = Units.Formatter;
        return (minM, maxM) switch
        {
            (null, null) => "",
            ({ } min, null) => units?.FormatDepth(min) ?? "",
            (null, { } max) => units?.FormatDepth(max) ?? "",
            ({ } min, { } max) => units is null
                ? ""
                : $"{units.FormatDepth(min).Split(' ')[0]}–{units.FormatDepth(max)}",
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Depth range line for site detail ("12–30 m"; site as value).</summary>
public sealed class SiteDepthRangeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is SiteDetailDto site ? SiteSubtitleConverter.DepthRange(site.DepthMinM, site.DepthMaxM) : "";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Dive-card title: the draft site name when the dive carries one (manual entry's
/// <c>{"draft_site_name": …}</c> convention), else a source-based title. Site names for matched
/// sites resolve on the detail page (the list stays offline-fast).
/// </summary>
public sealed class DiveCardTitleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DiveDto dive)
        {
            return "";
        }

        if (!string.IsNullOrWhiteSpace(dive.ConditionsJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(dive.ConditionsJson);
                if (doc.RootElement.TryGetProperty("draft_site_name", out var draft)
                    && draft.GetString() is { Length: > 0 } name)
                {
                    return name;
                }
            }
            catch (JsonException)
            {
                // Not the draft-site convention — fall through.
            }
        }

        if (dive.SiteId is not null)
        {
            return "Matched dive site";
        }

        return dive.Source == DiveSources.Import ? "Imported dive" : "Logged dive";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>(done, total) → ProgressBar fraction; 0 while total is unknown.</summary>
public sealed class ProgressFractionConverter : IMultiValueConverter
{
    public object Convert(object[]? values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values is [int done, int total] && total > 0)
        {
            return Math.Clamp((double)done / total, 0, 1);
        }

        return 0d;
    }

    public object[]? ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>"32.4 m" → "32.4" (numeral) — pairs with <see cref="UnitSuffixPartConverter"/>.</summary>
public sealed class UnitPrefixPartConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = value as string ?? "";
        var i = text.LastIndexOf(' ');
        return i > 0 ? text[..i] : text;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>"32.4 m" → "m" (55%-size TextSecondary suffix per the type spec).</summary>
public sealed class UnitSuffixPartConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = value as string ?? "";
        var i = text.LastIndexOf(' ');
        return i > 0 && i < text.Length - 1 ? text[(i + 1)..] : "";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Zero-based pane index → "Pane 1 of 3" (onboarding progress for screen readers).</summary>
public sealed class PageCounterConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int index
            ? $"Pane {index + 1} of {Trimix.Core.ViewModels.OnboardingViewModel.PageCount}"
            : "";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Metres → number part in the preferred unit ("32.4"); pairs with <see cref="DepthSuffixConverter"/>.</summary>
public sealed class DepthNumberConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var formatted = Units.Formatter?.FormatDepth(value as double? ?? (value is double d ? d : null)) ?? "";
        var i = formatted.LastIndexOf(' ');
        return i > 0 ? formatted[..i] : formatted;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Metres → unit suffix ("m"/"ft") for the 55%-size suffix label.</summary>
public sealed class DepthSuffixConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var formatted = Units.Formatter?.FormatDepth(value as double? ?? (value is double d ? d : null)) ?? "";
        var i = formatted.LastIndexOf(' ');
        return i > 0 && i < formatted.Length - 1 ? formatted[(i + 1)..] : "";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Total seconds → bottom-time number part ("12.4" hours / "45" minutes).</summary>
public sealed class BottomTimeNumberConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not int seconds)
        {
            return "";
        }

        return seconds >= 3600
            ? string.Create(CultureInfo.InvariantCulture, $"{seconds / 3600.0:0.#}")
            : string.Create(CultureInfo.InvariantCulture, $"{Math.Round(seconds / 60.0):0}");
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Total seconds → bottom-time unit suffix ("h" / "min").</summary>
public sealed class BottomTimeSuffixConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int seconds ? (seconds >= 3600 ? "h" : "min") : "";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Depth-unit preference value ("m"/"ft") → the Settings toggle's display text.</summary>
public sealed class DepthUnitLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value as string == DepthUnits.Feet ? "Feet (ft)" : "Metres (m)";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Temp-unit preference value ("c"/"f") → the Settings toggle's display text.</summary>
public sealed class TempUnitLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value as string == TempUnits.Fahrenheit ? "Fahrenheit (°F)" : "Celsius (°C)";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
