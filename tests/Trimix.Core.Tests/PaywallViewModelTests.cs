using Trimix.Contracts;
using Trimix.Core.Abstractions;
using Trimix.Core.Billing;
using Trimix.Core.Tests.Fakes;
using Trimix.Core.ViewModels;
using Xunit;

namespace Trimix.Core.Tests;

/// <summary>
/// Purchase-moment event contract: <c>purchase_started</c> → <c>purchase_completed</c> /
/// <c>purchase_cancelled</c> / <c>purchase_failed</c>, in order. Serverless: after the store
/// confirms, the entitlement is granted ON-DEVICE via a local restore (receipt → SKU → tier) —
/// there is no server validation.
/// </summary>
public class PaywallViewModelTests
{
    private readonly SandboxBillingService _billing = new(new SandboxBillingOptions());
    private readonly FakeEntitlementService _entitlements = new();
    private readonly Trimix.Core.Storage.InMemoryLocalStore _store = new();
    private readonly RecordingAnalytics _analytics = new();
    private readonly FakeNavigator _navigator = new();
    private readonly FakeDialogs _dialogs = new();

    private PaywallViewModel CreateViewModel(IBillingService? billing = null) =>
        new(billing ?? _billing, _entitlements, _store, _analytics, _navigator, _dialogs);

    [Fact]
    public async Task Load_offers_exactly_the_two_locked_skus()
    {
        var vm = CreateViewModel();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Equal(new[] { Skus.Pro, Skus.Lifetime }, vm.Products.Select(p => p.SkuId));
    }

    [Fact]
    public async Task Successful_purchase_emits_started_then_completed_and_grants_locally()
    {
        var vm = CreateViewModel();
        await vm.LoadCommand.ExecuteAsync(null);

        await vm.PurchaseCommand.ExecuteAsync(Skus.Pro);

        // Event emission order is the funnel spine.
        Assert.Equal(new[] { FunnelEvents.PurchaseStarted, FunnelEvents.PurchaseCompleted }, _analytics.Names);

        var started = _analytics.Events[0];
        Assert.Equal(Skus.Pro, started.Props[FunnelEvents.Props.SkuId]);

        var completed = _analytics.Events[1];
        Assert.Equal(Skus.Pro, completed.Props[FunnelEvents.Props.SkuId]);
        Assert.Equal("14.99", completed.Props[FunnelEvents.Props.GrossPrice]);
        Assert.Equal("USD", completed.Props[FunnelEvents.Props.Currency]);

        // On-device grant: the local restore path ran (receipt → SKU → tier).
        Assert.Equal(1, _entitlements.RestoreCalls);
        Assert.True(vm.PurchaseSucceeded);
        // The page stays put so the Unlocked (S9) state renders; "Go to logbook" dismisses.
        Assert.Equal(0, _navigator.BackCount);
        Assert.Equal(1, _analytics.FlushCalls); // funnel drained promptly after the money moment
    }

    [Fact]
    public async Task Not_now_dismisses_without_a_toast()
    {
        var vm = CreateViewModel();
        vm.PendingDives = 3;

        await vm.NotNowCommand.ExecuteAsync(null);

        Assert.Equal(1, _navigator.BackCount);
        Assert.Empty(_dialogs.Toasts); // the design has no toasts — the dismiss itself is the state change
    }

    [Fact]
    public void Cap_copy_binds_to_the_FreeTier_constant_so_copy_and_enforcement_cannot_drift()
    {
        var vm = CreateViewModel();
        vm.PendingDives = 3;

        Assert.Contains($"/ {FreeTier.FreeLogbookVisibleDives} DIVES", vm.LimitLabel, StringComparison.Ordinal);
        Assert.Contains("3 more dives are waiting on your providers.", vm.PendingLabel, StringComparison.Ordinal);
        Assert.Contains($"Free keeps your last {FreeTier.FreeLogbookVisibleDives}.", vm.PendingLabel, StringComparison.Ordinal);
    }

    [Fact]
    public async Task User_cancel_emits_cancelled_and_grants_nothing()
    {
        _billing.SimulateUserCancelOnNextPurchase = true;
        var vm = CreateViewModel();
        await vm.LoadCommand.ExecuteAsync(null);

        await vm.PurchaseCommand.ExecuteAsync(Skus.Lifetime);

        Assert.Equal(new[] { FunnelEvents.PurchaseStarted, FunnelEvents.PurchaseCancelled }, _analytics.Names);
        var cancelled = _analytics.Events[1];
        Assert.Equal(Skus.Lifetime, cancelled.Props[FunnelEvents.Props.SkuId]);
        Assert.Equal(PurchaseCancelledException.Reason, cancelled.Props[FunnelEvents.Props.Reason]);
        Assert.Equal(0, _entitlements.RestoreCalls);
        Assert.False(vm.PurchaseSucceeded);
        Assert.Equal(0, _navigator.BackCount); // stays on the paywall
    }

    private sealed class ExplodingBilling : IBillingService
    {
        public Task<IReadOnlyList<StoreProduct>> QueryProductsAsync(IReadOnlyList<string> skuIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StoreProduct>>([]);

        public Task<StoreReceipt> PurchaseAsync(string skuId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("store unavailable");

        public Task<IReadOnlyList<StoreReceipt>> RestoreAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StoreReceipt>>([]);
    }

    [Fact]
    public async Task Store_failure_emits_failed_with_a_reason()
    {
        var vm = CreateViewModel(new ExplodingBilling());

        await vm.PurchaseCommand.ExecuteAsync(Skus.Pro);

        Assert.Equal(new[] { FunnelEvents.PurchaseStarted, FunnelEvents.PurchaseFailed }, _analytics.Names);
        Assert.Equal(nameof(InvalidOperationException), _analytics.Events[1].Props[FunnelEvents.Props.Reason]);
        Assert.Equal(0, _entitlements.RestoreCalls);
        Assert.Single(_dialogs.Alerts);
    }

    [Fact]
    public async Task Restore_success_shows_the_unlocked_state_instead_of_a_toast()
    {
        _entitlements.RestoreResult = 1;
        var vm = CreateViewModel();

        await vm.RestoreCommand.ExecuteAsync(null);

        Assert.Equal(1, _entitlements.RestoreCalls);
        // No toast, no dismissal: the page flips to the Unlocked (S9) state — the design indicates
        // state by animation — and "Go to logbook" dismisses.
        Assert.True(vm.PurchaseSucceeded);
        Assert.Equal(0, _navigator.BackCount);
        Assert.Empty(_dialogs.Toasts);
    }

    [Fact]
    public async Task Restore_with_nothing_to_restore_stays_on_the_paywall()
    {
        _entitlements.RestoreResult = 0;
        var vm = CreateViewModel();

        await vm.RestoreCommand.ExecuteAsync(null);

        Assert.Equal(0, _navigator.BackCount);
        Assert.False(vm.PurchaseSucceeded);
        // Nothing changed on screen, so the outcome arrives as a dialog (never a toast).
        Assert.Contains(_dialogs.Alerts, a => a.Title.Contains("Nothing to restore", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(_dialogs.Toasts);
    }

    [Fact]
    public async Task Restore_failure_surfaces_a_message_instead_of_throwing()
    {
        _entitlements.RestoreThrows = new InvalidOperationException("store unavailable");
        var vm = CreateViewModel();

        await vm.RestoreCommand.ExecuteAsync(null);

        Assert.Single(_dialogs.Alerts);
        Assert.Equal(0, _navigator.BackCount);
    }
}
