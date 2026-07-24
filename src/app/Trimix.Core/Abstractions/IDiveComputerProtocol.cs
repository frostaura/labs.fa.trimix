using Trimix.Contracts.Dtos;

namespace Trimix.Core.Abstractions;

/// <summary>
/// A dive as parsed from a vendor protocol, before it becomes a logged <see cref="DiveDto"/>.
/// <paramref name="Cylinders"/> is an additive aggregator-foundation channel — optional, defaults null so
/// a protocol that reports no cylinder detail (today: all of them) compiles and behaves unchanged.
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

/// <summary>
/// Per-vendor frame parser (architecture overview §4) — pure C#, unit-testable against fixture frames.
/// Slice implements ShearwaterProtocol; Suunto/Garmin/Mares exist as typed placeholders returning
/// NotSupported so the abstraction is proven four-wide.
/// </summary>
public interface IDiveComputerProtocol
{
    DiveComputerVendor Vendor { get; }

    /// <summary>Parses the transport's frame stream into dives new since the last sync.</summary>
    Task<IReadOnlyList<ImportedDive>> ParseAsync(IAsyncEnumerable<DiveComputerFrame> frames, CancellationToken cancellationToken = default);
}
