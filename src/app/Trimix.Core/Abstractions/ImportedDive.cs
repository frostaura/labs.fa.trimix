using Trimix.Contracts.Dtos;

namespace Trimix.Core.Abstractions;

/// <summary>
/// A dive as parsed from a source connector, before it becomes a logged <see cref="DiveDto"/>.
/// This is the shared connector seam: every <see cref="IDiveSourceConnector"/> produces these and
/// <c>ImportService.ImportFromConnectorAsync</c> consumes them (site auto-match → free-tier
/// truncation → local upsert → <c>dive_logged_import</c>).
/// <paramref name="Cylinders"/> is an additive aggregator-foundation channel — optional, defaults null so
/// a source that reports no cylinder detail compiles and behaves unchanged.
/// </summary>
public sealed record ImportedDive(
    DateTime StartUtc,
    int DurationSec,
    double MaxDepthM,
    double? AvgDepthM,
    double? MinTempC,
    string? GasMix,
    IReadOnlyList<ProfileSampleDto> Samples,
    IReadOnlyList<CylinderDto>? Cylinders = null,
    // ── rich provider channels, mirrored onto DiveDto by ImportService.ToDiveDto ──
    // All optional by design: each connector fills what its provider actually returns, and the UI hides
    // whatever is null. That is the whole per-provider graceful-degradation mechanism — there is no
    // capability enum to keep in sync, a gap is simply a null.
    int? DiveNumber = null,
    int? BottomTimeSec = null,
    int? SurfaceIntervalSec = null,
    bool? IsDeco = null,
    string? DiveTypeLabel = null,
    string? TimezoneId = null,
    string? WaterType = null,
    DiveEnvironmentDto? Environment = null,
    DiveExposureDto? Exposure = null,
    DiveComputerDto? Computer = null,
    IReadOnlyList<DiveAlertDto>? Alerts = null,
    IReadOnlyList<string>? Tags = null);
