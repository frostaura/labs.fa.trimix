using Microsoft.EntityFrameworkCore;
using Trimix.Api.Auth;
using Trimix.Api.Data;
using Trimix.Api.Sites;
using Trimix.Contracts;
using Trimix.Contracts.Dtos;

namespace Trimix.Api.Endpoints;

/// <summary>
/// GET /sites (name substring and/or lat/lng bounding-box prefilter + haversine ranking),
/// GET /sites/{id} (detail incl. approved community notes), POST /sites/contributions
/// (server-checked Pro/Lifetime gate → moderation queue). Architecture overview §5, §9.
/// </summary>
public static class SitesEndpoints
{
    private const int DefaultLimit = 20;
    private const int MaxLimit = 100;

    /// <summary>Bounding-box prefilter half-size in degrees of latitude (~555 km).</summary>
    private const double BoundingBoxLatDelta = 5.0;

    public static RouteGroupBuilder MapSitesEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup(ApiRoutes.Sites.Group);

        group.MapGet(ApiRoutes.Sites.Search, async (
            AppDbContext db,
            string? query,
            double? lat,
            double? lng,
            int? limit,
            CancellationToken cancellationToken) =>
        {
            var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
            var sites = db.Sites.Where(s => s.Status == SiteStatuses.Live);

            if (!string.IsNullOrWhiteSpace(query))
            {
                var needle = query.Trim().ToLower();
                sites = sites.Where(s => s.Name.ToLower().Contains(needle));
            }

            var hasGeo = lat.HasValue && lng.HasValue;
            if (hasGeo)
            {
                var (minLat, maxLat, minLng, maxLng) = GeoMath.BoundingBox(lat!.Value, lng!.Value, BoundingBoxLatDelta);
                var clampMinLat = Math.Max(minLat, -90.0);
                var clampMaxLat = Math.Min(maxLat, 90.0);

                if (maxLng > 180.0)
                {
                    // Box spills east across the antimeridian: [minLng, 180] ∪ [-180, maxLng-360].
                    var wrap = maxLng - 360.0;
                    sites = sites.Where(s => s.Lat >= clampMinLat && s.Lat <= clampMaxLat
                        && (s.Lng >= minLng || s.Lng <= wrap));
                }
                else if (minLng < -180.0)
                {
                    // Box spills west across the antimeridian: [minLng+360, 180] ∪ [-180, maxLng].
                    var wrap = minLng + 360.0;
                    sites = sites.Where(s => s.Lat >= clampMinLat && s.Lat <= clampMaxLat
                        && (s.Lng >= wrap || s.Lng <= maxLng));
                }
                else
                {
                    sites = sites.Where(s => s.Lat >= clampMinLat && s.Lat <= clampMaxLat
                        && s.Lng >= minLng && s.Lng <= maxLng);
                }
            }

            // Bounding box is the SQL-translatable prefilter; exact haversine ranking happens
            // in memory on the (slice-scale) prefiltered set.
            var prefiltered = await sites.ToListAsync(cancellationToken);

            var ranked = hasGeo
                ? prefiltered
                    .Select(s => (Site: s, Distance: (double?)GeoMath.HaversineKm(lat!.Value, lng!.Value, s.Lat, s.Lng)))
                    .OrderBy(x => x.Distance)
                : prefiltered
                    .Select(s => (Site: s, Distance: (double?)null))
                    .OrderBy(x => x.Site.Name, StringComparer.Ordinal);

            var results = ranked
                .Take(take)
                .Select(x => new SiteSummaryDto(
                    x.Site.Id, x.Site.Name, x.Site.Lat, x.Site.Lng,
                    x.Site.DepthMinM, x.Site.DepthMaxM, x.Site.Type, x.Distance))
                .ToList();

            return Results.Ok(new SiteSearchResponse(results));
        });

        group.MapGet(ApiRoutes.Sites.ById, async (
            Guid id,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var site = await db.Sites.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
            if (site is null)
            {
                return Results.NotFound();
            }

            var approved = await db.SiteContributions
                .Where(c => c.SiteId == id && c.Status == ContributionStatus.Approved)
                .OrderBy(c => c.ResolvedAtUtc)
                .ToListAsync(cancellationToken);

            var notes = new List<CommunityNoteDto>();
            foreach (var contribution in approved)
            {
                if (ContributionPayload.TryParse(contribution.PayloadJson, out var payload)
                    && !string.IsNullOrWhiteSpace(payload!.CommunityNote))
                {
                    notes.Add(new CommunityNoteDto(
                        contribution.Id,
                        payload.CommunityNote,
                        contribution.ResolvedAtUtc ?? contribution.SubmittedAtUtc));
                }
            }

            return Results.Ok(new SiteDetailDto(
                site.Id, site.Name, site.Lat, site.Lng, site.DepthMinM, site.DepthMaxM, site.Type,
                SplitCsv(site.HazardsCsv), SplitCsv(site.MarineLifeCsv), site.AccessNotes,
                site.Status, site.Source, notes));
        });

        group.MapPost(ApiRoutes.Sites.Contributions, async (
            SiteContributionRequest request,
            HttpContext httpContext,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var userId = httpContext.User.RequireUserId();

            // Cap payload size before touching the DB — a moderation-queue row a moderator must load.
            if (request.PayloadJson is { Length: > AppDbContext.ContributionPayloadMaxLength })
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    detail: $"Contribution payload exceeds the {AppDbContext.ContributionPayloadMaxLength}-character limit.");
            }

            // Server-authoritative contribution gate (§9): Pro/Lifetime only in the slice.
            var tier = await db.Entitlements
                .Where(e => e.UserId == userId)
                .Select(e => (EntitlementTier?)e.Tier)
                .SingleOrDefaultAsync(cancellationToken) ?? EntitlementTier.Free;
            if (tier < EntitlementTier.Pro)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status403Forbidden,
                    detail: "Site contributions require a Pro or Lifetime entitlement.");
            }

            if (!ContributionPayload.TryParse(request.PayloadJson, out _))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    detail: "PayloadJson is not valid contribution JSON.");
            }

            if (request.SiteId is { } siteId
                && !await db.Sites.AnyAsync(s => s.Id == siteId, cancellationToken))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    detail: $"Site '{siteId}' does not exist.");
            }

            var contribution = new SiteContribution
            {
                Id = Guid.NewGuid(),
                SiteId = request.SiteId,
                UserId = userId,
                PayloadJson = request.PayloadJson,
                Status = ContributionStatus.Pending,
                SubmittedAtUtc = DateTime.UtcNow,
            };
            db.SiteContributions.Add(contribution);
            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(new SiteContributionResponse(contribution.Id, contribution.Status));
        }).RequireAuthorization();

        // The contributor's own submissions + their moderation outcome (PRD Flow C: 'contributor
        // notified'). The read path the app polls to surface approved/rejected status and any note.
        group.MapGet(ApiRoutes.Sites.ContributionsMine, async (
            HttpContext httpContext,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var userId = httpContext.User.RequireUserId();
            var mine = await db.SiteContributions
                .Where(c => c.UserId == userId)
                .OrderByDescending(c => c.SubmittedAtUtc)
                .Select(c => new SiteContributionDto(
                    c.Id, c.SiteId, c.UserId, c.PayloadJson, c.Status, c.ModeratorId, c.ResolvedAtUtc, c.Note))
                .ToListAsync(cancellationToken);
            return Results.Ok(new ModerationQueueResponse(mine));
        }).RequireAuthorization();

        return api;
    }

    private static IReadOnlyList<string> SplitCsv(string? csv) =>
        string.IsNullOrWhiteSpace(csv) ? [] : csv.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}
