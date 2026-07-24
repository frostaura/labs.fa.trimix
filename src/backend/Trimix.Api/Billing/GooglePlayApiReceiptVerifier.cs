using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Trimix.Contracts;

namespace Trimix.Api.Billing;

/// <summary>
/// Production Google Play (<see cref="ReceiptPlatforms.GooglePlay"/>) receipt verifier
/// (architecture overview §3). The Android client (App/Android <c>PlayBillingService</c>) posts the
/// opaque Play <b>purchase token</b> to <c>POST /purchases/validate</c>; this verifier validates it
/// against the Play Developer API:
/// <list type="number">
/// <item><description>Mint a short-lived OAuth2 access token from the configured service account
/// (RS256-signed JWT bearer grant), cached in-process across calls.</description></item>
/// <item><description>Call <c>purchases.products.get(packageName, productId, token)</c> and map
/// <c>purchaseState == 0</c> (Purchased) to <see cref="ReceiptVerificationStatus.Valid"/>.</description></item>
/// </list>
///
/// <para>
/// CONFIG-DRIVEN, FAIL-CLOSED. With no <c>Billing:GooglePlay:PackageName</c> or no service-account
/// credentials the verifier returns <see cref="ReceiptVerificationStatus.NotConfigured"/> (the endpoint
/// maps that to <c>501 NotImplemented</c>), so a dev host without Google credentials is unaffected and
/// the existing regression guard stays green. Every network/parse failure fails closed to
/// <see cref="ReceiptVerificationStatus.Invalid"/> — a receipt that cannot be proven authentic grants
/// nothing; client restore is idempotent and retries.
/// </para>
///
/// <para>
/// Acknowledgement is best-effort on the client (Play Billing acknowledges within its own SDK); the
/// server does NOT gate on <c>acknowledgementState</c> and never consumes the purchase — both SKUs are
/// non-consumables. Real credentials are deploy inputs (see docs/production-readiness.md); nothing is
/// hard-coded and shipped config defaults are empty.
/// </para>
/// </summary>
public sealed class GooglePlayApiReceiptVerifier(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<GooglePlayApiReceiptVerifier> logger) : IReceiptVerifier
{
    /// <summary>Named <see cref="HttpClient"/> for both the OAuth token and Play Developer API calls.</summary>
    public const string HttpClientName = "GooglePlayReceiptVerifier";

    /// <summary>Play application package name, e.g. <c>net.frostaura.trimix</c>.</summary>
    public const string PackageNameConfigKey = "Billing:GooglePlay:PackageName";

    /// <summary>Inline service-account JSON key (takes precedence over the path).</summary>
    public const string ServiceAccountJsonConfigKey = "Billing:GooglePlay:ServiceAccountJson";

    /// <summary>Filesystem path to the service-account JSON key.</summary>
    public const string ServiceAccountJsonPathConfigKey = "Billing:GooglePlay:ServiceAccountJsonPath";

    /// <summary>Override for the Play Developer API host (default <see cref="DefaultAndroidPublisherBaseUrl"/>).</summary>
    public const string AndroidPublisherBaseUrlConfigKey = "Billing:GooglePlay:AndroidPublisherBaseUrl";

    /// <summary>Override for the OAuth2 token endpoint (default: SA <c>token_uri</c> then <see cref="DefaultTokenUrl"/>).</summary>
    public const string TokenUrlConfigKey = "Billing:GooglePlay:TokenUrl";

    private const string DefaultAndroidPublisherBaseUrl = "https://androidpublisher.googleapis.com";
    private const string DefaultTokenUrl = "https://oauth2.googleapis.com/token";
    private const string AndroidPublisherScope = "https://www.googleapis.com/auth/androidpublisher";
    private const string JwtBearerGrantType = "urn:ietf:params:oauth:grant-type:jwt-bearer";

    // The verifier is a Singleton; the access token is shared across concurrent requests and refreshed
    // under this gate when it is within RefreshSkew of expiry.
    private static readonly TimeSpan RefreshSkew = TimeSpan.FromSeconds(60);
    private readonly SemaphoreSlim _tokenGate = new(1, 1);
    private string? _cachedAccessToken;
    private DateTimeOffset _accessTokenExpiresAt = DateTimeOffset.MinValue;

    public string Platform => ReceiptPlatforms.GooglePlay;

