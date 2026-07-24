using Android.BillingClient.Api;
using Microsoft.Extensions.Logging;
using Trimix.Contracts;
using Trimix.Core.Abstractions;
using Trimix.Core.Billing;

namespace Trimix.App.Billing;

/// <summary>
/// Real Play Billing <see cref="IBillingService"/> (architecture overview §2) over the maintained
/// Microsoft-verified <c>Xamarin.Android.Google.BillingClient</c> binding (9.1.0.1, net10.0-android).
///
/// Status: COMPILED BUT INERT until a Play listing exists (name-clearance gate) — DI registers it
/// only when sandbox billing is off (Release); Debug keeps SandboxBillingService as the slice's
/// live path. The client NEVER derives entitlement from a purchase: the receipt
/// (<see cref="ReceiptPlatforms.GooglePlay"/> + purchase token) is posted to
/// <c>POST /purchases/validate</c>, where the google_play verifier is a documented NotConfigured
/// stub until production wiring (overview §3). Both SKUs are true non-consumables — purchases are
/// acknowledged, never consumed, so they restore forever.
/// </summary>
public sealed class PlayBillingService(ILogger<PlayBillingService> logger) : IBillingService, IDisposable
{
    private readonly Lock _gate = new();
    private BillingClient? _client;
    private TaskCompletionSource<StoreReceipt>? _pendingPurchase;
    private string? _pendingSkuId;

    public async Task<IReadOnlyList<StoreProduct>> QueryProductsAsync(
        IReadOnlyList<string> skuIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(skuIds);
        var client = await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        var details = await QueryProductDetailsAsync(client, skuIds, cancellationToken).ConfigureAwait(false);
        return
        [
            .. details.Select(d =>
            {
                var offer = d.GetOneTimePurchaseOfferDetails();
                return new StoreProduct(
                    d.ProductId,
                    d.Name,
                    offer?.FormattedPrice ?? "",
                    offer is null ? 0m : offer.PriceAmountMicros / 1_000_000m,
                    offer?.PriceCurrencyCode ?? "USD");
            }),
        ];
    }

    public async Task<StoreReceipt> PurchaseAsync(string skuId, CancellationToken cancellationToken = default)
    {
        var client = await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        var details = (await QueryProductDetailsAsync(client, [skuId], cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(d => d.ProductId == skuId)
            ?? throw new InvalidOperationException(
                $"SKU '{skuId}' is not available on Play (no listing yet — name-clearance gate).");

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
            var flowParams = BillingFlowParams.NewBuilder()
                .SetProductDetailsParamsList(
                [
                    BillingFlowParams.ProductDetailsParams.NewBuilder()
                        .SetProductDetails(details)
                        .Build(),
                ])
                .Build();

            var activity = Platform.CurrentActivity
                ?? throw new InvalidOperationException("No foreground activity to launch the billing flow from.");
            var launch = await MainThread.InvokeOnMainThreadAsync(
                () => client.LaunchBillingFlow(activity, flowParams)).ConfigureAwait(false);
            if (launch.ResponseCode != BillingResponseCode.Ok)
            {
                throw new InvalidOperationException(
                    $"Play billing flow launch failed ({launch.ResponseCode}): {launch.DebugMessage}");
            }

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
        var client = await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        var tcs = new TaskCompletionSource<IReadOnlyList<StoreReceipt>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var queryParams = QueryPurchasesParams.NewBuilder()
            .SetProductType(BillingClient.ProductType.Inapp)
            .Build();
        client.QueryPurchases(queryParams, new PurchasesResponseListener((result, purchases) =>
        {
            if (result.ResponseCode != BillingResponseCode.Ok)
            {
                tcs.TrySetException(new InvalidOperationException(
                    $"Play purchase query failed ({result.ResponseCode}): {result.DebugMessage}"));
                return;
            }

            tcs.TrySetResult(
            [
                .. purchases
                    .Where(p => p.PurchaseState == PurchaseState.Purchased)
                    .SelectMany(p => (p.Products ?? []).Select(sku =>
                        new StoreReceipt(ReceiptPlatforms.GooglePlay, sku, p.PurchaseToken ?? ""))),
            ]);
        }));

        using var registration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        return await tcs.Task.ConfigureAwait(false);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _client?.EndConnection();
            _client = null;
        }
    }

    // ---- Plumbing --------------------------------------------------------------------------

    private async Task<BillingClient> EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        BillingClient client;
        lock (_gate)
        {
            _client ??= BillingClient.NewBuilder(Platform.AppContext)
                .SetListener(new PurchasesUpdatedListener(OnPurchasesUpdated))
                .EnablePendingPurchases(
                    PendingPurchasesParams.NewBuilder().EnableOneTimeProducts().Build())
                .Build();
            client = _client;
        }

        if (client.IsReady)
        {
            return client;
        }

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.StartConnection(new BillingStateListener(
            onSetupFinished: result =>
            {
                if (result.ResponseCode == BillingResponseCode.Ok)
                {
                    tcs.TrySetResult(true);
                }
                else
                {
                    tcs.TrySetException(new InvalidOperationException(
                        $"Play billing unavailable ({result.ResponseCode}): {result.DebugMessage}"));
                }
            },
            onDisconnected: () => logger.LogInformation("Play billing service disconnected; will reconnect on next use.")));

        using var registration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        await tcs.Task.ConfigureAwait(false);
        return client;
    }

