using Trimix.Contracts;

namespace Trimix.Core.Abstractions;

/// <summary>
/// Client-side view of the server-authoritative entitlement (architecture overview §1):
/// caches the server's answer in <see cref="ISecureStore"/>, refreshes on launch/sign-in,
/// auto-restores store purchases after sign-in on a fresh install.
/// </summary>
public interface IEntitlementService
{
    /// <summary>Last tier the server told us (cached; <see cref="EntitlementTier.Free"/> until known).</summary>
    EntitlementTier CurrentTier { get; }

    event EventHandler<EntitlementTier>? TierChanged;

    /// <summary>Loads the cached tier from secure storage (call at launch, before any UI gate).</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-asks the server (<c>GET /entitlements/me</c>) and updates the cache. Keeps the cached
    /// tier when the server is unreachable (offline-tolerant).
    /// </summary>
    Task<EntitlementTier> RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Post-sign-in hook: refreshes from the server and — once per install — auto-restores store
    /// purchases (PRD Flow D: restore auto-invoked on fresh install after sign-in).
    /// </summary>
    Task OnSignedInAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Manual "Restore purchases": <see cref="IBillingService.RestoreAsync"/>, then
    /// <c>POST /purchases/validate</c> per receipt (idempotent server-side), then refresh.
    /// Returns the number of receipts reconciled. Individual receipt failures do not abort the rest.
    /// </summary>
    Task<int> RestoreAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-validates receipts left pending by a failed post-purchase validate (store-charged but not yet
    /// granted). Called on launch/sign-in so a charged user is never stranded without their entitlement.
    /// </summary>
    Task ReconcilePendingAsync(CancellationToken cancellationToken = default);
}
