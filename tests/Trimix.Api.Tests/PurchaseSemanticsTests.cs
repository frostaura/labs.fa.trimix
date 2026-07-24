using System.Net.Http.Json;
using System.Text.Json;
using Trimix.Contracts;
using Trimix.Contracts.Dtos;
using Xunit;

namespace Trimix.Api.Tests;

/// <summary>
/// Grant vs restore semantics (architecture overview §3): first-time receipt → granted +
/// <c>entitlement_granted</c>; any subsequent reconciliation of the same receipt → restored +
/// <c>entitlement_restored</c>. Lifetime supersedes Pro; restores never downgrade.
/// </summary>
public sealed class PurchaseSemanticsTests : IDisposable
{
    private readonly TrimixApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private static string Receipt(string skuId, string purchaseId) =>
        SandboxReceiptCodec.Encode(
            new SandboxReceiptPayload(ReceiptPlatforms.Sandbox, skuId, purchaseId, DateTime.UtcNow),
            TrimixApiFactory.SandboxSecret);

    [Fact]
    public async Task First_receipt_grants_then_same_receipt_restores()
    {
        var pseudo = "pseudo-grant-restore";
        var (client, _) = await _factory.RegisterAsync("buyer@example.com", pseudo);
        var token = Receipt(Skus.Pro, "purchase-pro-1");

        var first = await client.ValidatePurchaseAsync(ReceiptPlatforms.Sandbox, Skus.Pro, token);
        first.EnsureSuccessStatusCode();
        var firstResult = await first.Content.ReadFromJsonAsync<ValidatePurchaseResponse>();
        Assert.NotNull(firstResult);
        Assert.Equal(EntitlementTier.Pro, firstResult.Tier);
        Assert.Equal(PurchaseValidationOutcome.Granted, firstResult.Outcome);

        var second = await client.ValidatePurchaseAsync(ReceiptPlatforms.Sandbox, Skus.Pro, token);
        second.EnsureSuccessStatusCode();
        var secondResult = await second.Content.ReadFromJsonAsync<ValidatePurchaseResponse>();
        Assert.NotNull(secondResult);
        Assert.Equal(EntitlementTier.Pro, secondResult.Tier);
        Assert.Equal(PurchaseValidationOutcome.Restored, secondResult.Outcome);

        Assert.Equal(EntitlementTier.Pro, await client.GetTierAsync());

        // Server-emitted rows: exactly one grant then one restore, attributed to the buyer's pseudo-id.
        var names = _factory.EventNamesFor(pseudo);
        Assert.Equal(1, names.Count(n => n == FunnelEvents.EntitlementGranted));
        Assert.Equal(1, names.Count(n => n == FunnelEvents.EntitlementRestored));
        Assert.True(
            names.ToList().IndexOf(FunnelEvents.EntitlementGranted) < names.ToList().IndexOf(FunnelEvents.EntitlementRestored),
            "grant must precede restore");
    }

    [Fact]
    public async Task Lifetime_supersedes_pro_and_restoring_pro_never_downgrades()
    {
        var (client, _) = await _factory.RegisterAsync("upgrader@example.com");
        var proToken = Receipt(Skus.Pro, "purchase-pro-2");
        var lifetimeToken = Receipt(Skus.Lifetime, "purchase-lifetime-1");

        (await client.ValidatePurchaseAsync(ReceiptPlatforms.Sandbox, Skus.Pro, proToken)).EnsureSuccessStatusCode();
        Assert.Equal(EntitlementTier.Pro, await client.GetTierAsync());

        var upgrade = await client.ValidatePurchaseAsync(ReceiptPlatforms.Sandbox, Skus.Lifetime, lifetimeToken);
        upgrade.EnsureSuccessStatusCode();
        var upgradeResult = await upgrade.Content.ReadFromJsonAsync<ValidatePurchaseResponse>();
        Assert.NotNull(upgradeResult);
        Assert.Equal(EntitlementTier.Lifetime, upgradeResult.Tier);
        Assert.Equal(PurchaseValidationOutcome.Granted, upgradeResult.Outcome);

        // Restoring the lesser Pro receipt afterwards must NOT downgrade the Lifetime entitlement.
        var restorePro = await client.ValidatePurchaseAsync(ReceiptPlatforms.Sandbox, Skus.Pro, proToken);
        restorePro.EnsureSuccessStatusCode();
        var restoreResult = await restorePro.Content.ReadFromJsonAsync<ValidatePurchaseResponse>();
        Assert.NotNull(restoreResult);
        Assert.Equal(PurchaseValidationOutcome.Restored, restoreResult.Outcome);
        Assert.Equal(EntitlementTier.Lifetime, restoreResult.Tier);

        Assert.Equal(EntitlementTier.Lifetime, await client.GetTierAsync());
    }

    [Fact]
    public async Task Entitlement_granted_reports_the_tier_the_sku_grants_not_the_resulting_tier()
    {
        var pseudo = "pseudo-tier-attribution";
        var (client, _) = await _factory.RegisterAsync("multistore@example.com", pseudo);

        // Already holds Lifetime.
        (await client.ValidatePurchaseAsync(ReceiptPlatforms.Sandbox, Skus.Lifetime, Receipt(Skus.Lifetime, "lt-1")))
            .EnsureSuccessStatusCode();

        // A brand-new Pro receipt (e.g. bought Pro on a second store account) → first-seen → Granted,
        // but the entitlement stays Lifetime. The grant event must attribute the Pro tier.
        var proGrant = await client.ValidatePurchaseAsync(ReceiptPlatforms.Sandbox, Skus.Pro, Receipt(Skus.Pro, "pro-1"));
        proGrant.EnsureSuccessStatusCode();
        var proResult = await proGrant.Content.ReadFromJsonAsync<ValidatePurchaseResponse>();
        Assert.Equal(PurchaseValidationOutcome.Granted, proResult!.Outcome);
        Assert.Equal(EntitlementTier.Lifetime, proResult.Tier); // entitlement unchanged

        var grantedTiers = _factory.WithDb(db => db.AnalyticsEvents
            .Where(e => e.UserPseudoId == pseudo && e.Name == FunnelEvents.EntitlementGranted)
            .Select(e => e.PropsJson)
            .ToList())
            .Select(json => JsonDocument.Parse(json).RootElement.GetProperty(FunnelEvents.Props.Tier).GetString())
            .ToList();

        Assert.Contains("lifetime", grantedTiers);
        Assert.Contains("pro", grantedTiers); // regression: reported 'lifetime' (the resulting tier)
    }
}
