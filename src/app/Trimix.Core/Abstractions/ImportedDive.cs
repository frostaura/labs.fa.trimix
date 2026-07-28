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
    IReadOnlyList<CylinderDto>? Cylinders = null);
