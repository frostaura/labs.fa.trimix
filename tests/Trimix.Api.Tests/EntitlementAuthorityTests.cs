using System.Net;
using Trimix.Contracts;
using Xunit;

namespace Trimix.Api.Tests;

/// <summary>
/// The server is the only entitlement authority (architecture overview §1, §3): the client cannot
/// self-grant, forged HMACs are rejected, and a receipt replayed by another account grants nothing.
/// </summary>
public sealed class EntitlementAuthorityTests : IDisposable
{
    private readonly TrimixApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Entitlements_require_authentication()
    {
        var anonymous = _factory.CreateClient();
        var me = await anonymous.GetAsync(ApiRoutes.Entitlements.MeFull);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);

        var validate = await anonymous.ValidatePurchaseAsync(ReceiptPlatforms.Sandbox, Skus.Pro, "whatever");
        Assert.Equal(HttpStatusCode.Unauthorized, validate.StatusCode);
    }

    [Fact]
    public async Task Garbage_token_is_rejected_and_grants_nothing()
    {
        var (client, _) = await _factory.RegisterAsync("forger@example.com");

        var response = await client.ValidatePurchaseAsync(ReceiptPlatforms.Sandbox, Skus.Pro, "not-a-receipt");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        Assert.Equal(EntitlementTier.Free, await client.GetTierAsync());
    }

    [Fact]
    public async Task Token_signed_with_wrong_secret_is_rejected()
    {
        var (client, _) = await _factory.RegisterAsync("tamper@example.com");

        var forged = SandboxReceiptCodec.Encode(
            new SandboxReceiptPayload(ReceiptPlatforms.Sandbox, Skus.Lifetime, "purchase-1", DateTime.UtcNow),
            "attacker-controlled-secret");
        var response = await client.ValidatePurchaseAsync(ReceiptPlatforms.Sandbox, Skus.Lifetime, forged);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(EntitlementTier.Free, await client.GetTierAsync());
    }

    [Fact]
    public async Task Token_for_a_different_sku_is_rejected()
    {
        var (client, _) = await _factory.RegisterAsync("skuswap@example.com");

        // Authentic Pro receipt presented as a Lifetime purchase: verification must fail.
        var proToken = SandboxReceiptCodec.Encode(
            new SandboxReceiptPayload(ReceiptPlatforms.Sandbox, Skus.Pro, "purchase-2", DateTime.UtcNow),
            TrimixApiFactory.SandboxSecret);
        var response = await client.ValidatePurchaseAsync(ReceiptPlatforms.Sandbox, Skus.Lifetime, proToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(EntitlementTier.Free, await client.GetTierAsync());
    }

    [Fact]
    public async Task Receipt_replayed_by_another_account_is_rejected()
    {
        var token = SandboxReceiptCodec.Encode(
            new SandboxReceiptPayload(ReceiptPlatforms.Sandbox, Skus.Pro, "purchase-owner", DateTime.UtcNow),
            TrimixApiFactory.SandboxSecret);

        var (owner, _) = await _factory.RegisterAsync("owner@example.com");
        var granted = await owner.ValidatePurchaseAsync(ReceiptPlatforms.Sandbox, Skus.Pro, token);
        granted.EnsureSuccessStatusCode();
        Assert.Equal(EntitlementTier.Pro, await owner.GetTierAsync());

        var (thief, _) = await _factory.RegisterAsync("thief@example.com");
        var replayed = await thief.ValidatePurchaseAsync(ReceiptPlatforms.Sandbox, Skus.Pro, token);
        Assert.Equal(HttpStatusCode.Conflict, replayed.StatusCode);
        Assert.Equal(EntitlementTier.Free, await thief.GetTierAsync());
    }

    [Fact]
    public async Task Store_platforms_are_explicit_not_configured_stubs()
    {
        var (client, _) = await _factory.RegisterAsync("stores@example.com");

        var play = await client.ValidatePurchaseAsync(ReceiptPlatforms.GooglePlay, Skus.Pro, "play-token");
        Assert.Equal(HttpStatusCode.NotImplemented, play.StatusCode);

        var appStore = await client.ValidatePurchaseAsync(ReceiptPlatforms.AppStore, Skus.Pro, "storekit-token");
        Assert.Equal(HttpStatusCode.NotImplemented, appStore.StatusCode);

        var unknown = await client.ValidatePurchaseAsync("steam", Skus.Pro, "steam-token");
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);

        Assert.Equal(EntitlementTier.Free, await client.GetTierAsync());
    }
}
