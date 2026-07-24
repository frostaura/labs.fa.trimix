using System.Net;
using Trimix.Contracts;
using Trimix.Contracts.Dtos;
using Trimix.Core.Abstractions;
using Trimix.Core.Api;
using Trimix.Core.Billing;
using Trimix.Core.Platform;
using Trimix.Core.Tests.Fakes;
using Trimix.Core.ViewModels;
using Xunit;

namespace Trimix.Core.Tests;

/// <summary>
/// Purchase-moment event contract: <c>purchase_started</c> → <c>purchase_completed</c> /
/// <c>purchase_cancelled</c> / <c>purchase_failed</c>, in order, with the receipt going to the
/// server for the authoritative grant.
/// </summary>
public class PaywallViewModelTests
{
    private readonly SandboxBillingService _billing = new(new SandboxBillingOptions());
    private readonly FakeApiClient _api = new();
    private readonly FakeEntitlementService _entitlements = new();
    private readonly RecordingAnalytics _analytics = new();
    private readonly FakeAuthSession _session = new() { IsAuthenticated = true };
    private readonly PendingPurchaseStore _pending = new(new InMemoryPreferences());
    private readonly FakeNavigator _navigator = new();
    private readonly FakeDialogs _dialogs = new();

    public PaywallViewModelTests()
    {
        _api.ValidatePurchaseHandler = r => new ValidatePurchaseResponse(
            r.SkuId == Skus.Lifetime ? EntitlementTier.Lifetime : EntitlementTier.Pro,
            PurchaseValidationOutcome.Granted);
    }

    private PaywallViewModel CreateViewModel(IBillingService? billing = null) =>
        new(billing ?? _billing, _api, _entitlements, _analytics, _session, _pending, _navigator, _dialogs);

    [Fact]
    public async Task Load_offers_exactly_the_two_locked_skus()
    {
        var vm = CreateViewModel();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Equal(new[] { Skus.Pro, Skus.Lifetime }, vm.Products.Select(p => p.SkuId));
    }

    [Fact]
    public async Task Successful_purchase_emits_started_then_completed_and_validates_server_side()
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

        // Server-authoritative grant: the receipt was posted and the tier re-fetched.
        var validated = Assert.Single(_api.ValidatePurchaseRequests);
        Assert.Equal(ReceiptPlatforms.Sandbox, validated.Platform);
        Assert.True(SandboxReceiptCodec.TryDecode(validated.Token, new SandboxBillingOptions().Secret, out var payload));
        Assert.Equal(Skus.Pro, payload!.SkuId);
        Assert.Equal(1, _entitlements.RefreshCalls);
        Assert.Equal(1, _navigator.BackCount);
        Assert.Equal(1, _analytics.FlushCalls); // funnel pushed promptly after the money moment
    }

    [Fact]
    public async Task User_cancel_emits_cancelled_and_never_reaches_the_server()
    {
        _billing.SimulateUserCancelOnNextPurchase = true;
        var vm = CreateViewModel();
        await vm.LoadCommand.ExecuteAsync(null);

        await vm.PurchaseCommand.ExecuteAsync(Skus.Lifetime);

        Assert.Equal(new[] { FunnelEvents.PurchaseStarted, FunnelEvents.PurchaseCancelled }, _analytics.Names);
        var cancelled = _analytics.Events[1];
        Assert.Equal(Skus.Lifetime, cancelled.Props[FunnelEvents.Props.SkuId]);
        Assert.Equal(PurchaseCancelledException.Reason, cancelled.Props[FunnelEvents.Props.Reason]);
        Assert.Empty(_api.ValidatePurchaseRequests);
        Assert.Equal(0, _entitlements.RefreshCalls);
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
        Assert.Empty(_api.ValidatePurchaseRequests);
        Assert.Single(_dialogs.Alerts);
    }

    [Fact]
    public async Task Restore_link_reconciles_and_leaves_the_paywall_on_success()
    {
        _entitlements.RestoreResult = 1;
        var vm = CreateViewModel();

        await vm.RestoreCommand.ExecuteAsync(null);

        Assert.Equal(1, _entitlements.RestoreCalls);
        Assert.Equal(1, _navigator.BackCount);
        Assert.Contains(_dialogs.Toasts, t => t.Contains("restored", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Restore_with_nothing_to_restore_stays_on_the_paywall()
    {
        _entitlements.RestoreResult = 0;
        var vm = CreateViewModel();

        await vm.RestoreCommand.ExecuteAsync(null);

        Assert.Equal(0, _navigator.BackCount);
        Assert.Contains(_dialogs.Toasts, t => t.Contains("No previous purchases", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Guest_is_routed_to_sign_in_before_any_store_charge()
    {
        _session.IsAuthenticated = false;
        var vm = CreateViewModel();
        await vm.LoadCommand.ExecuteAsync(null);

        await vm.PurchaseCommand.ExecuteAsync(Skus.Pro);

        // No purchase started, nothing charged, nothing validated — routed to auth instead.
        Assert.Empty(_analytics.Names);
        Assert.Empty(_api.ValidatePurchaseRequests);
        Assert.Empty(await _billing.RestoreAsync()); // the store never recorded ownership
        Assert.Contains(_navigator.Navigations, n => n.Route == Routes.Auth);
        Assert.Equal(0, _navigator.BackCount);
    }

    [Fact]
    public async Task Validation_failure_queues_the_receipt_and_warns_without_crashing()
    {
        // Store charged the user, then POST /purchases/validate fails: the receipt must be queued
        // for retry (not lost) and the user told — no unhandled exception.
        _api.ValidatePurchaseHandler = _ =>
            throw new ApiException(HttpStatusCode.ServiceUnavailable, new ApiError("unavailable", "server down"));
        var vm = CreateViewModel();
        await vm.LoadCommand.ExecuteAsync(null);

        await vm.PurchaseCommand.ExecuteAsync(Skus.Pro);

        // purchase_completed still fired (the store confirmed), but no entitlement grant.
        Assert.Contains(FunnelEvents.PurchaseCompleted, _analytics.Names);
        var pending = Assert.Single(_pending.GetAll());
        Assert.Equal(Skus.Pro, pending.SkuId);
        Assert.Single(_dialogs.Alerts);
        Assert.Equal(1, _analytics.FlushCalls);
        Assert.Equal(1, _navigator.BackCount);
    }

    [Fact]
    public async Task Restore_failure_surfaces_a_message_instead_of_throwing()
    {
        _entitlements.RestoreThrows = new HttpRequestException("offline");
        var vm = CreateViewModel();

        await vm.RestoreCommand.ExecuteAsync(null);

        Assert.Single(_dialogs.Alerts);
        Assert.Equal(0, _navigator.BackCount);
    }
}
