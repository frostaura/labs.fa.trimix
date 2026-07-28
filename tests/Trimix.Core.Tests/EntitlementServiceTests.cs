using Microsoft.Extensions.Logging.Abstractions;
using Trimix.Contracts;
using Trimix.Core.Billing;
using Trimix.Core.Entitlements;
using Trimix.Core.Platform;
using Trimix.Core.Tests.Fakes;
using Xunit;

namespace Trimix.Core.Tests;

/// <summary>
/// On-device entitlement authority (serverless): launch cache load, the restore-on-reinstall path
/// (fresh install + sign-in → auto-restore → receipt → SKU → tier, locally), and the sign-out reset.
/// </summary>
public class EntitlementServiceTests
{
    private readonly InMemorySecureStore _secureStore = new();
    private readonly InMemoryPreferences _preferences = new();
    private readonly FakeAuthSession _session = new();
    private readonly RecordingAnalytics _analytics = new();

    private EntitlementService CreateService(SandboxBillingService billing) => new(
        billing,
        _secureStore,
        _preferences,
        _session,
        _analytics,
        NullLogger<EntitlementService>.Instance);

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
    public async Task Fresh_install_sign_in_auto_restores_and_derives_tier_locally()
    {
        // Reinstall scenario: empty secure store + preferences, store account owns Lifetime.
        var service = CreateService(BillingOwning(Skus.Lifetime));
        var tierChanges = new List<EntitlementTier>();
        service.TierChanged += (_, tier) => tierChanges.Add(tier);

        await service.OnSignedInAsync();

        Assert.Equal(EntitlementTier.Lifetime, service.CurrentTier);
        Assert.Equal(new[] { EntitlementTier.Lifetime }, tierChanges);
        Assert.Equal(
            nameof(EntitlementTier.Lifetime),
            await _secureStore.GetAsync(SecureStoreKeys.EntitlementTier)); // cache survives next launch
    }

    [Fact]
    public async Task Auto_restore_marks_the_one_shot_flag()
    {
        var service = CreateService(BillingOwning(Skus.Lifetime));

        await service.OnSignedInAsync();
        await service.OnSignedInAsync(); // second sign-in on the same install is a no-op

        Assert.NotNull(_preferences.Get(PreferenceKeys.AutoRestoreDone));
        Assert.Equal(EntitlementTier.Lifetime, service.CurrentTier);
    }

    [Fact]
    public async Task Initialize_loads_the_cached_tier()
    {
        await _secureStore.SetAsync(SecureStoreKeys.EntitlementTier, nameof(EntitlementTier.Pro));
        var service = CreateService(BillingOwning());

        await service.InitializeAsync();

        Assert.Equal(EntitlementTier.Pro, service.CurrentTier);
    }

    [Fact]
    public async Task Refresh_reports_the_local_tier()
    {
        // Serverless: the local cache IS the authority — refresh never changes it.
        await _secureStore.SetAsync(SecureStoreKeys.EntitlementTier, nameof(EntitlementTier.Pro));
        var service = CreateService(BillingOwning());
        await service.InitializeAsync();

        var tier = await service.RefreshAsync();

        Assert.Equal(EntitlementTier.Pro, tier);
    }

    [Fact]
    public async Task Manual_restore_reports_reconciled_receipt_count()
    {
        var service = CreateService(BillingOwning(Skus.Pro));

        var reconciled = await service.RestoreAsync();

        Assert.Equal(1, reconciled);
        Assert.Equal(EntitlementTier.Pro, service.CurrentTier);
    }

    [Fact]
    public async Task Restore_grants_the_highest_owned_tier()
    {
        var service = CreateService(BillingOwning(Skus.Pro, Skus.Lifetime));

        var reconciled = await service.RestoreAsync();

        Assert.Equal(2, reconciled);
        Assert.Equal(EntitlementTier.Lifetime, service.CurrentTier); // Lifetime > Pro
    }

    [Fact]
    public async Task Sign_in_with_no_purchases_still_completes_and_marks_restore_attempted()
    {
        var service = CreateService(BillingOwning());

        await service.OnSignedInAsync();

        Assert.Equal(EntitlementTier.Free, service.CurrentTier);
        Assert.NotNull(_preferences.Get(PreferenceKeys.AutoRestoreDone));
    }

    [Fact]
    public async Task Sign_out_clears_the_cached_paid_tier()
    {
        var service = CreateService(BillingOwning(Skus.Lifetime));
        await service.OnSignedInAsync();
        Assert.Equal(EntitlementTier.Lifetime, service.CurrentTier);

        await _session.SignOutAsync(); // raises AuthStateChanged(false)

        Assert.Equal(EntitlementTier.Free, service.CurrentTier);
        Assert.Null(await _secureStore.GetAsync(SecureStoreKeys.EntitlementTier));
        Assert.Null(_preferences.Get(PreferenceKeys.AutoRestoreDone));
    }

    [Fact]
    public async Task Reconcile_pending_is_a_serverless_noop()
    {
        // There is no pending server-validation queue any more; the call is kept for call-site
        // compatibility and must complete without effect.
        var service = CreateService(BillingOwning());

        await service.ReconcilePendingAsync();

        Assert.Equal(EntitlementTier.Free, service.CurrentTier);
    }
}
