using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trimix.Contracts;
using Trimix.Core.Abstractions;
using Trimix.Core.Billing;

namespace Trimix.Core.ViewModels;

/// <summary>
/// The paywall — the prototype's "Purchase moment" (S8) + "Unlocked" (S9), two states of one page:
/// banner <c>⚠ FREE LIMIT REACHED · N / cap DIVES</c>, "Unlock unlimited import.", exactly two
/// one-off SKUs (Pro / Lifetime <c>BEST VALUE</c> — never auto-renew), "Unlock — one-time",
/// "One-time purchase · no subscription, ever", "Restore purchases" and "Not now" (dismiss with the
/// "N dives on hold" toast). Serverless: the store confirms the purchase and the entitlement is
/// granted ON-DEVICE (receipt → SKU → tier via <see cref="IEntitlementService.RestoreAsync"/>) —
/// client-trust is the accepted tradeoff. Event contract per attempt: <c>purchase_started</c> →
/// store result → <c>purchase_completed</c> or <c>purchase_cancelled</c> / <c>purchase_failed</c>;
/// the tier grant emits <c>entitlement_granted</c> (EntitlementService). The cap numbers bind to
/// <see cref="FreeTier.FreeLogbookVisibleDives"/> so copy and enforcement can never drift.
/// </summary>
public partial class PaywallViewModel(
    IBillingService billing,
    IEntitlementService entitlements,
    ILocalStore store,
    IAnalytics analytics,
    INavigator navigator,
    IUserDialogs dialogs) : ObservableObject
{
    [ObservableProperty]
    private IReadOnlyList<StoreProduct> _products = [];

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>
    /// View-state flag for the post-purchase "You're Pro." confirmation (S9): true once the store has
    /// confirmed the purchase and the local entitlement is granted. The page stays put so the
    /// unlocked state shows; "Go to logbook" dismisses.
    /// </summary>
    [ObservableProperty]
    private bool _purchaseSucceeded;

    /// <summary>Count of dives left waiting on providers by the free cap (nav param from the sync run).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PendingLabel))]
    private int _pendingDives;

    /// <summary>Active (non-deleted) dives on device — the banner's N and the unlocked body's count.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LimitLabel))]
    [NotifyPropertyChangedFor(nameof(UnlockedBody))]
    private int _diveCount;

    /// <summary>Banner: "⚠ FREE LIMIT REACHED · N / cap DIVES".</summary>
    public string LimitLabel =>
        $"⚠ FREE LIMIT REACHED · {Math.Min(DiveCount, FreeTier.FreeLogbookVisibleDives)} / {FreeTier.FreeLogbookVisibleDives} DIVES";

    /// <summary>Sub-copy: "{N} more dives are waiting on your providers. Free keeps your last cap."</summary>
    public string PendingLabel =>
        (PendingDives > 0
            ? $"{PendingDives} more dive{(PendingDives > 1 ? "s are" : " is")} waiting on your providers."
            : "Import anything, forever.")
        + $" Free keeps your last {FreeTier.FreeLogbookVisibleDives}.";

    /// <summary>Unlocked (S9) body copy.</summary>
    public string UnlockedBody =>
        $"All {DiveCount} dives imported and multi-vendor unlock is active — restorable anytime through your app store.";

    [RelayCommand]
    private async Task LoadAsync()
    {
        Products = await billing.QueryProductsAsync([.. Skus.All.Select(s => s.Id)]);

        var dives = await store.GetDivesAsync();
        DiveCount = dives.Count(d => !d.Deleted);
    }

    [RelayCommand]
    private async Task PurchaseAsync(string skuId)
    {
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

            // On-device grant: the store now owns the receipt (incl. the one just purchased), so a
            // restore derives and caches the new tier locally. _ = receipt: kept for the event props
            // above; the receipt itself is never posted anywhere (serverless, client-trust).
            _ = receipt;
            await entitlements.RestoreAsync();

            // Stay on the page: PurchaseSucceeded flips the view to the Unlocked (S9) state and
            // "Go to logbook" dismisses.
            PurchaseSucceeded = true;
            await analytics.FlushAsync();
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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _ = ex;
            await dialogs.AlertAsync("Restore failed", "We couldn't reach the store. Check your connection and try again.");
            return;
        }

        await dialogs.ToastAsync(reconciled > 0
            ? "Purchases restored."
            : "No previous purchases found on this device.");
        if (reconciled > 0)
        {
            await navigator.GoBackAsync();
        }
    }

    /// <summary>"Not now": the single dismiss — free plan stays, held dives wait on the providers.</summary>
    [RelayCommand]
    private async Task NotNowAsync()
    {
        await navigator.GoBackAsync();
        await dialogs.ToastAsync(PendingDives > 0
            ? $"{PendingDives} dive{(PendingDives > 1 ? "s" : "")} on hold — upgrade anytime"
            : "Free plan kept — upgrade anytime");
    }

    /// <summary>Unlocked state's "Go to logbook".</summary>
    [RelayCommand]
    private Task GoToLogbookAsync() => navigator.GoToAsync(Routes.Logbook);
}
