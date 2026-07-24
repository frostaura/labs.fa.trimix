using System.Globalization;
using Trimix.Contracts.Dtos;

namespace Trimix.Core.Connectors.Export;

/// <summary>Deterministic, filesystem-safe file names for exported dives.</summary>
internal static class ExportNaming
{
    public static string FileName(DiveDto dive, string extension) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"trimix-dive-{dive.StartUtc.ToUniversalTime():yyyyMMdd-HHmmss}.{extension}");
}
