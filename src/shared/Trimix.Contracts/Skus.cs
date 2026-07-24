namespace Trimix.Contracts;

/// <summary>Store product type. The locked ladder contains ONLY non-consumables — no auto-renew, ever.</summary>
public enum SkuType
{
    NonConsumable = 0,
}

/// <summary>One entry of the locked price ladder.</summary>
public sealed record SkuDefinition(
    string Id,
    string DisplayName,
    decimal PriceUsd,
    SkuType Type,
    EntitlementTier GrantsTier);

/// <summary>
/// Locked price ladder (PRD Layer 3): exactly two one-off SKUs, both true non-consumables.
/// No consumables, no DLC, no auto-renew.
/// </summary>
public static class Skus
{
    public const string Pro = "trimix.pro";
    public const string Lifetime = "trimix.lifetime";

    public static readonly SkuDefinition ProSku =
        new(Pro, "Pro", 14.99m, SkuType.NonConsumable, EntitlementTier.Pro);

    public static readonly SkuDefinition LifetimeSku =
        new(Lifetime, "Lifetime", 29.99m, SkuType.NonConsumable, EntitlementTier.Lifetime);

    public static readonly IReadOnlyList<SkuDefinition> All = [ProSku, LifetimeSku];
}
