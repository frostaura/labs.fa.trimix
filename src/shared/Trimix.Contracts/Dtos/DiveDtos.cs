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
/// Water and place conditions a source reported. Every field is optional: a provider that reports none
/// contributes no <see cref="DiveEnvironmentDto"/> at all, and the UI hides the corresponding rows.
/// </summary>
public sealed record DiveEnvironmentDto(
    double? MaxTempC = null,
    double? AvgTempC = null,
    double? WaterDensityKgM3 = null,
    double? EntryLatitude = null,
    double? EntryLongitude = null);

/// <summary>
/// Physiological exposure a dive computer tracked across the dive: oxygen-toxicity clock, CNS and nitrogen
/// loading at the start and end, and partial-pressure extremes.
///
/// <para><b>This is reported data, never computed.</b> Trimix displays what the computer recorded and does
/// not model tissue loading — see the permanent deco non-goal in this project's <c>CLAUDE.md</c>.</para>
/// </summary>
public sealed record DiveExposureDto(
    double? CnsStartPercent = null,
    double? CnsEndPercent = null,
    double? N2StartPercent = null,
    double? N2EndPercent = null,
    double? O2ToxicityPercent = null,
    double? AvgPo2 = null,
    double? MaxPo2 = null);

/// <summary>
/// The device that recorded the dive and the deco configuration it was running. Again reported, not
/// computed — gradient factors are shown so the diver can see what their computer was set to.
/// </summary>
public sealed record DiveComputerDto(
    string? DeviceName = null,
    string? TissueModel = null,
    int? GradientFactorLow = null,
    int? GradientFactorHigh = null);

/// <summary>
/// One timestamped event a dive computer raised (gas switch, max depth, min temperature, tank reserve).
/// <paramref name="TSeconds"/> is seconds from the dive start, matching <see cref="ProfileSampleDto"/>.
/// </summary>
public sealed record DiveAlertDto(int TSeconds, string Type, string? Detail = null);

/// <summary>
/// A dive as synced between client and server. <paramref name="Id"/> is client-generated. The trailing
/// fields (cylinders, typed conditions, weight, then the rich provider channels) are additive
/// aggregator-foundation channels; every one is optional and defaults null so all existing construction
/// sites compile unchanged. <c>ConditionsJson</c> is retained for back-compat — no data migration in this phase.
///
/// <para><b>Widening is free here.</b> The local store serialises the whole DTO as JSON, so there is no
/// schema column and no migration for any field added below. Providers populate what they have; the UI
/// hides what is null, which is how per-provider gaps degrade gracefully.</para>
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
    double? WeightKg = null,
    // ── rich provider channels (Garmin Dive supplies all of these; other providers supply a subset) ──
    int? DiveNumber = null,
    int? BottomTimeSec = null,
    int? SurfaceIntervalSec = null,
    bool? IsDeco = null,
    string? DiveTypeLabel = null,
    string? TimezoneId = null,
    DiveEnvironmentDto? Environment = null,
    DiveExposureDto? Exposure = null,
    DiveComputerDto? Computer = null,
    IReadOnlyList<DiveAlertDto>? Alerts = null,
    IReadOnlyList<string>? Tags = null);

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
