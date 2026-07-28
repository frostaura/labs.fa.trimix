using Trimix.Contracts;

namespace Trimix.Core.Abstractions;

/// <summary>
/// On-device entitlement view (serverless): the tier is derived locally from store receipts and
/// cached in <see cref="ISecureStore"/>. No server is consulted.
/// </summary>
public interface IEntitlementService
{
    /// <summary>Current locally-derived tier (<see cref="EntitlementTier.Free"/> until a grant).</summary>
    EntitlementTier CurrentTier { get; }

    event EventHandler<EntitlementTier>? TierChanged;

    /// <summary>Loads the cached tier from secure storage (call at launch, before any UI gate).</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>Reports the current tier (the local cache is the authority — kept for call-site compatibility).</summary>
    Task<EntitlementTier> RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>Post-sign-in hook: once per install, auto-restores store purchases (PRD Flow D).</summary>
    Task OnSignedInAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Manual "Restore purchases": <see cref="IBillingService.RestoreAsync"/>, then a local
    /// receipt→SKU→tier grant. Returns the number of receipts reconciled.
    /// </summary>
    Task<int> RestoreAsync(CancellationToken cancellationToken = default);

    /// <summary>Serverless no-op (there is no pending server-validation queue). Kept for call-site compatibility.</summary>
    Task ReconcilePendingAsync(CancellationToken cancellationToken = default);
}
