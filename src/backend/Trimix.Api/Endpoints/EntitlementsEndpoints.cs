using Microsoft.EntityFrameworkCore;
using Trimix.Api.Auth;
using Trimix.Api.Data;
using Trimix.Contracts;
using Trimix.Contracts.Dtos;

namespace Trimix.Api.Endpoints;

/// <summary>
/// GET /entitlements/me — the server's authoritative answer (architecture overview §1). The client
/// caches this; it never decides tier from a receipt.
/// </summary>
public static class EntitlementsEndpoints
{
    public static RouteGroupBuilder MapEntitlementsEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup(ApiRoutes.Entitlements.Group);

        group.MapGet(ApiRoutes.Entitlements.Me, async (
            HttpContext httpContext,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var userId = httpContext.User.RequireUserId();
            var tier = await db.Entitlements
                .Where(e => e.UserId == userId)
                .Select(e => (EntitlementTier?)e.Tier)
                .SingleOrDefaultAsync(cancellationToken) ?? EntitlementTier.Free;
            return Results.Ok(new EntitlementResponse(tier));
        }).RequireAuthorization();

        return api;
    }
}
