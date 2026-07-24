namespace Trimix.Contracts.Dtos;

/// <summary>
/// <c>GET /privacy/export</c> response — the caller's personal data (GDPR/CCPA portability): their
/// account, current entitlement tier, and every dive they have synced. Idempotent, read-only.
/// </summary>
public sealed record PrivacyExportResponse(
    UserDto Account,
    EntitlementTier EntitlementTier,
    IReadOnlyList<DiveDto> Dives,
    int AnalyticsEventCount);

/// <summary>
/// <c>POST /privacy/delete</c> response — the erasure summary. After this call the account and all
/// its data are gone and the caller's token no longer resolves to a user.
/// </summary>
public sealed record PrivacyDeleteResponse(
    int DivesErased,
    int AnalyticsEventsErased,
    int ContributionsErased);
