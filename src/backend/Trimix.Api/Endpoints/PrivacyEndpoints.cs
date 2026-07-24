using Microsoft.EntityFrameworkCore;
using Trimix.Api.Auth;
using Trimix.Api.Data;
using Trimix.Contracts;
using Trimix.Contracts.Dtos;

namespace Trimix.Api.Endpoints;

/// <summary>
/// GET /privacy/export, POST /privacy/delete (GDPR/CCPA self-service). Export returns the caller's
/// account + dives; delete performs server-side erasure of the caller's dives, analytics rows,
/// contributions, receipts, entitlement, and account. Both require the caller's own bearer token.
/// </summary>
public static class PrivacyEndpoints
{
    public static RouteGroupBuilder MapPrivacyEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup(ApiRoutes.Privacy.Group).RequireAuthorization();

        group.MapGet(ApiRoutes.Privacy.Export, async (
            HttpContext httpContext,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var userId = httpContext.User.RequireUserId();

            var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
            if (user is null)
            {
                return Results.NotFound();
            }

            var tier = await db.Entitlements
                .Where(e => e.UserId == userId)
                .Select(e => (EntitlementTier?)e.Tier)
                .SingleOrDefaultAsync(cancellationToken) ?? EntitlementTier.Free;

            var dives = await db.Dives
                .Include(d => d.ProfileSamples)
                .Include(d => d.Cylinders)
                .Where(d => d.UserId == userId && !d.Deleted)
                .OrderBy(d => d.StartUtc)
                .ToListAsync(cancellationToken);

            var analyticsCount = await db.AnalyticsEvents.CountAsync(e => e.UserId == userId, cancellationToken);

            var response = new PrivacyExportResponse(
                new UserDto(user.Id, user.Email, user.Role, user.Verified, user.CreatedAtUtc),
                tier,
                dives.Select(ToDto).ToList(),
                analyticsCount);

            return Results.Ok(response);
        });

        group.MapPost(ApiRoutes.Privacy.Delete, async (
            HttpContext httpContext,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var userId = httpContext.User.RequireUserId();

            var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
            if (user is null)
            {
                return Results.NotFound();
            }

            var dives = await db.Dives
                .Include(d => d.ProfileSamples)
                .Include(d => d.Cylinders)
                .Where(d => d.UserId == userId)
                .ToListAsync(cancellationToken);

            // Erase analytics by the user's device PSEUDO-ID(s), not only by UserId. Pre-auth / guest funnel
            // rows (install, first_run_completed, dive_logged_*) are ingested with UserId=null and a populated
            // UserPseudoId; deleting only UserId==userId would leave that behavioral telemetry — which is
            // device-linkable personal data — behind after account deletion. Collect the pseudo-ids the user's
            // rows are stamped with, then erase every row under those pseudo-ids too.
            var pseudoIds = await db.AnalyticsEvents
                .Where(e => e.UserId == userId)
                .Select(e => e.UserPseudoId)
                .Distinct()
                .ToListAsync(cancellationToken);
            var analytics = await db.AnalyticsEvents
                .Where(e => e.UserId == userId || pseudoIds.Contains(e.UserPseudoId))
                .ToListAsync(cancellationToken);
            var contributions = await db.SiteContributions.Where(c => c.UserId == userId).ToListAsync(cancellationToken);
            var receipts = await db.Receipts.Where(r => r.UserId == userId).ToListAsync(cancellationToken);
            var entitlement = await db.Entitlements.SingleOrDefaultAsync(e => e.UserId == userId, cancellationToken);

            db.ProfileSamples.RemoveRange(dives.SelectMany(d => d.ProfileSamples));
            db.Cylinders.RemoveRange(dives.SelectMany(d => d.Cylinders));
            db.Dives.RemoveRange(dives);
            db.AnalyticsEvents.RemoveRange(analytics);
            db.SiteContributions.RemoveRange(contributions);
            db.Receipts.RemoveRange(receipts);
            if (entitlement is not null)
            {
                db.Entitlements.Remove(entitlement);
            }

            db.Users.Remove(user);

            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(new PrivacyDeleteResponse(dives.Count, analytics.Count, contributions.Count));
        });

        return api;
    }

    private static DiveDto ToDto(Dive dive) => DiveMapping.ToDto(dive);
}
