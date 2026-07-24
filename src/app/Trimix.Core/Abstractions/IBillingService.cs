namespace Trimix.Core.Abstractions;

/// <summary>A store product as reported by the (sandbox or real) store.</summary>
public sealed record StoreProduct(
    string SkuId,
    string DisplayName,
    string FormattedPrice,
    decimal Price,
    string CurrencyCode);

/// <summary>
/// An opaque purchase receipt. The client NEVER derives entitlement from this — it is posted to
/// <c>POST /api/v1/purchases/validate</c> and the server answers with the tier.
/// <paramref name="Platform"/> is a <see cref="Trimix.Contracts.ReceiptPlatforms"/> value.
/// </summary>
public sealed record StoreReceipt(string Platform, string SkuId, string Token);

/// <summary>
/// Billing abstraction shaped to Play Billing semantics (architecture overview §2).
/// Implementations: SandboxBillingService (Core; deterministic HMAC-signed tokens — the slice's live path)
/// and PlayBillingService (App/Android; inert until a Play listing exists).
/// DI picks sandbox in Debug / <c>TRIMIX_SANDBOX_BILLING=1</c>.
/// </summary>
public interface IBillingService
{
    Task<IReadOnlyList<StoreProduct>> QueryProductsAsync(IReadOnlyList<string> skuIds, CancellationToken cancellationToken = default);

    Task<StoreReceipt> PurchaseAsync(string skuId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StoreReceipt>> RestoreAsync(CancellationToken cancellationToken = default);
}
