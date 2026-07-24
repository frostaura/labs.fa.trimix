namespace Trimix.Contracts.Dtos;

/// <summary><c>GET /entitlements/me</c> response — the server's authoritative answer.</summary>
public sealed record EntitlementResponse(EntitlementTier Tier);
