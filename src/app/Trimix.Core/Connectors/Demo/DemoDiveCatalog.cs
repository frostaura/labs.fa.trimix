using Trimix.Contracts.Dtos;
using Trimix.Core.Abstractions;

namespace Trimix.Core.Connectors.Demo;

/// <summary>
/// The seeded dives the DEBUG-only demo source connectors emit. Everything is deterministic — fixed
/// start instants, durations and profiles — so <c>ImportService.DeterministicDiveId</c> (vendor +
/// start + duration) yields stable ids and re-syncs upsert instead of duplicating. Nine dives across
/// three sources deliberately exceed the free cap (5), so a demo sync exercises the REAL funnel:
/// import → truncation → <c>paywall_shown(cloud_sync_cap)</c> → purchase → re-sync imports the rest.
/// </summary>
public static class DemoDiveCatalog
{
    public const string GarminId = "demo_garmin";
    public const string SuuntoId = "demo_suunto";
    public const string MaresId = "demo_mares";
    public const string PadiId = "demo_padi";
    public const string SsiId = "demo_ssi";

    /// <summary>Seeded dives for a demo source connector id (newest last; empty for unknown ids).</summary>
    public static IReadOnlyList<ImportedDive> DivesFor(string connectorId) => connectorId switch
    {
        GarminId =>
        [
            Dive(new DateTime(2026, 6, 6, 8, 12, 0, DateTimeKind.Utc), 46 * 60, 18.4, 24.5, "EAN32"),
            Dive(new DateTime(2026, 6, 6, 11, 40, 0, DateTimeKind.Utc), 41 * 60, 14.2, 25.0, "EAN32"),
            Dive(new DateTime(2026, 6, 20, 9, 5, 0, DateTimeKind.Utc), 52 * 60, 27.8, 21.0, "Air"),
            Dive(new DateTime(2026, 7, 11, 7, 58, 0, DateTimeKind.Utc), 44 * 60, 22.6, 22.5, "EAN36"),
        ],
        SuuntoId =>
        [
            Dive(new DateTime(2026, 5, 17, 10, 20, 0, DateTimeKind.Utc), 38 * 60, 12.8, 16.0, "Air"),
            Dive(new DateTime(2026, 6, 27, 13, 15, 0, DateTimeKind.Utc), 57 * 60, 31.5, 14.5, "EAN28"),
            Dive(new DateTime(2026, 7, 18, 8, 42, 0, DateTimeKind.Utc), 49 * 60, 19.7, 18.0, "EAN32"),
        ],
        MaresId =>
        [
            Dive(new DateTime(2026, 5, 30, 9, 33, 0, DateTimeKind.Utc), 61 * 60, 16.9, 26.5, "Air"),
            Dive(new DateTime(2026, 7, 4, 15, 8, 0, DateTimeKind.Utc), 35 * 60, 24.3, 8.5, "Air"),
        ],
        _ => [],
    };

    /// <summary>One seeded dive with a simple descend → bottom → ascend profile (30s samples).</summary>
    private static ImportedDive Dive(DateTime startUtc, int durationSec, double maxDepthM, double tempC, string gasMix)
    {
        const int step = 30;
        var samples = new List<ProfileSampleDto>(durationSec / step + 1);
        var descendEnd = durationSec * 0.18;
        var ascendStart = durationSec * 0.78;
        for (var t = 0; t <= durationSec; t += step)
        {
            var depth = t <= descendEnd
                ? maxDepthM * (t / descendEnd)
                : t < ascendStart
                    ? maxDepthM * (0.92 + 0.08 * Math.Sin(t / 90.0)) // gentle bottom-phase undulation
                    : maxDepthM * Math.Max(0, 1 - ((t - ascendStart) / (durationSec - ascendStart)));
            samples.Add(new ProfileSampleDto(t, Math.Round(depth, 1), Math.Round(tempC, 1), null));
        }

        var avg = Math.Round(samples.Average(s => s.DepthM), 1);
        return new ImportedDive(startUtc, durationSec, maxDepthM, avg, tempC, gasMix, samples);
    }
}
