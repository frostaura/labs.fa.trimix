using System.Text;

namespace Trimix.Core.Connectors.FileImport;

/// <summary>Shared helpers for the text-based file parsers (UDDF / DL7 / Suunto-JSON).</summary>
internal static class DiveFileText
{
    /// <summary>Decodes the file as UTF-8, stripping a leading BOM. Never throws (invalid bytes → replacement char).</summary>
    public static string AsUtf8(PickedFile file) =>
        Encoding.UTF8.GetString(file.Contents).TrimStart('﻿');

    public static bool HasExtension(PickedFile file, params string[] extensions) =>
        extensions.Any(ext => file.FileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Diver-native gas naming, identical to <c>ShearwaterProtocol.FormatGasMix</c> so every source labels
/// gases the same way: trimix as <c>"O2/He"</c>, nitrox as <c>"EANxx"</c>, air (21/0) as <c>"Air"</c>.
/// </summary>
internal static class GasMixFormatter
{
    public static string? Format(double? o2Percent, double? hePercent)
    {
        if (o2Percent is null)
        {
            return null;
        }

        var o2 = (int)Math.Round(o2Percent.Value);
        var he = (int)Math.Round(hePercent ?? 0);
        return he > 0 ? $"{o2}/{he}" : o2 == 21 ? "Air" : $"EAN{o2}";
    }
}