    public async Task<ReceiptVerification> VerifyAsync(
        string skuId,
        string token,
        CancellationToken cancellationToken = default)
    {
        var packageName = configuration[PackageNameConfigKey];
        if (string.IsNullOrWhiteSpace(packageName))
        {
            return ReceiptVerification.NotConfigured(
                "Google Play package name is not configured; use the sandbox platform.");
        }

        if (!TryResolveServiceAccount(out var serviceAccount, out var configError))
        {
            return ReceiptVerification.NotConfigured(configError);
        }

        try
        {
            var accessToken = await GetAccessTokenAsync(serviceAccount, cancellationToken);
            if (string.IsNullOrEmpty(accessToken))
            {
                // OAuth failed (fail closed): never grant off an unauthenticated Play API call.
                return ReceiptVerification.Invalid("Google Play access token could not be obtained.");
            }

            return await GetProductPurchaseAsync(packageName, skuId, token, accessToken, cancellationToken);
        }
        catch (OperationCanceledException ex)
        {
            // Timeout or caller cancellation — fail closed; the idempotent restore path retries.
            logger.LogWarning(ex, "Google Play receipt verification did not complete before cancellation.");
            return ReceiptVerification.Invalid("Google Play verification did not complete.");
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Google Play receipt verification failed: transport error.");
            return ReceiptVerification.Invalid("Google Play verification is unavailable.");
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Google Play receipt verification failed: malformed response JSON.");
            return ReceiptVerification.Invalid("Google Play returned a malformed response.");
        }
    }

    /// <summary>
    /// Resolves the service-account credentials from inline JSON (preferred) or a file path. Missing or
    /// unreadable credentials are a NotConfigured (not Invalid) state so a creds-absent dev host stays on
    /// the sandbox path; malformed present JSON is also NotConfigured (the platform simply is not wired).
    /// </summary>
    private bool TryResolveServiceAccount(out ServiceAccountCredentials credentials, out string error)
    {
        credentials = default!;

        string? json = configuration[ServiceAccountJsonConfigKey];
        if (string.IsNullOrWhiteSpace(json))
        {
            var path = configuration[ServiceAccountJsonPathConfigKey];
            if (!string.IsNullOrWhiteSpace(path))
            {
                try
                {
                    json = File.ReadAllText(path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
                {
                    logger.LogError(ex, "Google Play service-account key file could not be read.");
                    error = "Google Play service-account key file is unreadable.";
                    return false;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            error = "Google Play service-account credentials are not configured; use the sandbox platform.";
            return false;
        }

        ServiceAccountJson? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<ServiceAccountJson>(json);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Google Play service-account JSON is malformed.");
            error = "Google Play service-account JSON is malformed.";
            return false;
        }

        if (parsed is null
            || string.IsNullOrWhiteSpace(parsed.ClientEmail)
            || string.IsNullOrWhiteSpace(parsed.PrivateKey))
        {
            error = "Google Play service-account JSON is missing client_email or private_key.";
            return false;
        }

        credentials = new ServiceAccountCredentials(parsed.ClientEmail, parsed.PrivateKey, parsed.TokenUri);
        error = string.Empty;
        return true;
    }

    private async Task<string?> GetAccessTokenAsync(
        ServiceAccountCredentials credentials,
        CancellationToken cancellationToken)
    {
        await _tokenGate.WaitAsync(cancellationToken);
        try
        {
            if (_cachedAccessToken is not null && DateTimeOffset.UtcNow < _accessTokenExpiresAt - RefreshSkew)
            {
                return _cachedAccessToken;
            }

            var tokenUrl = FirstNonEmpty(
                configuration[TokenUrlConfigKey],
                credentials.TokenUri,
                DefaultTokenUrl);

            var assertion = BuildSignedJwtAssertion(credentials, tokenUrl);

            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = JwtBearerGrantType,
                ["assertion"] = assertion,
            });

            var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.PostAsync(tokenUrl, content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogError(
                    "Google Play OAuth token request failed with HTTP {StatusCode}.",
                    (int)response.StatusCode);
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<OAuthTokenResponse>(cancellationToken);
            if (payload is null || string.IsNullOrEmpty(payload.AccessToken))
            {
                logger.LogError("Google Play OAuth token response was empty.");
                return null;
            }

            var lifetime = payload.ExpiresIn > 0 ? payload.ExpiresIn : 3600;
            _cachedAccessToken = payload.AccessToken;
            _accessTokenExpiresAt = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(lifetime);
            return _cachedAccessToken;
        }
        finally
        {
            _tokenGate.Release();
        }
    }

    private async Task<ReceiptVerification> GetProductPurchaseAsync(
        string packageName,
        string skuId,
        string token,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var baseUrl = FirstNonEmpty(configuration[AndroidPublisherBaseUrlConfigKey], DefaultAndroidPublisherBaseUrl)
            .TrimEnd('/');

        var url =
            $"{baseUrl}/androidpublisher/v3/applications/{Uri.EscapeDataString(packageName)}" +
            $"/purchases/products/{Uri.EscapeDataString(skuId)}/tokens/{Uri.EscapeDataString(token)}";

        var client = httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await client.SendAsync(request, cancellationToken);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            // Service-account permission/misconfiguration — loud, and fail closed (never grant).
            logger.LogError(
                "Google Play purchases.products.get was refused with HTTP {StatusCode}: check the " +
                "service account's Play Console access to package '{PackageName}'.",
                (int)response.StatusCode,
                packageName);
            return ReceiptVerification.Invalid("Google Play purchase verification was refused (service-account permissions).");
        }

        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone or HttpStatusCode.BadRequest)
        {
            return ReceiptVerification.Invalid("Google Play purchase token was not found or is no longer valid.");
        }

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("Google Play purchases.products.get failed with HTTP {StatusCode}.", (int)response.StatusCode);
            return ReceiptVerification.Invalid("Google Play purchase verification is unavailable.");
        }

