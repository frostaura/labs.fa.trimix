using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Trimix.Contracts;
using Trimix.Core.Abstractions;

namespace Trimix.Core.Billing;

/// <summary>The user backed out of the store purchase sheet (not an error).</summary>
public sealed class PurchaseCancelledException(string skuId)
    : Exception($"Purchase of '{skuId}' was cancelled by the user.")
{
    public string SkuId { get; } = skuId;

    /// <summary>Frozen-contract <c>reason</c> prop value for <c>purchase_cancelled</c>.</summary>
    public const string Reason = "user_cancelled";
}

/// <summary>Configuration for <see cref="SandboxBillingService"/>.</summary>
public sealed class SandboxBillingOptions
{
    /// <summary>
    /// HMAC shared secret for sandbox receipts. Default matches the API's dev
    /// <c>Billing:SandboxSecret</c> (appsettings.json) so the slice's live purchase path
    /// works out of the box against a local API.
    /// </summary>
    public string Secret { get; set; } = "PLACEHOLDER-dev-only-sandbox-receipt-hmac-shared-secret";

    /// <summary>
    /// Simulated store account. Receipt identity (purchaseId, and therefore the whole token) is a
    /// pure function of (account, sku) — reinstalling and restoring reproduces the identical
    /// token, which is exactly what lets the server answer "restored" instead of granting twice.
    /// </summary>
    public string AccountId { get; set; } = "sandbox-local-account";

    /// <summary>SKUs the simulated account already owns (drives fresh-install restore scenarios).</summary>
    public IList<string> InitiallyOwnedSkuIds { get; } = [];
}

/// <summary>
/// Deterministic sandbox <see cref="IBillingService"/> (architecture overview §2) — the slice's
/// live purchase path. Signs receipts with <see cref="SandboxReceiptCodec"/> so the API's
/// SandboxReceiptVerifier validates them with the shared dev secret. DI selects this in Debug /
/// <c>TRIMIX_SANDBOX_BILLING=1</c>; PlayBillingService (App/Android) is the production twin.
/// </summary>
public sealed class SandboxBillingService : IBillingService
{
    /// <summary>Fixed issue instant so tokens are fully deterministic per (account, sku).</summary>
    private static readonly DateTime DeterministicIssuedAtUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly SandboxBillingOptions _options;
    private readonly IPreferences? _preferences;
    private readonly HashSet<string> _ownedSkuIds;
    private readonly Lock _gate = new();

    /// <summary>
    /// <paramref name="preferences"/> (optional) persists owned SKUs across process restart / reinstall,
    /// so the on-device restore path (PRD Flow D → <c>entitlement_restored</c>) works and a receipt lost
    /// to a failed validate stays recoverable. When null (unit tests), ownership is process-lifetime only.
    /// </summary>
    public SandboxBillingService(SandboxBillingOptions options, IPreferences? preferences = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _preferences = preferences;
        _ownedSkuIds = [.. options.InitiallyOwnedSkuIds];
        foreach (var sku in LoadPersistedOwned())
        {
            _ownedSkuIds.Add(sku);
        }
    }

    private string PersistKey => $"trimix.billing.owned.{_options.AccountId}";

    private IEnumerable<string> LoadPersistedOwned()
    {
        var raw = _preferences?.Get(PersistKey);
        return string.IsNullOrEmpty(raw)
            ? []
            : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private void PersistOwned() =>
        _preferences?.Set(PersistKey, string.Join(',', _ownedSkuIds.Order(StringComparer.Ordinal)));

    /// <summary>
    /// Test hook: when set, the next <see cref="PurchaseAsync"/> throws
    /// <see cref="PurchaseCancelledException"/> (simulating the user dismissing the store sheet)
    /// and resets the flag.
    /// </summary>
    public bool SimulateUserCancelOnNextPurchase { get; set; }

    public Task<IReadOnlyList<StoreProduct>> QueryProductsAsync(IReadOnlyList<string> skuIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(skuIds);
        IReadOnlyList<StoreProduct> products =
        [
            .. Skus.All
                .Where(sku => skuIds.Contains(sku.Id, StringComparer.Ordinal))
                .Select(sku => new StoreProduct(
                    sku.Id,
                    sku.DisplayName,
                    "$" + sku.PriceUsd.ToString("0.00", CultureInfo.InvariantCulture),
                    sku.PriceUsd,
                    "USD")),
        ];
        return Task.FromResult(products);
    }

    public Task<StoreReceipt> PurchaseAsync(string skuId, CancellationToken cancellationToken = default)
    {
        if (Skus.All.All(sku => sku.Id != skuId))
        {
            throw new ArgumentException($"Unknown SKU '{skuId}' — the locked ladder is exactly {Skus.Pro} and {Skus.Lifetime}.", nameof(skuId));
        }

        lock (_gate)
        {
            if (SimulateUserCancelOnNextPurchase)
            {
                SimulateUserCancelOnNextPurchase = false;
                throw new PurchaseCancelledException(skuId);
            }

            _ownedSkuIds.Add(skuId);
            PersistOwned();
        }

        return Task.FromResult(CreateReceipt(skuId));
    }

    public Task<IReadOnlyList<StoreReceipt>> RestoreAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IReadOnlyList<StoreReceipt> receipts = [.. _ownedSkuIds.Order(StringComparer.Ordinal).Select(CreateReceipt)];
            return Task.FromResult(receipts);
        }
    }

    private StoreReceipt CreateReceipt(string skuId)
    {
        var payload = new SandboxReceiptPayload(
            ReceiptPlatforms.Sandbox,
            skuId,
            DeterministicPurchaseId(_options.AccountId, skuId),
            DeterministicIssuedAtUtc);
        return new StoreReceipt(ReceiptPlatforms.Sandbox, skuId, SandboxReceiptCodec.Encode(payload, _options.Secret));
    }

    /// <summary>Stable purchase id: <c>sandbox-</c> + GUID folded from SHA-256(account:sku).</summary>
    private static string DeterministicPurchaseId(string accountId, string skuId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{accountId}:{skuId}"));
        return $"sandbox-{new Guid(hash.AsSpan(0, 16)):N}";
    }
}
