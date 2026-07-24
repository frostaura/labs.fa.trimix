using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Trimix.Contracts;
using Trimix.Contracts.Dtos;
using Trimix.Core.Abstractions;

namespace Trimix.Core.Api;

/// <summary>
/// Typed HttpClient wrapper for every route in <see cref="ApiRoutes"/>. Attaches the bearer token
/// from <see cref="ISecureStore"/> when present, stamps the device analytics pseudo-id header
/// (<see cref="TrimixHeaders.UserPseudoId"/>) on EVERY request so server-emitted funnel events
/// (<c>account_created</c>, <c>entitlement_granted</c>, <c>entitlement_restored</c>) reconcile into
/// the same per-pseudo-user funnel as client-emitted events, maps non-success responses to
/// <see cref="ApiException"/>, and retries exactly once on transient failures (network fault, HTTP 408/5xx).
/// </summary>
public sealed class ApiClient(HttpClient http, ISecureStore secureStore, IUserPseudoIdProvider pseudoIds) : IApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        // Tolerate both enum-as-string and enum-as-number server serialization.
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    public Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<AuthResponse>(HttpMethod.Post, ApiRoutes.Auth.RegisterFull, request, cancellationToken);

    public Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<AuthResponse>(HttpMethod.Post, ApiRoutes.Auth.LoginFull, request, cancellationToken);

    public Task<PasskeyRegisterBeginResponse> PasskeyRegisterBeginAsync(PasskeyRegisterBeginRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<PasskeyRegisterBeginResponse>(HttpMethod.Post, ApiRoutes.Auth.PasskeyRegisterBeginFull, request, cancellationToken);

    public Task<AuthResponse> PasskeyRegisterFinishAsync(PasskeyRegisterFinishRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<AuthResponse>(HttpMethod.Post, ApiRoutes.Auth.PasskeyRegisterFinishFull, request, cancellationToken);

    public Task<PasskeyLoginBeginResponse> PasskeyLoginBeginAsync(CancellationToken cancellationToken = default) =>
        SendAsync<PasskeyLoginBeginResponse>(HttpMethod.Post, ApiRoutes.Auth.PasskeyLoginBeginFull, null, cancellationToken);

    public Task<AuthResponse> PasskeyLoginFinishAsync(PasskeyLoginFinishRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<AuthResponse>(HttpMethod.Post, ApiRoutes.Auth.PasskeyLoginFinishFull, request, cancellationToken);

    public Task<EntitlementResponse> GetMyEntitlementAsync(CancellationToken cancellationToken = default) =>
        SendAsync<EntitlementResponse>(HttpMethod.Get, ApiRoutes.Entitlements.MeFull, null, cancellationToken);

    public Task<ValidatePurchaseResponse> ValidatePurchaseAsync(ValidatePurchaseRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<ValidatePurchaseResponse>(HttpMethod.Post, ApiRoutes.Purchases.ValidateFull, request, cancellationToken);

    public Task<DiveSyncResponse> SyncDivesAsync(DiveSyncRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<DiveSyncResponse>(HttpMethod.Post, ApiRoutes.Dives.SyncFull, request, cancellationToken);

    public Task<SiteSearchResponse> SearchSitesAsync(string? query, double? lat, double? lng, int? limit, CancellationToken cancellationToken = default)
    {
        var parameters = new List<string>(4);
        if (!string.IsNullOrWhiteSpace(query))
        {
            parameters.Add($"query={Uri.EscapeDataString(query)}");
        }

        if (lat is not null)
        {
            parameters.Add($"lat={lat.Value.ToString(CultureInfo.InvariantCulture)}");
        }

        if (lng is not null)
        {
            parameters.Add($"lng={lng.Value.ToString(CultureInfo.InvariantCulture)}");
        }

        if (limit is not null)
        {
            parameters.Add($"limit={limit.Value.ToString(CultureInfo.InvariantCulture)}");
        }

        var route = parameters.Count == 0
            ? ApiRoutes.Sites.SearchFull
            : $"{ApiRoutes.Sites.SearchFull}?{string.Join('&', parameters)}";
        return SendAsync<SiteSearchResponse>(HttpMethod.Get, route, null, cancellationToken);
    }

    public Task<SiteDetailDto> GetSiteAsync(Guid id, CancellationToken cancellationToken = default) =>
        SendAsync<SiteDetailDto>(HttpMethod.Get, ApiRoutes.Sites.ByIdFull(id), null, cancellationToken);

    public Task<SiteContributionResponse> SubmitContributionAsync(SiteContributionRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<SiteContributionResponse>(HttpMethod.Post, ApiRoutes.Sites.ContributionsFull, request, cancellationToken);

    public Task<ModerationQueueResponse> GetModerationQueueAsync(string status, CancellationToken cancellationToken = default) =>
        SendAsync<ModerationQueueResponse>(
            HttpMethod.Get,
            $"{ApiRoutes.Moderation.ContributionsFull}?status={Uri.EscapeDataString(status)}",
            null,
            cancellationToken);

    public Task<ResolveContributionResponse> ResolveContributionAsync(Guid contributionId, ResolveContributionRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<ResolveContributionResponse>(HttpMethod.Post, ApiRoutes.Moderation.ResolveFull(contributionId), request, cancellationToken);

    public Task<AnalyticsBatchResponse> SendAnalyticsAsync(AnalyticsBatchRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<AnalyticsBatchResponse>(HttpMethod.Post, ApiRoutes.Analytics.EventsFull, request, cancellationToken);

    public Task<PrivacyDeleteResponse> DeleteMyDataAsync(CancellationToken cancellationToken = default) =>
        SendAsync<PrivacyDeleteResponse>(HttpMethod.Post, ApiRoutes.Privacy.DeleteFull, null, cancellationToken);

    private async Task<T> SendAsync<T>(HttpMethod method, string route, object? body, CancellationToken cancellationToken)
    {
        var token = await secureStore.GetAsync(SecureStoreKeys.AuthToken, cancellationToken).ConfigureAwait(false);

        HttpResponseMessage response;
        try
        {
            response = await SendOnceAsync(method, route, body, token, cancellationToken).ConfigureAwait(false);
            if (IsTransient(response.StatusCode))
            {
                response.Dispose();
                response = await SendOnceAsync(method, route, body, token, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (HttpRequestException)
        {
            // Retry once on network-level faults (DNS, connection reset, ...).
            response = await SendOnceAsync(method, route, body, token, cancellationToken).ConfigureAwait(false);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new ApiException(response.StatusCode, await ReadErrorAsync(response, cancellationToken).ConfigureAwait(false));
            }

            var payload = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken).ConfigureAwait(false);
            return payload ?? throw new ApiException(
                response.StatusCode,
                new ApiError("empty_body", $"The API returned an empty body for {method} {route}."));
        }
    }

    private async Task<HttpResponseMessage> SendOnceAsync(
        HttpMethod method,
        string route,
        object? body,
        string? token,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, route);
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        // Stitch server-emitted funnel events to the device funnel: every request carries the
        // client's analytics pseudo-id so ServerAnalytics.ResolvePseudoId uses it instead of the
        // account-id fallback.
        request.Headers.Add(TrimixHeaders.UserPseudoId, pseudoIds.UserPseudoId);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: JsonOptions);
        }

        return await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.RequestTimeout || (int)statusCode >= 500;

    /// <summary>Lenient error-body shapes: bespoke envelope and RFC 7807 problem details.</summary>
    private sealed record ErrorBody(string? Code, string? Message, string? Title, string? Detail);

    private static async Task<ApiError> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var fallback = new ApiError(
            ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture),
            $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}".TrimEnd());
        try
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorBody>(JsonOptions, cancellationToken).ConfigureAwait(false);
            if (body is null)
            {
                return fallback;
            }

            var code = body.Code ?? body.Title ?? fallback.Code;
            var message = body.Message ?? body.Detail ?? body.Title ?? fallback.Message;
            return new ApiError(code, message);
        }
        catch (JsonException)
        {
            return fallback;
        }
    }
}
