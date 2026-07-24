using Trimix.Contracts;
using Trimix.Core.Billing;
using Trimix.Core.Platform;
using Xunit;

namespace Trimix.Core.Tests;

/// <summary>SandboxBillingService: deterministic purchases/restore roundtripping through SandboxReceiptCodec.</summary>
public class SandboxBillingServiceTests
{
    private static SandboxBillingOptions Options(params string[] owned)
    {
        var options = new SandboxBillingOptions();
        foreach (var sku in owned)
        {
            options.InitiallyOwnedSkuIds.Add(sku);
        }

        return options;
    }

    [Fact]
    public async Task Purchase_produces_a_token_the_codec_validates()
    {
        var options = Options();
        var billing = new SandboxBillingService(options);

        var receipt = await billing.PurchaseAsync(Skus.Pro);

        Assert.Equal(ReceiptPlatforms.Sandbox, receipt.Platform);
        Assert.Equal(Skus.Pro, receipt.SkuId);
        Assert.True(SandboxReceiptCodec.TryDecode(receipt.Token, options.Secret, out var payload));
        Assert.Equal(ReceiptPlatforms.Sandbox, payload!.Platform);
        Assert.Equal(Skus.Pro, payload.SkuId);
        Assert.StartsWith("sandbox-", payload.PurchaseId);
    }

    [Fact]
    public async Task Tampered_or_wrong_secret_tokens_fail_validation()
    {
        var options = Options();
        var billing = new SandboxBillingService(options);
        var receipt = await billing.PurchaseAsync(Skus.Pro);

        Assert.False(SandboxReceiptCodec.TryDecode(receipt.Token, "some-other-secret", out _));
        Assert.False(SandboxReceiptCodec.TryDecode(receipt.Token + "a", options.Secret, out _));
        Assert.False(SandboxReceiptCodec.TryDecode("garbage", options.Secret, out _));
    }

    [Fact]
    public async Task Restore_reproduces_the_identical_token_across_instances()
    {
        // Two service instances = app reinstall. Same account+sku ⇒ byte-identical token, which is
        // what lets the server dedupe the receipt and answer "restored" instead of granting twice.
        var first = new SandboxBillingService(Options(Skus.Lifetime));
        var second = new SandboxBillingService(Options(Skus.Lifetime));

        var restoredFirst = Assert.Single(await first.RestoreAsync());
        var restoredSecond = Assert.Single(await second.RestoreAsync());

        Assert.Equal(restoredFirst.Token, restoredSecond.Token);
    }

    [Fact]
    public async Task Purchase_then_restore_returns_the_same_receipt()
    {
        var billing = new SandboxBillingService(Options());
        var purchased = await billing.PurchaseAsync(Skus.Lifetime);

        var restored = Assert.Single(await billing.RestoreAsync());

        Assert.Equal(purchased.Token, restored.Token);
    }

    [Fact]
    public async Task User_cancel_hook_throws_and_resets()
    {
        var billing = new SandboxBillingService(Options()) { SimulateUserCancelOnNextPurchase = true };

        var ex = await Assert.ThrowsAsync<PurchaseCancelledException>(() => billing.PurchaseAsync(Skus.Pro));
        Assert.Equal(Skus.Pro, ex.SkuId);

        // The cancelled purchase is not owned…
        Assert.Empty(await billing.RestoreAsync());
        // …and the hook resets: the next attempt succeeds.
        var receipt = await billing.PurchaseAsync(Skus.Pro);
        Assert.Equal(Skus.Pro, receipt.SkuId);
    }

    [Fact]
    public async Task Owned_skus_persist_across_process_restart_via_preferences()
    {
        // Same preferences store = same device across an app restart/reinstall. A purchase on the
        // first instance must be restorable on the second so entitlement_restored can fire on device.
        var preferences = new InMemoryPreferences();
        var options = Options();

        var first = new SandboxBillingService(options, preferences);
        await first.PurchaseAsync(Skus.Pro);

        var second = new SandboxBillingService(Options(), preferences); // fresh in-memory ownership set
        var restored = Assert.Single(await second.RestoreAsync());
        Assert.Equal(Skus.Pro, restored.SkuId);
    }

    [Fact]
    public async Task Unknown_sku_is_rejected()
    {
        var billing = new SandboxBillingService(Options());
        await Assert.ThrowsAsync<ArgumentException>(() => billing.PurchaseAsync("trimix.subscription"));
    }

    [Fact]
    public async Task Query_products_returns_the_locked_ladder()
    {
        var billing = new SandboxBillingService(Options());
        var products = await billing.QueryProductsAsync([.. Skus.All.Select(s => s.Id)]);

        Assert.Equal(2, products.Count);
        Assert.Equal("$14.99", products.Single(p => p.SkuId == Skus.Pro).FormattedPrice);
        Assert.Equal("$29.99", products.Single(p => p.SkuId == Skus.Lifetime).FormattedPrice);
        Assert.All(products, p => Assert.Equal("USD", p.CurrencyCode));
    }
}
