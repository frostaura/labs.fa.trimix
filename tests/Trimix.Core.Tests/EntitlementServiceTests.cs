using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Trimix.Contracts;
using Trimix.Contracts.Dtos;
using Trimix.Core.Abstractions;
using Trimix.Core.Api;
using Trimix.Core.Billing;
using Trimix.Core.Entitlements;
using Trimix.Core.Platform;
using Trimix.Core.Tests.Fakes;
using Xunit;

namespace Trimix.Core.Tests;

/// <summary>
/// Server-authoritative entitlement cache: launch cache load, offline tolerance, and the
/// restore-on-reinstall path (fresh install + sign-in → auto-restore → server tier wins).
/// </summary>
public class EntitlementServiceTests
{
    private readonly FakeApiClient _api = new();
    private readonly InMemorySecureStore _secureStore = new();
    private readonly InMemoryPreferences _preferences = new();
    private readonly FakeAuthSession _session = new();
    private readonly PendingPurchaseStore _pending;

    public EntitlementServiceTests() => _pending = new PendingPurchaseStore(_preferences);

    private EntitlementService CreateService(SandboxBillingService billing) => new(
        _api,
        billing,
        _secureStore,
        _preferences,
        _pending,
        _session,
        NullLogger<EntitlementService>.Instance);

    /// <summary>Server whose entitlement answer flips once a receipt has been validated.</summary>
    private void ConfigureServerSide(EntitlementTier tierAfterValidate)
    {
        _api.EntitlementHandler = () => new EntitlementResponse(
            _api.ValidatePurchaseRequests.Count > 0 ? tierAfterValidate : EntitlementTier.Free);
        _api.ValidatePurchaseHandler = _ => new ValidatePurchaseResponse(tierAfterValidate, PurchaseValidationOutcome.Restored);
    }

    private static SandboxBillingService BillingOwning(params string[] skus)
    {
        var options = new SandboxBillingOptions();
        foreach (var sku in skus)
        {
            options.InitiallyOwnedSkuIds.Add(sku);
        }

        return new SandboxBillingService(options);
    }

    [Fact]
    public async Task Fresh_install_sign_in_auto_restores_and_adopts_server_tier()
    {
        // Reinstall scenario: empty secure store + preferences, store account owns Lifetime.
        ConfigureServerSide(EntitlementTier.Lifetime);
        var service = CreateService(BillingOwning(Skus.Lifetime));
        var tierChanges = new List<EntitlementTier>();
        service.TierChanged += (_, tier) => tierChanges.Add(tier);

        await service.OnSignedInAsync();

        var validated = Assert.Single(_api.ValidatePurchaseRequests);
        Assert.Equal(ReceiptPlatforms.Sandbox, validated.Platform);
        Assert.Equal(Skus.Lifetime, validated.SkuId);
        Assert.True(SandboxReceiptCodec.TryDecode(validated.Token, new SandboxBillingOptions().Secret, out _));

        Assert.Equal(EntitlementTier.Lifetime, service.CurrentTier);
        Assert.Equal(new[] { EntitlementTier.Lifetime }, tierChanges);
        Assert.Equal(
            nameof(EntitlementTier.Lifetime),
            await _secureStore.GetAsync(SecureStoreKeys.EntitlementTier)); // cache survives next launch
    }

    [Fact]
    public async Task Auto_restore_runs_only_once_per_install()
    {
        ConfigureServerSide(EntitlementTier.Lifetime);
        var service = CreateService(BillingOwning(Skus.Lifetime));

        await service.OnSignedInAsync();
        await service.OnSignedInAsync(); // second sign-in on the same install

        Assert.Single(_api.ValidatePurchaseRequests); // no re-restore
    }

    [Fact]
    public async Task Initialize_loads_the_cached_tier_before_any_network()
    {
        await _secureStore.SetAsync(SecureStoreKeys.EntitlementTier, nameof(EntitlementTier.Pro));
        var service = CreateService(BillingOwning());

        await service.InitializeAsync();

        Assert.Equal(EntitlementTier.Pro, service.CurrentTier);
    }