        var purchase = await response.Content.ReadFromJsonAsync<ProductPurchase>(cancellationToken);
        if (purchase is null)
        {
            return ReceiptVerification.Invalid("Google Play returned an empty purchase record.");
        }

        // purchaseState: 0 = Purchased, 1 = Canceled, 2 = Pending (deferred). Only 0 grants.
        return purchase.PurchaseState switch
        {
            0 => ReceiptVerification.Valid,
            1 => ReceiptVerification.Invalid("Google Play purchase was canceled."),
            2 => ReceiptVerification.Invalid("Google Play purchase is pending."),
            _ => ReceiptVerification.Invalid($"Google Play purchase state {purchase.PurchaseState} is not purchased."),
        };
    }

    /// <summary>
    /// Builds an RS256-signed JWT bearer assertion for the Google OAuth2 token endpoint. Hand-rolled with
    /// <see cref="RSA"/> to keep the API dependency-free (no Google.Apis SDK); the private key is the
    /// service account's PKCS#8 PEM.
    /// </summary>
    private static string BuildSignedJwtAssertion(ServiceAccountCredentials credentials, string audience)
    {
        var issuedAt = DateTimeOffset.UtcNow;
        var header = new { alg = "RS256", typ = "JWT" };
        var claims = new
        {
            iss = credentials.ClientEmail,
            scope = AndroidPublisherScope,
            aud = audience,
            iat = issuedAt.ToUnixTimeSeconds(),
            exp = issuedAt.AddHours(1).ToUnixTimeSeconds(),
        };

        var signingInput =
            $"{Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(header))}." +
            $"{Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(claims))}";

        using var rsa = RSA.Create();
        rsa.ImportFromPem(credentials.PrivateKey);
        var signature = rsa.SignData(
            Encoding.ASCII.GetBytes(signingInput),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return $"{signingInput}.{Base64UrlEncode(signature)}";
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string FirstNonEmpty(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate;
            }
        }

        return string.Empty;
    }

    /// <summary>Validated, non-null service-account fields used for signing and the token request.</summary>
    private sealed record ServiceAccountCredentials(string ClientEmail, string PrivateKey, string? TokenUri);

    private sealed class ServiceAccountJson
    {
        [JsonPropertyName("client_email")]
        public string? ClientEmail { get; set; }

        [JsonPropertyName("private_key")]
        public string? PrivateKey { get; set; }

        [JsonPropertyName("token_uri")]
        public string? TokenUri { get; set; }
    }

    private sealed class OAuthTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; set; }
    }

    private sealed class ProductPurchase
    {
        [JsonPropertyName("purchaseState")]
        public int PurchaseState { get; set; }

        [JsonPropertyName("consumptionState")]
        public int ConsumptionState { get; set; }

        [JsonPropertyName("acknowledgementState")]
        public int AcknowledgementState { get; set; }

        [JsonPropertyName("orderId")]
        public string? OrderId { get; set; }
    }
}
