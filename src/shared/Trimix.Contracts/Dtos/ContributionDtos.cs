namespace Trimix.Contracts.Dtos;

/// <summary><c>POST /sites/contributions</c> request. Null <paramref name="SiteId"/> proposes a new site.</summary>
public sealed record SiteContributionRequest(Guid? SiteId, string PayloadJson);

/// <summary>
/// Canonical shape of <see cref="SiteContributionRequest.PayloadJson"/> — the single source of truth
/// for the contribution payload shared by client and server. Field names/casing match the server's
/// <c>Trimix.Api.Sites.ContributionPayload</c> EXACTLY (web/camelCase JSON keys: <c>name</c>, <c>lat</c>,
/// <c>lng</c>, <c>depthMinM</c>, <c>depthMaxM</c>, <c>type</c>, <c>hazardsCsv</c>, <c>marineLifeCsv</c>,
/// <c>accessNotes</c>, <c>communityNote</c>). All fields optional; on moderator approval every non-null
/// field is applied to the target site (Name/Lat/Lng required when proposing a NEW site).
/// <para>
/// NOTE (client): <c>ContributeViewModel</c> MUST serialize its form to THIS DTO. The current app sends
/// <c>{"note":...}</c>, which the server's payload parser cannot read (there is no <c>note</c> field) —
/// the note belongs in <see cref="CommunityNote"/>. Backend agent: converge
/// <c>Trimix.Api.Sites.ContributionPayload</c> onto this shared record.
/// </para>
/// </summary>
public sealed record SiteContributionPayload(
    string? CommunityNote = null,
    string? Name = null,
    double? Lat = null,
    double? Lng = null,
    double? DepthMinM = null,
    double? DepthMaxM = null,
    string? Type = null,
    string? HazardsCsv = null,
    string? MarineLifeCsv = null,
    string? AccessNotes = null);

/// <summary><c>POST /sites/contributions</c> response — enqueued for moderation (<see cref="ContributionStatus.Pending"/>).</summary>
public sealed record SiteContributionResponse(Guid Id, string Status);

/// <summary>A contribution as seen by moderators.</summary>
public sealed record SiteContributionDto(
    Guid Id,
    Guid? SiteId,
    Guid UserId,
    string PayloadJson,
    string Status,
    Guid? ModeratorId,
    DateTime? ResolvedAtUtc,
    string? Note);

/// <summary><c>GET /moderation/contributions?status=pending</c> response.</summary>
public sealed record ModerationQueueResponse(IReadOnlyList<SiteContributionDto> Contributions);

/// <summary><c>POST /moderation/contributions/{id}/resolve</c> request.</summary>
public sealed record ResolveContributionRequest(bool Approved, string? Note);

/// <summary><c>POST /moderation/contributions/{id}/resolve</c> response.</summary>
public sealed record ResolveContributionResponse(Guid Id, string Status);
