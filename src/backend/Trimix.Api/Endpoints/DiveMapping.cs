using Trimix.Api.Data;
using Trimix.Contracts.Dtos;

namespace Trimix.Api.Endpoints;

/// <summary>
/// Single source of truth for projecting a persisted <see cref="Dive"/> back to its wire
/// <see cref="DiveDto"/>. Shared by the sync and privacy-export endpoints so the rich aggregator
/// channels (per-sample NDL/ceiling/CNS/…, cylinders, typed conditions) can never drift between the
/// two read paths. Callers must <c>Include</c> both <see cref="Dive.ProfileSamples"/> and
/// <see cref="Dive.Cylinders"/> before projecting.
/// </summary>
internal static class DiveMapping
{
    public static DiveDto ToDto(Dive dive) => new(
        dive.Id,
        dive.StartUtc,
        dive.DurationSec,
        dive.MaxDepthM,
        dive.AvgDepthM,
        dive.MinTempC,
        dive.GasMix,
        dive.Source,
        dive.SourceVendor,
        dive.SiteId,
        dive.ConditionsJson,
        dive.GearCsv,
        dive.BuddiesCsv,
        dive.Notes,
        dive.Rating,
        dive.UpdatedAtUtc,
        dive.Deleted,
        dive.ProfileSamples
            .OrderBy(s => s.TSeconds)
            .Select(s => new ProfileSampleDto(
                s.TSeconds,
                s.DepthM,
                s.TempC,
                s.PpO2,
                s.Ndl,
                s.CeilingM,
                s.AscentRateMPerMin,
                s.Cns,
                s.TankPressureBar,
                s.Setpoint,
                s.HeartRate))
            .ToList(),
        dive.Cylinders
            .OrderBy(c => c.Ordinal)
            .Select(c => new CylinderDto(c.O2, c.He, c.StartBar, c.EndBar, c.VolumeL, c.WorkingPressureBar))
            .ToList(),
        dive.Visibility,
        dive.Current,
        dive.Weather,
        dive.EntryType,
        dive.WaterType,
        dive.ExposureSuit,
        dive.WeightKg);
}
