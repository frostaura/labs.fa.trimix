namespace Trimix.Core.Abstractions;

/// <summary>A device location fix.</summary>
public sealed record GeoLocation(double Lat, double Lng);

/// <summary>
/// Device location for site search/auto-match. Permission is just-in-time, "while using" only,
/// never background (PRD privacy posture).
/// </summary>
public interface ILocationService
{
    /// <summary>Returns the current location, or null when permission is denied/unavailable.</summary>
    Task<GeoLocation?> GetCurrentLocationAsync(CancellationToken cancellationToken = default);
}
