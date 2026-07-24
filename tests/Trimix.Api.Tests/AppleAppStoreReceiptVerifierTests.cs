using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Trimix.Api.Billing;
using Trimix.Contracts;
using Xunit;

namespace Trimix.Api.Tests;

/// <summary>
/// Unit tests for <see cref="AppleAppStoreReceiptVerifier"/>. All Apple HTTP is served by a mocked
/// <see cref="HttpMessageHandler"/> with recorded verifyReceipt JSON — no socket is ever opened, so
/// these run offline and hermetically. Covers the configured production path, the 21007 sandbox
/// fallback, fail-closed mappings, and the credentials-absent dev/sandbox reconciliation that keeps
/// the endpoint answering 501 for opaque receipts.
/// </summary>
public sealed class AppleAppStoreReceiptVerifierTests
{
    private const string AppStoreSharedSecret = "apple-shared-secret-test-value";
    private const string AppStoreBundleId = "net.frostaura.trimix";
    private const string ProductionUrl = "https://buy.itunes.apple.com/verifyReceipt";
    private const string SandboxUrl = "https://sandbox.itunes.apple.com/verifyReceipt";

    // A non-placeholder sandbox HMAC secret so the dev/sandbox reconciliation path is allowed to grant.
    private const string SandboxSecret = "apple-verifier-test-sandbox-secret";

    private const string ValidReceiptJson =
        """{"status":0,"receipt":{"bundle_id":"net.frostaura.trimix","in_app":[{"product_id":"trimix.pro"}]}}""";

    private const string WrongBundleReceiptJson =
        """{"status":0,"receipt":{"bundle_id":"com.someone.else","in_app":[{"product_id":"trimix.pro"}]}}""";

    private static string StatusOnlyJson(int status) => $$"""{"status":{{status}}}""";

