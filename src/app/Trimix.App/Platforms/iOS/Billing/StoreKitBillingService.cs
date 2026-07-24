using Foundation;
using Microsoft.Extensions.Logging;
using StoreKit;
using Trimix.Contracts;
using Trimix.Core.Abstractions;
using Trimix.Core.Billing;

namespace Trimix.App.Billing;

/// <summary>
/// Native App Store <see cref="IBillingService"/> (architecture overview §2) over StoreKit 1
/// (<c>SKProductsRequest</c> / <c>SKPaymentQueue</c>), the iOS twin of the Android
/// <c>PlayBillingService</c>.
///
/// Status: COMPILED BUT INERT until an App Store Connect listing (or a local .storekit config) exists —
/// DI registers it only when sandbox billing is off (Release); Debug keeps SandboxBillingService as the
/// slice's live path. Like every client, it NEVER derives entitlement from a purchase: the receipt
/// (<see cref="ReceiptPlatforms.AppStore"/> + the base64 App Store receipt blob) is posted to
/// <c>POST /purchases/validate</c>, where the App Store verifier reconciles a dev/sandbox-shaped
/// receipt through the funnel and leaves production App Store Server API validation as a documented
/// TODO (overview §3). Both SKUs are true non-consumables — transactions are finished, never consumed,
/// so <c>SKPaymentQueue.RestoreCompletedTransactions</c> restores them forever.
///
/// Validation reality: real-hardware / real-store validation is an M3 concern. The iOS Simulator can
/// exercise StoreKit only against a local <c>.storekit</c> config (see Platforms/iOS/Trimix.storekit)
/// wired via an Xcode scheme; full validation needs App Store Connect or that config, not <c>dotnet run</c>.
/// </summary>
public sealed class StoreKitBillingService(ILogger<StoreKitBillingService> logger) : IBillingService, IDisposable
{
    private readonly Lock _gate = new();
    private PaymentObserver? _observer;

    // Single-pending-purchase gate (mirrors PlayBillingService).
    private TaskCompletionSource<StoreReceipt>? _pendingPurchase;
    private string? _pendingSkuId;

    // Single-pending-restore gate; product ids are collected across Restored callbacks.
    private TaskCompletionSource<IReadOnlyList<StoreReceipt>>? _pendingRestore;
    private List<string>? _restoredProductIds;

    public async Task<IReadOnlyList<StoreProduct>> QueryProductsAsync(
        IReadOnlyList<string> skuIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(skuIds);
        var products = await QueryRawProductsAsync(skuIds, cancellationToken).ConfigureAwait(false);
        return [.. products.Select(ToStoreProduct)];
    }

