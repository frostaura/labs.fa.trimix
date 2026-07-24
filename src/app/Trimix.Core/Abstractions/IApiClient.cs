using Trimix.Contracts.Dtos;

namespace Trimix.Core.Abstractions;

/// <summary>
/// Typed client for the Trimix API (routes in <see cref="Trimix.Contracts.ApiRoutes"/>).
/// Attaches the bearer token from <see cref="ISecureStore"/> when signed in.
/// </summary>
public interface IApiClient
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);

    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

    // Passkey (WebAuthn) — the app's sole account sign-in. Two-step begin→finish per flow.
    Task<PasskeyRegisterBeginResponse> PasskeyRegisterBeginAsync(PasskeyRegisterBeginRequest request, CancellationToken cancellationToken = default);

    Task<AuthResponse> PasskeyRegisterFinishAsync(PasskeyRegisterFinishRequest request, CancellationToken cancellationToken = default);

    Task<PasskeyLoginBeginResponse> PasskeyLoginBeginAsync(CancellationToken cancellationToken = default);

    Task<AuthResponse> PasskeyLoginFinishAsync(PasskeyLoginFinishRequest request, CancellationToken cancellationToken = default);

    Task<EntitlementResponse> GetMyEntitlementAsync(CancellationToken cancellationToken = default);

    Task<ValidatePurchaseResponse> ValidatePurchaseAsync(ValidatePurchaseRequest request, CancellationToken cancellationToken = default);

    Task<DiveSyncResponse> SyncDivesAsync(DiveSyncRequest request, CancellationToken cancellationToken = default);

    Task<SiteSearchResponse> SearchSitesAsync(string? query, double? lat, double? lng, int? limit, CancellationToken cancellationToken = default);

    Task<SiteDetailDto> GetSiteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<SiteContributionResponse> SubmitContributionAsync(SiteContributionRequest request, CancellationToken cancellationToken = default);

    Task<ModerationQueueResponse> GetModerationQueueAsync(string status, CancellationToken cancellationToken = default);

    Task<ResolveContributionResponse> ResolveContributionAsync(Guid contributionId, ResolveContributionRequest request, CancellationToken cancellationToken = default);

    Task<AnalyticsBatchResponse> SendAnalyticsAsync(AnalyticsBatchRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// GDPR/CCPA right-to-erasure: server-side hard delete of the caller's account, analytics, receipts,
    /// entitlement, contributions, and dives (<c>POST /privacy/delete</c>). Must be called while the bearer
    /// token still resolves (i.e. before sign-out).
    /// </summary>
    Task<PrivacyDeleteResponse> DeleteMyDataAsync(CancellationToken cancellationToken = default);
}
