using Trimix.Contracts.Dtos;

namespace Trimix.Core.Tests.Stats;

/// <summary>Terse builders for the stats-slice tests: dives, profile samples, and cylinders with sane defaults.</summary>
internal static class StatsTestFactory
{
    public static readonly DateTime BaseUtc = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    public static DiveDto Dive(
        DateTime? start = null,
        int durationSec = 3000,
        double maxDepthM = 30,
        double? avgDepthM = null,
        double? minTempC = null,
        string? gasMix = null,
        string source = "import",
        string? sourceVendor = "shearwater",
        Guid? siteId = null,
        string? notes = null,
        int? rating = null,
        DateTime? updatedAtUtc = null,
        IReadOnlyList<ProfileSampleDto>? samples = null,
        IReadOnlyList<CylinderDto>? cylinders = null,
        Guid? id = null)
    {
        var startUtc = start ?? BaseUtc;
        return new DiveDto(
            id ?? Guid.NewGuid(),
            startUtc,
            durationSec,
            maxDepthM,
            avgDepthM,
            minTempC,
            gasMix,
            source,
            sourceVendor,
            siteId,
            ConditionsJson: null,
            GearCsv: null,
            BuddiesCsv: null,
            Notes: notes,
            rating,
            updatedAtUtc ?? startUtc,
            Deleted: false,
            ProfileSamples: samples ?? [],
            Cylinders: cylinders);
    }

    public static ProfileSampleDto Sample(
        int t,
        double depth,
        double? temp = null,
        double? ppo2 = null,
        double? ndl = null,
        double? ceiling = null,
        double? ascent = null,
        double? cns = null,
        double? tank = null,
        double? setpoint = null,
        double? hr = null)
        => new(t, depth, temp, ppo2, ndl, ceiling, ascent, cns, tank, setpoint, hr);

    public static CylinderDto Cyl(
        double? o2 = null,
        double? he = null,
        double? startBar = null,
        double? endBar = null,
        double? volumeL = null,
        double? workingBar = null)
        => new(o2, he, startBar, endBar, volumeL, workingBar);
}