    private static async Task<IReadOnlyList<ProductDetails>> QueryProductDetailsAsync(
        BillingClient client, IReadOnlyList<string> skuIds, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<IReadOnlyList<ProductDetails>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var queryParams = QueryProductDetailsParams.NewBuilder()
            .SetProductList(
            [
                .. skuIds.Select(id => QueryProductDetailsParams.Product.NewBuilder()
                    .SetProductId(id)
                    .SetProductType(BillingClient.ProductType.Inapp)
                    .Build()),
            ])
            .Build();

        client.QueryProductDetails(queryParams, new ProductDetailsResponseListener((result, detailsResult) =>
        {
            if (result.ResponseCode != BillingResponseCode.Ok)
            {
                tcs.TrySetException(new InvalidOperationException(
                    $"Play product query failed ({result.ResponseCode}): {result.DebugMessage}"));
                return;
            }

            tcs.TrySetResult([.. detailsResult.ProductDetailsList ?? []]);
        }));

        using var registration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        return await tcs.Task.ConfigureAwait(false);
    }

    private void OnPurchasesUpdated(BillingResult result, IList<Purchase>? purchases)
    {
        TaskCompletionSource<StoreReceipt>? pending;
        string? skuId;
        lock (_gate)
        {
            pending = _pendingPurchase;
            skuId = _pendingSkuId;
        }

        if (pending is null || skuId is null)
        {
            return; // e.g. out-of-app purchase completion; reconciled by the next Restore.
        }

        if (result.ResponseCode == BillingResponseCode.UserCancelled)
        {
            pending.TrySetException(new PurchaseCancelledException(skuId));
            return;
        }

        if (result.ResponseCode != BillingResponseCode.Ok)
        {
            pending.TrySetException(new InvalidOperationException(
                $"Play purchase failed ({result.ResponseCode}): {result.DebugMessage}"));
            return;
        }

        var purchase = purchases?.FirstOrDefault(p =>
            p.PurchaseState == PurchaseState.Purchased && (p.Products?.Contains(skuId) ?? false));
        if (purchase?.PurchaseToken is not { Length: > 0 } token)
        {
            pending.TrySetException(new InvalidOperationException(
                "Play reported success but returned no completed purchase for the requested SKU."));
            return;
        }

        // Non-consumables must be acknowledged within 3 days or Play refunds them. Best-effort
        // here; the server-side receipt validation is the authoritative record either way.
        if (!purchase.IsAcknowledged)
        {
            BillingClient? client;
            lock (_gate)
            {
                client = _client;
            }

            client?.AcknowledgePurchase(
                AcknowledgePurchaseParams.NewBuilder().SetPurchaseToken(token).Build(),
                new AcknowledgeListener(r => logger.LogInformation(
                    "Acknowledge for {Sku}: {Code} {Message}", skuId, r.ResponseCode, r.DebugMessage)));
        }

        pending.TrySetResult(new StoreReceipt(ReceiptPlatforms.GooglePlay, skuId, token));
    }

    // ---- Java listener adapters ------------------------------------------------------------

    private sealed class BillingStateListener(Action<BillingResult> onSetupFinished, Action onDisconnected)
        : Java.Lang.Object, IBillingClientStateListener
    {
        public void OnBillingSetupFinished(BillingResult result) => onSetupFinished(result);

        public void OnBillingServiceDisconnected() => onDisconnected();
    }

    private sealed class PurchasesUpdatedListener(Action<BillingResult, IList<Purchase>?> onUpdated)
        : Java.Lang.Object, IPurchasesUpdatedListener
    {
        public void OnPurchasesUpdated(BillingResult result, IList<Purchase>? purchases) =>
            onUpdated(result, purchases);
    }

    private sealed class ProductDetailsResponseListener(Action<BillingResult, QueryProductDetailsResult> onResponse)
        : Java.Lang.Object, IProductDetailsResponseListener
    {
        public void OnProductDetailsResponse(BillingResult result, QueryProductDetailsResult detailsResult) =>
            onResponse(result, detailsResult);
    }

    private sealed class PurchasesResponseListener(Action<BillingResult, IList<Purchase>> onResponse)
        : Java.Lang.Object, IPurchasesResponseListener
    {
        public void OnQueryPurchasesResponse(BillingResult result, IList<Purchase> purchases) =>
            onResponse(result, purchases);
    }

    private sealed class AcknowledgeListener(Action<BillingResult> onResponse)
        : Java.Lang.Object, IAcknowledgePurchaseResponseListener
    {
        public void OnAcknowledgePurchaseResponse(BillingResult result) => onResponse(result);
    }
}
