namespace Trimix.Contracts.Dtos;

/// <summary>One row of <c>GET /sites</c> search results (haversine-ranked when lat/lng given).</summary>
public sealed record SiteSummaryDto(
    Guid Id,
    string Name,
    double Lat,
    double Lng,
    double? DepthMinM,
    double? DepthMaxM,
    string? Type,
    double? DistanceKm);

/// <summary><c>GET /sites</c> response.</summary>
public sealed record SiteSearchResponse(IReadOnlyList<SiteSummaryDto> Sites);

/// <summary>An approved community note shown on site detail.</summary>
public sealed record CommunityNoteDto(
    Guid ContributionId,
    string Note,
    DateTime ResolvedAtUtc);

/// <summary><c>GET /sites/{id}</c> response, including approved community notes.</summary>
public sealed record SiteDetailDto(
    Guid Id,
    string Name,
    double Lat,
    double Lng,
    double? DepthMinM,
    double? DepthMaxM,
    string? Type,
    IReadOnlyList<string> Hazards,
    IReadOnlyList<string> MarineLife,
    string? AccessNotes,
    string Status,
    string Source,
    IReadOnlyList<CommunityNoteDto> CommunityNotes);