    [Fact]
    public async Task Refresh_keeps_cached_tier_when_server_unreachable()
    {
        await _secureStore.SetAsync(SecureStoreKeys.EntitlementTier, nameof(EntitlementTier.Pro));
        _api.EntitlementHandler = () => throw new HttpRequestException("offline");
        var service = CreateService(BillingOwning());
        await service.InitializeAsync();

        var tier = await service.RefreshAsync();

        Assert.Equal(EntitlementTier.Pro, tier);
    }

    [Fact]
    public async Task Refresh_adopts_server_downgrade()
    {
        // Server truth wins in BOTH directions — e.g. a refunded purchase downgrades the client.
        await _secureStore.SetAsync(SecureStoreKeys.EntitlementTier, nameof(EntitlementTier.Pro));
        _api.EntitlementHandler = () => new EntitlementResponse(EntitlementTier.Free);
        var service = CreateService(BillingOwning());
        await service.InitializeAsync();

        var tier = await service.RefreshAsync();

        Assert.Equal(EntitlementTier.Free, tier);
        Assert.Equal(nameof(EntitlementTier.Free), await _secureStore.GetAsync(SecureStoreKeys.EntitlementTier));
    }

    [Fact]
    public async Task Manual_restore_reports_reconciled_receipt_count()
    {
        ConfigureServerSide(EntitlementTier.Pro);
        var service = CreateService(BillingOwning(Skus.Pro));

        var reconciled = await service.RestoreAsync();

        Assert.Equal(1, reconciled);
        Assert.Equal(EntitlementTier.Pro, service.CurrentTier);
    }

    [Fact]
    public async Task Sign_in_with_no_purchases_still_completes_and_marks_restore_attempted()
    {
        ConfigureServerSide(EntitlementTier.Free);
        var service = CreateService(BillingOwning());

        await service.OnSignedInAsync();

        Assert.Empty(_api.ValidatePurchaseRequests);
        Assert.Equal(EntitlementTier.Free, service.CurrentTier);
        Assert.NotNull(_preferences.Get(PreferenceKeys.AutoRestoreDone));
    }

    [Fact]
    public async Task Restore_continues_past_a_receipt_that_fails_validation()
    {
        // Two receipts; the pro one is bound to another account (409) — it must NOT abort the lifetime one.
        _api.EntitlementHandler = () => new EntitlementResponse(EntitlementTier.Lifetime);
        _api.ValidatePurchaseHandler = r => r.SkuId == Skus.Pro
            ? throw new ApiException(HttpStatusCode.Conflict, new ApiError("conflict", "receipt bound to another account"))
            : new ValidatePurchaseResponse(EntitlementTier.Lifetime, PurchaseValidationOutcome.Restored);
        var service = CreateService(BillingOwning(Skus.Pro, Skus.Lifetime));

        var reconciled = await service.RestoreAsync();

        Assert.Equal(1, reconciled); // lifetime reconciled, pro skipped — not aborted
        Assert.Equal(2, _api.ValidatePurchaseRequests.Count); // both were attempted
        Assert.Equal(EntitlementTier.Lifetime, service.CurrentTier);
    }

    [Fact]
    public async Task Sign_out_clears_the_cached_paid_tier()
    {
        ConfigureServerSide(EntitlementTier.Lifetime);
        var service = CreateService(BillingOwning(Skus.Lifetime));
        await service.OnSignedInAsync();
        Assert.Equal(EntitlementTier.Lifetime, service.CurrentTier);

        await _session.SignOutAsync(); // raises AuthStateChanged(false)

        Assert.Equal(EntitlementTier.Free, service.CurrentTier);
        Assert.Null(await _secureStore.GetAsync(SecureStoreKeys.EntitlementTier));
        Assert.Null(_preferences.Get(PreferenceKeys.AutoRestoreDone));
    }

    [Fact]
    public async Task Reconcile_pending_validates_and_clears_a_stranded_receipt()
    {
        // A receipt left pending by a failed post-purchase validate is finished on the next launch.
        ConfigureServerSide(EntitlementTier.Pro);
        var proReceipt = await new SandboxBillingService(new SandboxBillingOptions()).PurchaseAsync(Skus.Pro);
        _pending.Add(proReceipt);
        var service = CreateService(BillingOwning());

        await service.ReconcilePendingAsync();

        Assert.Single(_api.ValidatePurchaseRequests);
        Assert.Equal(EntitlementTier.Pro, service.CurrentTier);
        Assert.Empty(_pending.GetAll()); // cleared once acknowledged
    }
}
