namespace Trimix.Contracts;

/// <summary>
/// Server-authoritative entitlement tier. Lifetime is a superset of Pro.
/// The client never derives tier from a receipt — it caches the server's answer.
/// </summary>
public enum EntitlementTier
{
    Free = 0,
    Pro = 1,
    Lifetime = 2,
}