    public async Task<StoreReceipt> PurchaseAsync(string skuId, CancellationToken cancellationToken = default)
    {
        var product = (await QueryRawProductsAsync([skuId], cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(p => p.ProductIdentifier == skuId)
            ?? throw new InvalidOperationException(
                $"SKU '{skuId}' is not available on the App Store (no listing yet — name-clearance gate).");

        var pending = new TaskCompletionSource<StoreReceipt>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_pendingPurchase is not null)
            {
                throw new InvalidOperationException("Another purchase is already in progress.");
            }

            _pendingPurchase = pending;
            _pendingSkuId = skuId;
        }

        try
        {
            EnsureObserver();
            SKPaymentQueue.DefaultQueue.AddPayment(SKPayment.CreateFrom(product));

            using var registration = cancellationToken.Register(() => pending.TrySetCanceled(cancellationToken));
            return await pending.Task.ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                _pendingPurchase = null;
                _pendingSkuId = null;
            }
        }
    }

    public async Task<IReadOnlyList<StoreReceipt>> RestoreAsync(CancellationToken cancellationToken = default)
    {
        var pending = new TaskCompletionSource<IReadOnlyList<StoreReceipt>>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_pendingRestore is not null)
            {
                throw new InvalidOperationException("Another restore is already in progress.");
            }

            _pendingRestore = pending;
            _restoredProductIds = [];
        }

        try
        {
            EnsureObserver();
            SKPaymentQueue.DefaultQueue.RestoreCompletedTransactions();

            using var registration = cancellationToken.Register(() => pending.TrySetCanceled(cancellationToken));
            return await pending.Task.ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                _pendingRestore = null;
                _restoredProductIds = null;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_observer is not null)
            {
                SKPaymentQueue.DefaultQueue.RemoveTransactionObserver(_observer);
                _observer.Dispose();
                _observer = null;
            }
        }
    }

    // ---- Plumbing --------------------------------------------------------------------------

    private void EnsureObserver()
    {
        lock (_gate)
        {
            if (_observer is not null)
            {
                return;
            }

            _observer = new PaymentObserver(OnTransactionsUpdated, OnRestoreFinished, OnRestoreFailed);
            SKPaymentQueue.DefaultQueue.AddTransactionObserver(_observer);
        }
    }

    private static async Task<SKProduct[]> QueryRawProductsAsync(
        IReadOnlyList<string> skuIds, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<SKProduct[]>(TaskCreationOptions.RunContinuationsAsynchronously);

        // `using` locals keep the request + delegate rooted across the await so the GC can't collect
        // the managed wrappers before StoreKit calls back on the delegate.
        using var identifiers = new NSSet(skuIds.Select(id => (NSObject)(NSString)id).ToArray());
        using var request = new SKProductsRequest(identifiers);
        using var requestDelegate = new ProductsRequestDelegate(
            onResponse: (_, response) => tcs.TrySetResult(response.Products ?? []),
            onFailed: (_, error) => tcs.TrySetException(new InvalidOperationException(
                $"App Store product query failed: {error?.LocalizedDescription}")));
        request.Delegate = requestDelegate;

        using var registration = cancellationToken.Register(() =>
        {
            request.Cancel();
            tcs.TrySetCanceled(cancellationToken);
        });

        request.Start();
        return await tcs.Task.ConfigureAwait(false);
    }

    private static StoreProduct ToStoreProduct(SKProduct product)
    {
        using var formatter = new NSNumberFormatter
        {
            FormatterBehavior = NSNumberFormatterBehavior.Version_10_4,
            NumberStyle = NSNumberFormatterStyle.Currency,
            Locale = product.PriceLocale,
        };

        return new StoreProduct(
            product.ProductIdentifier,
            product.LocalizedTitle,
            formatter.StringFromNumber(product.Price) ?? "",
            (decimal)product.Price.DoubleValue,
            product.PriceLocale?.CurrencyCode ?? "USD");
    }

    /// <summary>The bundle App Store receipt as base64 — the opaque token posted to the server.</summary>
    private static string ReadAppReceiptBase64()
    {
        var url = NSBundle.MainBundle.AppStoreReceiptUrl
            ?? throw new InvalidOperationException("No App Store receipt is present on this bundle.");
        using var data = NSData.FromUrl(url)
            ?? throw new InvalidOperationException("The App Store receipt could not be read from the bundle.");
        return data.GetBase64EncodedString(NSDataBase64EncodingOptions.None);
    }

    // ---- Transaction handling --------------------------------------------------------------

    private void OnTransactionsUpdated(SKPaymentTransaction[] transactions)
    {
        foreach (var transaction in transactions)
        {
            switch (transaction.TransactionState)
            {
                case SKPaymentTransactionState.Purchased:
                    HandlePurchased(transaction);
                    break;
                case SKPaymentTransactionState.Failed:
                    HandleFailed(transaction);
                    break;
                case SKPaymentTransactionState.Restored:
                    HandleRestored(transaction);
                    break;

                // Purchasing / Deferred: still in flight — leave on the queue, StoreKit re-notifies.
            }
        }
    }

    private void HandlePurchased(SKPaymentTransaction transaction)
    {
        TaskCompletionSource<StoreReceipt>? pending;
        string? skuId;
        lock (_gate)
        {
            pending = _pendingPurchase;
            skuId = _pendingSkuId;
        }

        var productId = transaction.Payment?.ProductIdentifier;
        SKPaymentQueue.DefaultQueue.FinishTransaction(transaction);

        if (pending is null || skuId is null || !string.Equals(productId, skuId, StringComparison.Ordinal))
        {
            // e.g. an out-of-app / interrupted purchase completing; reconciled by the next Restore.
            logger.LogInformation("Ignoring purchased transaction for {Sku} with no matching pending request.", productId);
            return;
        }

        try
        {
            pending.TrySetResult(new StoreReceipt(ReceiptPlatforms.AppStore, skuId, ReadAppReceiptBase64()));
        }
        catch (Exception ex)
        {
            pending.TrySetException(ex);
        }
    }

    private void HandleFailed(SKPaymentTransaction transaction)
    {
        TaskCompletionSource<StoreReceipt>? pending;
        string? skuId;
        lock (_gate)
        {
            pending = _pendingPurchase;
            skuId = _pendingSkuId;
        }

        var productId = transaction.Payment?.ProductIdentifier;
        var error = transaction.Error;
        SKPaymentQueue.DefaultQueue.FinishTransaction(transaction);

        if (pending is null || skuId is null
            || (productId is not null && !string.Equals(productId, skuId, StringComparison.Ordinal)))
        {
            return;
        }

        if (error is not null && (long)error.Code == (long)SKError.PaymentCancelled)
        {
            pending.TrySetException(new PurchaseCancelledException(skuId));
        }
        else
        {
            pending.TrySetException(new InvalidOperationException(
                $"App Store purchase failed ({error?.Code}): {error?.LocalizedDescription}"));
        }
    }

    private void HandleRestored(SKPaymentTransaction transaction)
    {
        // The restored product id lives on the original transaction; fall back to the current one.
        var productId = (transaction.OriginalTransaction?.Payment ?? transaction.Payment)?.ProductIdentifier;
        SKPaymentQueue.DefaultQueue.FinishTransaction(transaction);

        if (productId is null)
        {
            return;
        }

        lock (_gate)
        {
            _restoredProductIds?.Add(productId);
        }
    }

    private void OnRestoreFinished()
    {
        TaskCompletionSource<IReadOnlyList<StoreReceipt>>? pending;
        List<string>? productIds;
        lock (_gate)
        {
            pending = _pendingRestore;
            productIds = _restoredProductIds;
        }

        if (pending is null)
        {
            return;
        }

        try
        {
            var ids = (productIds ?? []).Distinct(StringComparer.Ordinal).ToArray();
            if (ids.Length == 0)
            {
                // Nothing owned — don't read a (possibly absent) receipt just to hand back an empty list.
                pending.TrySetResult([]);
                return;
            }

            // Non-consumables share the single bundle receipt; each restored SKU points at it.
            var receiptBase64 = ReadAppReceiptBase64();
            pending.TrySetResult(
                [.. ids.Select(id => new StoreReceipt(ReceiptPlatforms.AppStore, id, receiptBase64))]);
        }
        catch (Exception ex)
        {
            pending.TrySetException(ex);
        }
    }

    private void OnRestoreFailed(NSError? error)
    {
        TaskCompletionSource<IReadOnlyList<StoreReceipt>>? pending;
        lock (_gate)
        {
            pending = _pendingRestore;
        }

        pending?.TrySetException(new InvalidOperationException(
            $"App Store restore failed ({error?.Code}): {error?.LocalizedDescription}"));
    }

    // ---- ObjC delegate / observer adapters -------------------------------------------------

    private sealed class ProductsRequestDelegate(
        Action<SKProductsRequest, SKProductsResponse> onResponse,
        Action<SKRequest, NSError?> onFailed) : SKProductsRequestDelegate
    {
        public override void ReceivedResponse(SKProductsRequest request, SKProductsResponse response) =>
            onResponse(request, response);

        public override void RequestFailed(SKRequest request, NSError error) => onFailed(request, error);
    }

    private sealed class PaymentObserver(
        Action<SKPaymentTransaction[]> onUpdated,
        Action onRestoreFinished,
        Action<NSError?> onRestoreFailed) : SKPaymentTransactionObserver
    {
        public override void UpdatedTransactions(SKPaymentQueue queue, SKPaymentTransaction[] transactions) =>
            onUpdated(transactions);

        public override void RestoreCompletedTransactionsFinished(SKPaymentQueue queue) => onRestoreFinished();

        public override void RestoreCompletedTransactionsFailedWithError(SKPaymentQueue queue, NSError error) =>
            onRestoreFailed(error);
    }
}
