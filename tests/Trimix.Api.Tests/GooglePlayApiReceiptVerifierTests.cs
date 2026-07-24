using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Trimix.Api.Billing;
using Trimix.Contracts;
using Xunit;

namespace Trimix.Api.Tests;

/// <summary>
/// Unit tests for <see cref="GooglePlayApiReceiptVerifier"/>. All Play Developer API / OAuth traffic is
/// served by a mocked <see cref="HttpMessageHandler"/> with recorded Google JSON — no real Google
/// servers are contacted. The service-account fixture carries a real throwaway RSA key so the RS256 JWT
/// assertion is signed for real, offline (the mocked token endpoint does not validate it).
/// </summary>
public sealed class GooglePlayApiReceiptVerifierTests
{
    private const string PackageName = "net.frostaura.trimix";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string ProductToken = "play-purchase-token-abc123";
    private const string AccessToken = "ya29.mock-access-token";

    [Fact]
    public async Task Configured_purchased_token_is_valid_with_bearer_auth_and_correct_url()
    {
        var handler = new StubHttpMessageHandler(request =>
            IsTokenRequest(request)
                ? Json(HttpStatusCode.OK, $$"""{"access_token":"{{AccessToken}}","expires_in":3600,"token_type":"Bearer"}""")
                : Json(HttpStatusCode.OK, """{"purchaseState":0,"acknowledgementState":1,"orderId":"GPA.1234-5678-9012-34567"}"""));

        var verifier = CreateVerifier(handler, Configured());

        var result = await verifier.VerifyAsync(Skus.Pro, ProductToken);

        Assert.Equal(ReceiptVerificationStatus.Valid, result.Status);

        // The product request carried the bearer token minted at the token endpoint.
        Assert.Contains(
            handler.AuthorizationHeaders,
            h => h is { Scheme: "Bearer" } && h.Parameter == AccessToken);

        // The product URL addresses the configured package, SKU, and purchase token.
        var productUrl = Assert.Single(handler.RequestUris, u => u.Contains("/androidpublisher/"));
        Assert.Contains(PackageName, productUrl);
        Assert.Contains(Skus.Pro, productUrl);
        Assert.Contains(ProductToken, productUrl);
    }

    [Fact]
    public async Task Canceled_purchase_state_is_invalid()
    {
        var verifier = CreateVerifier(
            ProductHandler("""{"purchaseState":1,"acknowledgementState":0}"""),
            Configured());

        var result = await verifier.VerifyAsync(Skus.Pro, ProductToken);

        Assert.Equal(ReceiptVerificationStatus.Invalid, result.Status);
    }

    [Fact]
    public async Task Pending_purchase_state_is_invalid()
    {
        var verifier = CreateVerifier(
            ProductHandler("""{"purchaseState":2,"acknowledgementState":0}"""),
            Configured());

        var result = await verifier.VerifyAsync(Skus.Pro, ProductToken);

        Assert.Equal(ReceiptVerificationStatus.Invalid, result.Status);
    }

    [Fact]
    public async Task Not_found_purchase_is_invalid()
    {
        var handler = new StubHttpMessageHandler(request =>
            IsTokenRequest(request)
                ? Json(HttpStatusCode.OK, $$"""{"access_token":"{{AccessToken}}","expires_in":3600}""")
                : new HttpResponseMessage(HttpStatusCode.NotFound));

        var verifier = CreateVerifier(handler, Configured());

        var result = await verifier.VerifyAsync(Skus.Pro, ProductToken);

        Assert.Equal(ReceiptVerificationStatus.Invalid, result.Status);
    }

