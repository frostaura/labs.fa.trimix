using Microsoft.EntityFrameworkCore;
using Trimix.Api.Analytics;
using Trimix.Api.Auth;
using Trimix.Api.Data;
using Trimix.Api.Sites;
using Trimix.Contracts;
using Trimix.Contracts.Dtos;

namespace Trimix.Api.Endpoints;

/// <summary>
/// GET /moderation/contributions, POST /moderation/contributions/{id}/resolve — moderator-only.
/// Approval applies the contribution payload to the target site (or creates a proposed site);
/// both outcomes emit the frozen <c>site_contribution_resolved</c> funnel event.
/// </summary>
public static class ModerationEndpoints
{
    public static RouteGroupBuilder MapModerationEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup(ApiRoutes.Moderation.Group).RequireAuthorization(AuthPolicies.Moderator);

        group.MapGet(ApiRoutes.Moderation.Contributions, async (
            HttpContext httpContext,
            AppDbContext db,
            string? status,
            CancellationToken cancellationToken) =>
        {
            if (!await IsStillModeratorAsync(httpContext, db, cancellationToken))
            {
                return Results.Forbid();
            }

            var wanted = string.IsNullOrWhiteSpace(status) ? ContributionStatus.Pending : status;
            var contributions = await db.SiteContributions
                .Where(c => c.Status == wanted)
                .OrderBy(c => c.SubmittedAtUtc)
                .Select(c => new SiteContributionDto(
                    c.Id, c.SiteId, c.UserId, c.PayloadJson, c.Status, c.ModeratorId, c.ResolvedAtUtc, c.Note))
                .ToListAsync(cancellationToken);
            return Results.Ok(new ModerationQueueResponse(contributions));
        });

        group.MapPost(ApiRoutes.Moderation.Resolve, async (
            Guid id,
            ResolveContributionRequest request,
            HttpContext httpContext,
            AppDbContext db,
            ServerAnalytics serverAnalytics,
            CancellationToken cancellationToken) =>
        {
            var moderatorId = httpContext.User.RequireUserId();

            // Re-check the role against the DB: the 30-day token's role claim can outlive a demotion.
            if (!await IsStillModeratorAsync(httpContext, db, cancellationToken))
            {
                return Results.Forbid();
            }

            var contribution = await db.SiteContributions.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
            if (contribution is null)
            {
                return Results.NotFound();
            }

            if (contribution.Status != ContributionStatus.Pending)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    detail: $"Contribution is already '{contribution.Status}'.");
            }

            if (request.Approved)
            {
                if (!ContributionPayload.TryParse(contribution.PayloadJson, out var payload))
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status422UnprocessableEntity,
                        detail: "Contribution payload is not valid JSON; reject it instead.");
                }

                if (contribution.SiteId is { } siteId)
                {
                    var site = await db.Sites.SingleAsync(s => s.Id == siteId, cancellationToken);
                    ApplyPayload(payload!, site);
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(payload!.Name) || payload.Lat is null || payload.Lng is null)
                    {
                        return Results.Problem(
                            statusCode: StatusCodes.Status422UnprocessableEntity,
                            detail: "A new-site contribution needs at least name, lat, and lng to be approved.");
                    }

                    var site = new Site
                    {
                        Id = Guid.NewGuid(),
                        Name = payload.Name,
                        Lat = payload.Lat.Value,
                        Lng = payload.Lng.Value,
                        Status = SiteStatuses.Live,
                        Source = SiteSources.Contributed,
                    };
                    ApplyPayload(payload, site);
                    db.Sites.Add(site);
                    contribution.SiteId = site.Id;
                }

                contribution.Status = ContributionStatus.Approved;
            }
            else
            {
                contribution.Status = ContributionStatus.Rejected;
            }

            contribution.ModeratorId = moderatorId;
            contribution.ResolvedAtUtc = DateTime.UtcNow;
            contribution.Note = request.Note;

            serverAnalytics.Emit(
                FunnelEvents.SiteContributionResolved,
                new Dictionary<string, string> { [FunnelEvents.Props.Status] = contribution.Status },
                await serverAnalytics.ResolvePseudoIdForUserAsync(contribution.UserId, cancellationToken),
                contribution.UserId);

            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(new ResolveContributionResponse(contribution.Id, contribution.Status));
        });

        return api;
    }

    /// <summary>
    /// True only if the caller's CURRENT database role is moderator. The <c>Moderator</c> policy
    /// checks the token's role claim, but that claim is baked into a 30-day JWT with no revocation —
    /// so moderation actions additionally verify the live role, closing the demotion-lag window.
    /// </summary>
    private static async Task<bool> IsStillModeratorAsync(
        HttpContext httpContext, AppDbContext db, CancellationToken cancellationToken)
    {
        var userId = httpContext.User.RequireUserId();
        var role = await db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.Role)
            .SingleOrDefaultAsync(cancellationToken);
        return role == UserRoles.Moderator;
    }

    private static void ApplyPayload(ContributionPayload payload, Site site)
    {
        if (!string.IsNullOrWhiteSpace(payload.Name))
        {
            site.Name = payload.Name;
        }

        site.Lat = payload.Lat ?? site.Lat;
        site.Lng = payload.Lng ?? site.Lng;
        site.DepthMinM = payload.DepthMinM ?? site.DepthMinM;
        site.DepthMaxM = payload.DepthMaxM ?? site.DepthMaxM;
        site.Type = payload.Type ?? site.Type;
        site.HazardsCsv = payload.HazardsCsv ?? site.HazardsCsv;
        site.MarineLifeCsv = payload.MarineLifeCsv ?? site.MarineLifeCsv;
        site.AccessNotes = payload.AccessNotes ?? site.AccessNotes;
    }
}