    [Fact]
    public async Task Configured_valid_production_receipt_is_valid()
    {
        var handler = new StubHttpMessageHandler(_ => (HttpStatusCode.OK, ValidReceiptJson));
        var verifier = BuildVerifier(ConfiguredSettings(), handler);

        var result = await verifier.VerifyAsync(Skus.Pro, "app-receipt-blob");

        Assert.Equal(ReceiptVerificationStatus.Valid, result.Status);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(ProductionUrl, request.Url);
        Assert.Contains("\"receipt-data\":\"app-receipt-blob\"", request.Body, StringComparison.Ordinal);
        Assert.Contains(AppStoreSharedSecret, request.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sandbox_receipt_sent_to_production_falls_back_to_sandbox()
    {
        var handler = new StubHttpMessageHandler(request =>
            request.RequestUri!.AbsoluteUri.Contains("sandbox", StringComparison.Ordinal)
                ? (HttpStatusCode.OK, ValidReceiptJson)
                : (HttpStatusCode.OK, StatusOnlyJson(21007)));
        var verifier = BuildVerifier(ConfiguredSettings(), handler);

        var result = await verifier.VerifyAsync(Skus.Pro, "sandbox-receipt-blob");

        Assert.Equal(ReceiptVerificationStatus.Valid, result.Status);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(ProductionUrl, handler.Requests[0].Url);
        Assert.Equal(SandboxUrl, handler.Requests[1].Url);
    }

    [Fact]
    public async Task Configured_malformed_status_is_invalid()
    {
        var handler = new StubHttpMessageHandler(_ => (HttpStatusCode.OK, StatusOnlyJson(21002)));
        var verifier = BuildVerifier(ConfiguredSettings(), handler);

        var result = await verifier.VerifyAsync(Skus.Pro, "app-receipt-blob");

        Assert.Equal(ReceiptVerificationStatus.Invalid, result.Status);
    }

    [Fact]
    public async Task Configured_wrong_bundle_id_is_invalid()
    {
        var handler = new StubHttpMessageHandler(_ => (HttpStatusCode.OK, WrongBundleReceiptJson));
        var verifier = BuildVerifier(ConfiguredSettings(), handler);

        var result = await verifier.VerifyAsync(Skus.Pro, "app-receipt-blob");

        Assert.Equal(ReceiptVerificationStatus.Invalid, result.Status);
    }

    [Fact]
    public async Task Configured_receipt_missing_requested_sku_is_invalid()
    {
        // ValidReceiptJson only contains trimix.pro; requesting Lifetime must not grant.
        var handler = new StubHttpMessageHandler(_ => (HttpStatusCode.OK, ValidReceiptJson));
        var verifier = BuildVerifier(ConfiguredSettings(), handler);

        var result = await verifier.VerifyAsync(Skus.Lifetime, "app-receipt-blob");

        Assert.Equal(ReceiptVerificationStatus.Invalid, result.Status);
    }

    [Fact]
    public async Task Configured_http_failure_fails_closed_as_invalid()
    {
        var handler = new StubHttpMessageHandler(_ => (HttpStatusCode.ServiceUnavailable, "{}"));
        var verifier = BuildVerifier(ConfiguredSettings(), handler);

        var result = await verifier.VerifyAsync(Skus.Pro, "app-receipt-blob");

        Assert.Equal(ReceiptVerificationStatus.Invalid, result.Status);
    }

    [Fact]
    public async Task Not_configured_opaque_receipt_is_not_configured()
    {
        // No Apple shared secret; opaque token is not a sandbox codec token ⇒ NotConfigured (endpoint 501).
        var handler = new StubHttpMessageHandler(_ => (HttpStatusCode.OK, "{}"));
        var settings = new Dictionary<string, string?> { ["Billing:SandboxSecret"] = SandboxSecret };
        var verifier = BuildVerifier(settings, handler);

        var result = await verifier.VerifyAsync(Skus.Pro, "storekit-token");

        Assert.Equal(ReceiptVerificationStatus.NotConfigured, result.Status);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Not_configured_dev_sandbox_token_reconciles_to_valid()
    {
        var handler = new StubHttpMessageHandler(_ => (HttpStatusCode.OK, "{}"));
        var settings = new Dictionary<string, string?> { ["Billing:SandboxSecret"] = SandboxSecret };
        var verifier = BuildVerifier(settings, handler);

        var token = SandboxReceiptCodec.Encode(
            new SandboxReceiptPayload(ReceiptPlatforms.AppStore, Skus.Pro, "purchase-dev", DateTime.UtcNow),
            SandboxSecret);

        var result = await verifier.VerifyAsync(Skus.Pro, token);

        Assert.Equal(ReceiptVerificationStatus.Valid, result.Status);
        Assert.Empty(handler.Requests);
    }

    private static Dictionary<string, string?> ConfiguredSettings() => new()
    {
        [AppleAppStoreReceiptVerifier.SharedSecretConfigKey] = AppStoreSharedSecret,
        [AppleAppStoreReceiptVerifier.BundleIdConfigKey] = AppStoreBundleId,
        [AppleAppStoreReceiptVerifier.ProductionUrlConfigKey] = ProductionUrl,
        [AppleAppStoreReceiptVerifier.SandboxUrlConfigKey] = SandboxUrl,
    };

    private static AppleAppStoreReceiptVerifier BuildVerifier(
        IDictionary<string, string?> settings,
        HttpMessageHandler handler,
        string environmentName = "Development")
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var environment = new StubHostEnvironment { EnvironmentName = environmentName };
        return new AppleAppStoreReceiptVerifier(
            new SingleClientFactory(handler),
            configuration,
            environment,
            NullLogger<AppleAppStoreReceiptVerifier>.Instance);
    }

    /// <summary>Records each request (URL + body) and returns a canned status + JSON body.</summary>
    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, (HttpStatusCode Status, string Body)> responder)
        : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(request.RequestUri!.AbsoluteUri, body));

            var (status, responseBody) = responder(request);
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed record RecordedRequest(string Url, string Body);

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StubHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Trimix.Api.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
