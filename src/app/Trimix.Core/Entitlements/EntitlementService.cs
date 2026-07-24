using Microsoft.Extensions.Logging;
using Trimix.Contracts;
using Trimix.Contracts.Dtos;
using Trimix.Core.Abstractions;
using Trimix.Core.Api;

namespace Trimix.Core.Entitlements;

/// <summary>
/// Server-authoritative entitlement cache (architecture overview §1). The client NEVER decides
/// tier from a receipt: receipts go to <c>POST /purchases/validate</c>, the server answers, and
/// this service caches that answer in <see cref="ISecureStore"/> and refreshes on launch/sign-in.
/// After sign-in on a fresh install it auto-restores store purchases once (PRD Flow D) — the
/// server records <c>entitlement_granted</c>/<c>entitlement_restored</c>, not the client.
/// </summary>
public sealed class EntitlementService : IEntitlementService
{
    private readonly IApiClient _api;
    private readonly IBillingService _billing;
    private readonly ISecureStore _secureStore;
    private readonly IPreferences _preferences;
    private readonly IPendingPurchaseStore _pendingPurchases;
    private readonly ILogger<EntitlementService> _logger;

    public EntitlementService(
        IApiClient api,
        IBillingService billing,
        ISecureStore secureStore,
        IPreferences preferences,
        IPendingPurchaseStore pendingPurchases,
        IAuthSession session,
        ILogger<EntitlementService> logger)
    {
        _api = api;
        _billing = billing;
        _secureStore = secureStore;
        _preferences = preferences;
        _pendingPurchases = pendingPurchases;
        _logger = logger;

        // Account-bound entitlement: dropping the account drops the cached paid tier so it never
        // leaks to a signed-out guest or the next account on the device.
        session.AuthStateChanged += (_, isAuthenticated) =>
        {
            if (!isAuthenticated)
            {
                Reset();
            }
        };
    }

    public EntitlementTier CurrentTier { get; private set; } = EntitlementTier.Free;

    public event EventHandler<EntitlementTier>? TierChanged;

    /// <summary>Sign-out reset: clear the cached tier + secure key + one-shot restore flag, drop to Free.</summary>
    private void Reset()
    {
        _secureStore.Remove(SecureStoreKeys.EntitlementTier);
        _preferences.Remove(PreferenceKeys.AutoRestoreDone);
        if (CurrentTier != EntitlementTier.Free)
        {
            CurrentTier = EntitlementTier.Free;
            TierChanged?.Invoke(this, EntitlementTier.Free);
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var cached = await _secureStore.GetAsync(SecureStoreKeys.EntitlementTier, cancellationToken).ConfigureAwait(false);
        if (cached is not null && Enum.TryParse<EntitlementTier>(cached, out var tier))
        {
            await SetTierAsync(tier, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<EntitlementTier> RefreshAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _api.GetMyEntitlementAsync(cancellationToken).ConfigureAwait(false);
            await SetTierAsync(response.Tier, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException or TaskCanceledException)
        {
            // Offline / unauthenticated: keep the cached answer.
            _logger.LogInformation(ex, "Entitlement refresh failed; keeping cached tier {Tier}.", CurrentTier);
        }

        return CurrentTier;
    }

    public async Task OnSignedInAsync(CancellationToken cancellationToken = default)
    {
        await RefreshAsync(cancellationToken).ConfigureAwait(false);

        // Fresh install (the preference flag is wiped on uninstall): reconcile store purchases once.
        if (_preferences.Get(PreferenceKeys.AutoRestoreDone) is not null)
        {
            // Even after the one-shot window, always drain any receipts left pending by a failed
            // post-purchase validate so a charged user eventually gets their entitlement.
            await ReconcilePendingAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            await RestoreAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException)
        {
            _logger.LogWarning(ex, "Auto-restore after sign-in failed; a later manual restore can reconcile.");
            return; // Leave the flag unset so the next sign-in retries.
        }

        _preferences.Set(PreferenceKeys.AutoRestoreDone, "1");
    }

    /// <summary>
    /// Re-validates only the receipts left pending by a failed post-purchase validate (does not touch
    /// the store). Called on launch/sign-in so a store-charged user is never stuck without entitlement.
    /// </summary>
    public async Task ReconcilePendingAsync(CancellationToken cancellationToken = default)
    {
        var pending = _pendingPurchases.GetAll();
        if (pending.Count == 0)
        {
            return;
        }

        var reconciled = await ValidateReceiptsAsync(pending, cancellationToken).ConfigureAwait(false);
        if (reconciled > 0)
        {
            await RefreshAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<int> RestoreAsync(CancellationToken cancellationToken = default)
    {
        var storeReceipts = await _billing.RestoreAsync(cancellationToken).ConfigureAwait(false);

        // Restore both the store's receipts AND any receipt left pending by a failed validate,
        // de-duplicated by token so we never double-post the same receipt.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var receipts = new List<StoreReceipt>();
        foreach (var receipt in storeReceipts.Concat(_pendingPurchases.GetAll()))
        {
            if (seen.Add(receipt.Token))
            {
                receipts.Add(receipt);
            }
        }

        var reconciled = await ValidateReceiptsAsync(receipts, cancellationToken).ConfigureAwait(false);
        if (reconciled > 0)
        {
            await RefreshAsync(cancellationToken).ConfigureAwait(false);
        }

        return reconciled;
    }

    /// <summary>
    /// Validates each receipt independently: one poisoned/foreign receipt (409) or a transient failure
    /// does not abort reconciliation of the rest. Successfully validated receipts are dropped from the
    /// pending queue. Returns the number reconciled.
    /// </summary>
    private async Task<int> ValidateReceiptsAsync(IReadOnlyList<StoreReceipt> receipts, CancellationToken cancellationToken)
    {
        var reconciled = 0;
        foreach (var receipt in receipts)
        {
            try
            {
                await _api.ValidatePurchaseAsync(
                        new ValidatePurchaseRequest(receipt.Platform, receipt.SkuId, receipt.Token),
                        cancellationToken)
                    .ConfigureAwait(false);
                _pendingPurchases.Remove(receipt);
                reconciled++;
            }
            catch (Exception ex) when (ex is ApiException or HttpRequestException)
            {
                _logger.LogWarning(ex, "Restore: receipt for {Sku} failed validation; continuing with the rest.", receipt.SkuId);
            }
        }

        return reconciled;
    }

    private async Task SetTierAsync(EntitlementTier tier, CancellationToken cancellationToken)
    {
        await _secureStore.SetAsync(SecureStoreKeys.EntitlementTier, tier.ToString(), cancellationToken).ConfigureAwait(false);
        if (tier == CurrentTier)
        {
            return;
        }

        CurrentTier = tier;
        TierChanged?.Invoke(this, tier);
    }
}