    [Fact]
    public async Task Missing_package_name_is_not_configured()
    {
        // Service account present, but no package name → the platform is simply not wired.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [GooglePlayApiReceiptVerifier.ServiceAccountJsonConfigKey] = ServiceAccountJson(),
            })
            .Build();

        var verifier = CreateVerifier(ProductHandler("""{"purchaseState":0}"""), config);

        var result = await verifier.VerifyAsync(Skus.Pro, ProductToken);

        Assert.Equal(ReceiptVerificationStatus.NotConfigured, result.Status);
    }

    [Fact]
    public async Task Missing_service_account_is_not_configured()
    {
        // Package present, but no service-account credentials → not wired (mirrors the endpoint's 501 guard).
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [GooglePlayApiReceiptVerifier.PackageNameConfigKey] = PackageName,
            })
            .Build();

        var verifier = CreateVerifier(ProductHandler("""{"purchaseState":0}"""), config);

        var result = await verifier.VerifyAsync(Skus.Pro, ProductToken);

        Assert.Equal(ReceiptVerificationStatus.NotConfigured, result.Status);
    }

    [Fact]
    public async Task Access_token_is_cached_across_calls()
    {
        var handler = new StubHttpMessageHandler(request =>
            IsTokenRequest(request)
                ? Json(HttpStatusCode.OK, $$"""{"access_token":"{{AccessToken}}","expires_in":3600,"token_type":"Bearer"}""")
                : Json(HttpStatusCode.OK, """{"purchaseState":0,"acknowledgementState":1}"""));

        var verifier = CreateVerifier(handler, Configured());

        var first = await verifier.VerifyAsync(Skus.Pro, ProductToken);
        var second = await verifier.VerifyAsync(Skus.Pro, "second-token");

        Assert.Equal(ReceiptVerificationStatus.Valid, first.Status);
        Assert.Equal(ReceiptVerificationStatus.Valid, second.Status);

        // The (non-expired) access token is fetched exactly once, then reused for the second call.
        Assert.Equal(1, handler.TokenCalls);
        Assert.Equal(2, handler.ProductCalls);
    }

    // --- fixtures -----------------------------------------------------------------------------------

    private static GooglePlayApiReceiptVerifier CreateVerifier(
        StubHttpMessageHandler handler,
        IConfiguration configuration) =>
        new(new SingleClientFactory(handler), configuration, NullLogger<GooglePlayApiReceiptVerifier>.Instance);

    private static IConfiguration Configured() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [GooglePlayApiReceiptVerifier.PackageNameConfigKey] = PackageName,
                [GooglePlayApiReceiptVerifier.ServiceAccountJsonConfigKey] = ServiceAccountJson(),
            })
            .Build();

    /// <summary>A handler that answers the token endpoint with a canned access token and the product
    /// endpoint with <paramref name="productJson"/>.</summary>
    private static StubHttpMessageHandler ProductHandler(string productJson) =>
        new(request =>
            IsTokenRequest(request)
                ? Json(HttpStatusCode.OK, $$"""{"access_token":"{{AccessToken}}","expires_in":3600}""")
                : Json(HttpStatusCode.OK, productJson));

    private static bool IsTokenRequest(HttpRequestMessage request) =>
        request.RequestUri!.AbsoluteUri.StartsWith(TokenEndpoint, StringComparison.Ordinal);

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    /// <summary>Fake service-account JSON with a real throwaway RSA private key (so signing runs offline).</summary>
    private static string ServiceAccountJson()
    {
        using var rsa = RSA.Create(2048);
        var pem = rsa.ExportPkcs8PrivateKeyPem();
        return JsonSerializer.Serialize(new
        {
            type = "service_account",
            client_email = "trimix-verifier@trimix.iam.gserviceaccount.com",
            private_key = pem,
            token_uri = TokenEndpoint,
        });
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        public List<string> RequestUris { get; } = [];
        public List<AuthenticationHeaderValue?> AuthorizationHeaders { get; } = [];
        public int TokenCalls { get; private set; }
        public int ProductCalls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!.AbsoluteUri;
            RequestUris.Add(uri);
            AuthorizationHeaders.Add(request.Headers.Authorization);

            if (IsTokenRequest(request))
            {
                TokenCalls++;
            }
            else if (uri.Contains("/androidpublisher/", StringComparison.Ordinal))
            {
                ProductCalls++;
            }

            return Task.FromResult(responder(request));
        }
    }
}
