namespace Trimix.Contracts.Dtos;

/// <summary>
/// One point of a dive profile (composite key <c>DiveId + TSeconds</c> server-side). The trailing
/// channels are additive rich-fidelity fields (aggregator foundation): they arrive from higher-fidelity
/// sources (dive-computer NDL/ceiling/CNS/setpoint, transmitter tank pressure, HR belt) and are all
/// optional — a low-fidelity source leaves them null.
/// </summary>
public sealed record ProfileSampleDto(
    int TSeconds,
    double DepthM,
    double? TempC,
    double? PpO2,
    double? Ndl = null,
    double? CeilingM = null,
    double? AscentRateMPerMin = null,
    double? Cns = null,
    double? TankPressureBar = null,
    double? Setpoint = null,
    double? HeartRate = null);

/// <summary>
/// A gas cylinder used on a dive (aggregator foundation). Fully optional: a source that reports no
/// cylinder detail contributes none. <paramref name="O2"/>/<paramref name="He"/> are gas-mix percentages.
/// </summary>
public sealed record CylinderDto(
    double? O2 = null,
    double? He = null,
    double? StartBar = null,
    double? EndBar = null,
    double? VolumeL = null,
    double? WorkingPressureBar = null);

/// <summary>
/// A dive as synced between client and server. <paramref name="Id"/> is client-generated. The trailing
/// fields (cylinders, typed conditions, weight) are additive aggregator-foundation channels; every one is
/// optional and defaults null so all existing construction sites compile unchanged. <c>ConditionsJson</c>
/// is retained for back-compat — no data migration in this phase.
/// </summary>
public sealed record DiveDto(
    Guid Id,
    DateTime StartUtc,
    int DurationSec,
    double MaxDepthM,
    double? AvgDepthM,
    double? MinTempC,
    string? GasMix,
    string Source,
    string? SourceVendor,
    Guid? SiteId,
    string? ConditionsJson,
    string? GearCsv,
    string? BuddiesCsv,
    string? Notes,
    int? Rating,
    DateTime UpdatedAtUtc,
    bool Deleted,
    IReadOnlyList<ProfileSampleDto> ProfileSamples,
    IReadOnlyList<CylinderDto>? Cylinders = null,
    string? Visibility = null,
    string? Current = null,
    string? Weather = null,
    string? EntryType = null,
    string? WaterType = null,
    string? ExposureSuit = null,
    double? WeightKg = null);

/// <summary><c>POST /dives/sync</c> request: upsert client dives; server returns changes since <paramref name="Since"/>.</summary>
public sealed record DiveSyncRequest(
    IReadOnlyList<DiveDto> Dives,
    IReadOnlyList<Guid> DeletedIds,
    DateTime? Since);

/// <summary><c>POST /dives/sync</c> response: server-side changes (last-write-wins on <c>UpdatedAtUtc</c>).</summary>
public sealed record DiveSyncResponse(
    IReadOnlyList<DiveDto> Dives,
    IReadOnlyList<Guid> DeletedIds,
    DateTime ServerTimeUtc);
