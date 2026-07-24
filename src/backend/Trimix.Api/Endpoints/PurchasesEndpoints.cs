using Microsoft.EntityFrameworkCore;
using Trimix.Api.Analytics;
using Trimix.Api.Auth;
using Trimix.Api.Billing;
using Trimix.Api.Data;
using Trimix.Contracts;
using Trimix.Contracts.Dtos;

namespace Trimix.Api.Endpoints;

/// <summary>
/// POST /purchases/validate (architecture overview §1, §3): the ONLY path to an entitlement.
/// Verified receipt → upsert entitlement keyed by user → server-emitted <c>entitlement_granted</c>
/// (first time per receipt) or <c>entitlement_restored</c> (any subsequent reconciliation).
/// Restore is the same endpoint, idempotent. Lifetime supersedes Pro; restores never downgrade.
/// </summary>
public static class PurchasesEndpoints
{
    public static RouteGroupBuilder MapPurchasesEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup(ApiRoutes.Purchases.Group);

        group.MapPost(ApiRoutes.Purchases.Validate, async (
            ValidatePurchaseRequest request,
            HttpContext httpContext,
            AppDbContext db,
            IEnumerable<IReceiptVerifier> verifiers,
            ServerAnalytics serverAnalytics,
            CancellationToken cancellationToken) =>
        {
            var userId = httpContext.User.RequireUserId();

            var verifier = verifiers.FirstOrDefault(v => string.Equals(v.Platform, request.Platform, StringComparison.Ordinal));
            if (verifier is null)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    detail: $"Unknown receipt platform '{request.Platform}'.");
            }

            var sku = Skus.All.FirstOrDefault(s => string.Equals(s.Id, request.SkuId, StringComparison.Ordinal));
            if (sku is null)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    detail: $"Unknown SKU '{request.SkuId}'.");
            }

            var verification = await verifier.VerifyAsync(request.SkuId, request.Token, cancellationToken);
            if (verification.Status == ReceiptVerificationStatus.NotConfigured)
            {
                return Results.Problem(statusCode: StatusCodes.Status501NotImplemented, detail: verification.Error);
            }

            if (verification.Status != ReceiptVerificationStatus.Valid)
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, detail: verification.Error);
            }

            var now = DateTime.UtcNow;
            // Dedup on the fixed-length hash, never the raw token: a real Apple bundle receipt is multi-KB
            // and cannot be a btree unique key. The hash is deterministic, so lookup + insert agree.
            var tokenHash = ReceiptTokenHash.Compute(request.Token);
            var receipt = await db.Receipts.SingleOrDefaultAsync(r => r.TokenSha256 == tokenHash, cancellationToken);
            PurchaseValidationOutcome outcome;
            if (receipt is null)
            {
                receipt = new ReceiptRecord
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Platform = request.Platform,
                    SkuId = request.SkuId,
                    Token = request.Token,
                    TokenSha256 = tokenHash,
                    FirstSeenUtc = now,
                };
                db.Receipts.Add(receipt);

                // Persist the receipt in its own unit of work so a concurrent insert of the same token
                // surfaces as the designed restore/409 instead of an unhandled 500 on the unique index.
                try
                {
                    await db.SaveChangesAsync(cancellationToken);
                    outcome = PurchaseValidationOutcome.Granted;
                }
                catch (DbUpdateException)
                {
                    // A DbUpdateException means the write failed — but ONLY a unique-hash conflict means the
                    // receipt row is actually there to restore from. Re-query: if it exists, treat it as the
                    // designed concurrent-restore race; if it does NOT (the write failed for another reason,
                    // e.g. a concurrent GDPR delete of the user FK), rethrow so the real error surfaces instead
                    // of a misleading "sequence contains no elements" 500.
                    db.Entry(receipt).State = EntityState.Detached;
                    var existing = await db.Receipts.SingleOrDefaultAsync(r => r.TokenSha256 == tokenHash, cancellationToken);
                    if (existing is null)
                    {
                        throw;
                    }

                    receipt = existing;
                    if (receipt.UserId != userId)
                    {
                        return Results.Problem(
                            statusCode: StatusCodes.Status409Conflict,
                            detail: "This receipt is already bound to a different account.");
                    }

                    outcome = PurchaseValidationOutcome.Restored;
                }
            }
            else if (receipt.UserId != userId)
            {
                // A receipt binds to exactly one account: replaying another user's receipt grants nothing.
                return Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    detail: "This receipt is already bound to a different account.");
            }
            else
            {
                outcome = PurchaseValidationOutcome.Restored;
            }

            var entitlement = await db.Entitlements.SingleOrDefaultAsync(e => e.UserId == userId, cancellationToken);
            if (entitlement is null)
            {
                entitlement = new Entitlement
                {
                    UserId = userId,
                    Tier = sku.GrantsTier,
                    SourceReceiptId = receipt.Id,
                    Platform = request.Platform,
                    GrantedAtUtc = now,
                };
                db.Entitlements.Add(entitlement);
            }
            else if (sku.GrantsTier > entitlement.Tier)
            {
                // Upgrade only (Lifetime ⊇ Pro). Restoring a lesser receipt never downgrades.
                entitlement.Tier = sku.GrantsTier;
                entitlement.SourceReceiptId = receipt.Id;
                entitlement.Platform = request.Platform;
                entitlement.GrantedAtUtc = now;
            }

            var pseudoId = ServerAnalytics.ResolvePseudoId(httpContext, userId);
            if (outcome == PurchaseValidationOutcome.Granted)
            {
                serverAnalytics.Emit(
                    FunnelEvents.EntitlementGranted,
                    new Dictionary<string, string>
                    {
                        // The tier THIS receipt/SKU grants — not the resulting entitlement (which may
                        // already be higher). A new Pro receipt for a Lifetime holder is a pro grant.
                        [FunnelEvents.Props.Tier] = TierStrings.From(sku.GrantsTier),
                        [FunnelEvents.Props.SourceReceiptId] = receipt.Id.ToString(),
                    },
                    pseudoId,
                    userId);
            }
            else
            {
                serverAnalytics.Emit(
                    FunnelEvents.EntitlementRestored,
                    new Dictionary<string, string>
                    {
                        [FunnelEvents.Props.Tier] = TierStrings.From(entitlement.Tier),
                    },
                    pseudoId,
                    userId);
            }

            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(new ValidatePurchaseResponse(entitlement.Tier, outcome));
        }).RequireAuthorization();

        return api;
    }
}

/// <summary>Wire representation of tiers in funnel props (<c>free|pro|lifetime</c>, per the frozen taxonomy).</summary>
public static class TierStrings
{
    public static string From(EntitlementTier tier) => tier switch
    {
        EntitlementTier.Free => FunnelEvents.Values.EntitlementStateFree,
        EntitlementTier.Pro => "pro",
        EntitlementTier.Lifetime => "lifetime",
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown entitlement tier."),
    };
}
