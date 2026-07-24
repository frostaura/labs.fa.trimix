using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Trimix.Contracts;

namespace Trimix.Api.Billing;

/// <summary>
/// Production App Store (<see cref="ReceiptPlatforms.AppStore"/>) receipt verifier (architecture
/// overview §3). The iOS client posts the base64 <c>AppStoreReceiptUrl</c> blob (a StoreKit&#160;1
/// bundle receipt), so this verifier validates it with Apple's classic <c>verifyReceipt</c> endpoint
/// (shared-secret; production first, with the documented <c>21007</c> sandbox fallback). All vendor
/// HTTP goes through an injected <see cref="IHttpClientFactory"/> so the verifier is unit-testable
/// against a mocked handler and never opens a socket in tests.
/// <para>
/// The server is the only entitlement authority: a receipt that fails verification grants nothing,
/// and the verifier <b>fails closed</b> — any transport error, non-2xx response, unparseable body,
/// bundle-id mismatch, or SKU mismatch maps to <see cref="ReceiptVerificationStatus.Invalid"/>
/// (never to Valid). Client restore is idempotent, so a transient outage costs at most a retry.
/// </para>
/// <para>
/// When no production shared secret is configured (<see cref="SharedSecretConfigKey"/> empty) the
/// verifier preserves the slice's dev/sandbox behavior exactly: it reconciles an
/// <see cref="SandboxReceiptCodec"/> HMAC token whose payload self-declares
/// <see cref="ReceiptPlatforms.AppStore"/>, and returns
/// <see cref="ReceiptVerificationStatus.NotConfigured"/> for an opaque real receipt (so the endpoint
/// still answers 501 until Apple credentials are provisioned).
/// </para>
/// </summary>
public sealed class AppleAppStoreReceiptVerifier(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    IHostEnvironment environment,
    ILogger<AppleAppStoreReceiptVerifier> logger) : IReceiptVerifier
{
    /// <summary>Named <see cref="System.Net.Http.HttpClient"/> the verifier resolves for verifyReceipt calls.</summary>
    public const string HttpClientName = "AppStoreReceiptVerifier";

    /// <summary>Apple shared secret (App Store Connect → App Information → App-Specific Shared Secret).</summary>
    public const string SharedSecretConfigKey = "Billing:AppStore:SharedSecret";

    /// <summary>Bundle id the receipt must belong to (guards against a receipt for a different app).</summary>
    public const string BundleIdConfigKey = "Billing:AppStore:BundleId";

    /// <summary>Override for Apple's production verifyReceipt URL.</summary>
    public const string ProductionUrlConfigKey = "Billing:AppStore:ProductionUrl";

    /// <summary>Override for Apple's sandbox verifyReceipt URL (used on the 21007 fallback).</summary>
    public const string SandboxUrlConfigKey = "Billing:AppStore:SandboxUrl";

    private const string DefaultProductionUrl = "https://buy.itunes.apple.com/verifyReceipt";
    private const string DefaultSandboxUrl = "https://sandbox.itunes.apple.com/verifyReceipt";

    /// <summary>verifyReceipt status: the receipt is valid.</summary>
    private const int StatusValid = 0;

    /// <summary>verifyReceipt status: a sandbox receipt was sent to production — retry against sandbox.</summary>
    private const int StatusSandboxReceiptSentToProduction = 21007;

    public string Platform => ReceiptPlatforms.AppStore;

    public async Task<ReceiptVerification> VerifyAsync(string skuId, string token, CancellationToken cancellationToken = default)
    {
        var sharedSecret = configuration[SharedSecretConfigKey];
        if (string.IsNullOrEmpty(sharedSecret))
        {
            // No Apple credentials provisioned: keep the slice's dev/sandbox reconciliation behavior,
            // and answer NotConfigured (⇒ endpoint 501) for a real opaque App Store receipt.
            return VerifyDevSandbox(skuId, token);
        }

        var bundleId = configuration[BundleIdConfigKey];
        var productionUrl = FirstNonEmpty(configuration[ProductionUrlConfigKey], DefaultProductionUrl);
        var sandboxUrl = FirstNonEmpty(configuration[SandboxUrlConfigKey], DefaultSandboxUrl);
        var client = httpClientFactory.CreateClient(HttpClientName);

        try
        {
            var result = await PostVerifyReceiptAsync(client, productionUrl, token, sharedSecret, cancellationToken);
            if (result.Status == StatusSandboxReceiptSentToProduction)
            {
                // Apple routes sandbox receipts to production during review; retry the sandbox endpoint.
                result = await PostVerifyReceiptAsync(client, sandboxUrl, token, sharedSecret, cancellationToken);
            }

            return Map(result, skuId, bundleId);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "App Store verifyReceipt returned an unparseable response; failing closed.");
            return ReceiptVerification.Invalid("App Store verifyReceipt returned an unparseable response.");
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            // Fail closed on outage/timeout/cancellation — never grant on an ambiguous outcome.
            logger.LogWarning(ex, "App Store verifyReceipt was unavailable; failing closed.");
            return ReceiptVerification.Invalid("App Store verifyReceipt unavailable");
        }
    }

    private ReceiptVerification Map(AppleReceiptResult result, string skuId, string? bundleId)
    {
        if (result.Status != StatusValid)
        {
            return ReceiptVerification.Invalid($"App Store verifyReceipt returned status {result.Status}.");
        }

        if (!string.Equals(result.BundleId, bundleId, StringComparison.Ordinal))
        {
            logger.LogWarning(
                "App Store receipt bundle id '{Actual}' did not match the configured bundle id '{Expected}'.",
                result.BundleId,
                bundleId);
            return ReceiptVerification.Invalid("App Store receipt bundle id did not match the configured app.");
        }

        if (!result.ProductIds.Contains(skuId, StringComparer.Ordinal))
        {
            return ReceiptVerification.Invalid("App Store receipt does not contain the requested SKU.");
        }

        // Non-consumables never expire, so a matching, authentic status:0 receipt grants.
        return ReceiptVerification.Valid;
    }

    private static async Task<AppleReceiptResult> PostVerifyReceiptAsync(
        HttpClient client,
        string url,
        string token,
        string sharedSecret,
        CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, object?>
        {
            ["receipt-data"] = token,
            ["password"] = sharedSecret,
            ["exclude-old-transactions"] = false,
        };

        using var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(url, content, cancellationToken);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseReceipt(payload);
    }

    private static AppleReceiptResult ParseReceipt(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        var status = root.TryGetProperty("status", out var statusElement)
            && statusElement.ValueKind == JsonValueKind.Number
                ? statusElement.GetInt32()
                : -1;

        string? bundleId = null;
        var productIds = new List<string>();
        if (root.TryGetProperty("receipt", out var receipt) && receipt.ValueKind == JsonValueKind.Object)
        {
            if (receipt.TryGetProperty("bundle_id", out var bundleElement)
                && bundleElement.ValueKind == JsonValueKind.String)
            {
                bundleId = bundleElement.GetString();
            }

            if (receipt.TryGetProperty("in_app", out var inApp) && inApp.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in inApp.EnumerateArray())
                {
                    if (item.TryGetProperty("product_id", out var productId)
                        && productId.ValueKind == JsonValueKind.String
                        && productId.GetString() is { } value)
                    {
                        productIds.Add(value);
                    }
                }
            }
        }

        return new AppleReceiptResult(status, bundleId, productIds);
    }

    /// <summary>
    /// Credentials-absent path: byte-identical to the pre-production stub. Reconciles a dev/sandbox
    /// HMAC token that self-declares the <c>app_store</c> platform, and returns NotConfigured for an
    /// opaque real receipt so the endpoint keeps answering 501 until Apple credentials are provisioned.
    /// </summary>
    private ReceiptVerification VerifyDevSandbox(string skuId, string token)
    {
        var secret = configuration[SandboxReceiptVerifier.SecretConfigKey];
        if (string.IsNullOrEmpty(secret))
        {
            return ReceiptVerification.NotConfigured(
                "App Store receipt verification is not configured in the vertical slice; use the sandbox platform.");
        }

        // Fail closed: the publicly known placeholder must never mint entitlements off dev machines.
        if (!environment.IsDevelopment()
            && string.Equals(secret, SandboxReceiptVerifier.CommittedPlaceholderSecret, StringComparison.Ordinal))
        {
            return ReceiptVerification.NotConfigured(
                "App Store sandbox reconciliation refuses the committed placeholder secret outside Development.");
        }

        if (!SandboxReceiptCodec.TryDecode(token, secret, out var payload))
        {
            // Not a dev/sandbox-shaped token — this is (or looks like) a real opaque App Store receipt,
            // which needs the production shared secret configured above.
            return ReceiptVerification.NotConfigured(
                "Production App Store receipt validation is not wired yet (no shared secret configured); only dev/sandbox-shaped app_store receipts reconcile.");
        }

        if (!string.Equals(payload.Platform, ReceiptPlatforms.AppStore, StringComparison.Ordinal))
        {
            return ReceiptVerification.Invalid("Receipt platform mismatch.");
        }

        if (!string.Equals(payload.SkuId, skuId, StringComparison.Ordinal))
        {
            return ReceiptVerification.Invalid("Receipt was issued for a different SKU.");
        }

        return ReceiptVerification.Valid;
    }

    private static string FirstNonEmpty(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

    /// <summary>The fields of a verifyReceipt response the verifier decides on.</summary>
    private sealed record AppleReceiptResult(int Status, string? BundleId, IReadOnlyList<string> ProductIds);
}
