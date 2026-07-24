namespace Trimix.Contracts.Dtos;

/// <summary>
/// Whether receipt validation granted the entitlement first-time-per-receipt (<c>entitlement_granted</c>)
/// or reconciled an already-known receipt / sign-in restore (<c>entitlement_restored</c>).
/// </summary>
public enum PurchaseValidationOutcome
{
    Granted = 0,
    Restored = 1,
}

/// <summary><c>POST /purchases/validate</c> request. <paramref name="Platform"/> is a <see cref="ReceiptPlatforms"/> value.</summary>
public sealed record ValidatePurchaseRequest(string Platform, string SkuId, string Token);

/// <summary><c>POST /purchases/validate</c> response.</summary>
public sealed record ValidatePurchaseResponse(EntitlementTier Tier, PurchaseValidationOutcome Outcome);
