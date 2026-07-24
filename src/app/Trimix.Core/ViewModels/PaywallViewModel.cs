using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trimix.Contracts;
using Trimix.Contracts.Dtos;
using Trimix.Core.Abstractions;
using Trimix.Core.Api;
using Trimix.Core.Billing;

namespace Trimix.Core.ViewModels;

/// <summary>
/// The paywall (PRD Layer 3): exactly two one-off SKUs (Pro $14.99 / Lifetime $29.99, both true
/// non-consumables — never auto-renew) plus the restore link. Event contract per purchase attempt:
/// <c>purchase_started</c> → store result → <c>purchase_completed</c> (store confirmed;
/// receipt then goes to the server, which records <c>entitlement_granted</c>) or
/// <c>purchase_cancelled</c> / <c>purchase_failed</c>.
/// </summary>
public partial class PaywallViewModel(
    IBillingService billing,
    IApiClient api,
    IEntitlementService entitlements,
    IAnalytics analytics,
    IAuthSession session,
    IPendingPurchaseStore pendingPurchases,
    INavigator navigator,
    IUserDialogs dialogs) : ObservableObject
{
    [ObservableProperty]
    private IReadOnlyList<StoreProduct> _products = [];

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>
    /// View-state flag for the post-purchase "You're Pro" confirmation (S9): true once the store has
    /// confirmed the purchase and the entitlement is granted (or queued for retry). Purely a view
    /// concern — swaps the paywall from the purchase state to the unlocked state.
    /// </summary>
    [ObservableProperty]
    private bool _purchaseSucceeded;

    [RelayCommand]
    private async Task LoadAsync()
    {
        Products = await billing.QueryProductsAsync([.. Skus.All.Select(s => s.Id)]);
    }

    [RelayCommand]
    private async Task PurchaseAsync(string skuId)
    {
        // Account-bound entitlement (PRD Flow A ordering): a store purchase must attach to an account,
        // so route guests to sign-in FIRST instead of charging them and stranding the receipt.
        if (!session.IsAuthenticated)
        {
            await dialogs.AlertAsync(
                "Sign in to continue",
                "Create an account or sign in first so your purchase is saved to it and restores on any device.");
            await navigator.GoToAsync(Routes.Auth);
            return;
        }

        analytics.Track(FunnelEvents.PurchaseStarted, new Dictionary<string, string>
        {
            [FunnelEvents.Props.SkuId] = skuId,
        });

        IsBusy = true;
        try
        {
            StoreReceipt receipt;
            try
            {
                receipt = await billing.PurchaseAsync(skuId);
            }
            catch (PurchaseCancelledException)
            {
                analytics.Track(FunnelEvents.PurchaseCancelled, new Dictionary<string, string>
                {
                    [FunnelEvents.Props.SkuId] = skuId,
                    [FunnelEvents.Props.Reason] = PurchaseCancelledException.Reason,
                });
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                analytics.Track(FunnelEvents.PurchaseFailed, new Dictionary<string, string>
                {
                    [FunnelEvents.Props.SkuId] = skuId,
                    [FunnelEvents.Props.Reason] = ex.GetType().Name,
                });
                await dialogs.AlertAsync("Purchase failed", "The store could not complete the purchase. You were not charged.");
                return;
            }

            // The store confirmed the purchase — that is what purchase_completed means.
            var product = Products.FirstOrDefault(p => p.SkuId == skuId);
            var sku = Skus.All.First(s => s.Id == skuId);
            analytics.Track(FunnelEvents.PurchaseCompleted, new Dictionary<string, string>
            {
                [FunnelEvents.Props.SkuId] = skuId,
                [FunnelEvents.Props.GrossPrice] = (product?.Price ?? sku.PriceUsd).ToString("0.00", CultureInfo.InvariantCulture),
                [FunnelEvents.Props.Currency] = product?.CurrencyCode ?? "USD",
            });

            // Server-authoritative grant: POST the receipt; the server records entitlement_granted.
            // The store already charged the user, so a validation failure must NOT lose the receipt:
            // persist it for automatic retry on the next launch / restore and tell the user.
            try
            {
                await api.ValidatePurchaseAsync(new ValidatePurchaseRequest(receipt.Platform, receipt.SkuId, receipt.Token));
                await entitlements.RefreshAsync();
            }
            catch (Exception ex) when (ex is ApiException or HttpRequestException)
            {
                pendingPurchases.Add(receipt);
                await dialogs.AlertAsync(
                    "Purchase received",
                    "Your purchase went through. We couldn't finish activating it just now — it will complete automatically the next time you open the app or tap Restore.");
            }

            PurchaseSucceeded = true;
            await analytics.FlushAsync();
            await navigator.GoBackAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>The restore link (store policy requires it on every paywall).</summary>
    [RelayCommand]
    private async Task RestoreAsync()
    {
        int reconciled;
        try
        {
            reconciled = await entitlements.RestoreAsync();
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException)
        {
            await dialogs.AlertAsync("Restore failed", "We couldn't reach the store or server. Check your connection and try again.");
            return;
        }

        await dialogs.ToastAsync(reconciled > 0
            ? "Purchases restored."
            : "No previous purchases found for this account.");
        if (reconciled > 0)
        {
            await navigator.GoBackAsync();
        }
    }
}
