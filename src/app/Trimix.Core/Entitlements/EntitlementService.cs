using Microsoft.Extensions.Logging;
using Trimix.Contracts;
using Trimix.Core.Abstractions;

namespace Trimix.Core.Entitlements;

/// <summary>
/// On-device entitlement authority (serverless). The tier is derived locally from store receipts
/// (<see cref="IBillingService"/> purchase/restore → SKU → <see cref="SkuDefinition.GrantsTier"/>) and
/// cached in <see cref="ISecureStore"/>. There is no server receipt validation — client-trust is the
/// accepted tradeoff of the serverless architecture (founder decision, 2026-07-27). Restore is the
/// single grant path: after a purchase the store owns the receipt and a restore picks it up.
/// </summary>
public sealed class EntitlementService : IEntitlementService
{
    private readonly IBillingService _billing;
    private readonly ISecureStore _secureStore;
    private readonly IPreferences _preferences;
    private readonly IAnalytics _analytics;
    private readonly ILogger<EntitlementService> _logger;

    public EntitlementService(
        IBillingService billing,
        ISecureStore secureStore,
        IPreferences preferences,
        IAuthSession session,
        IAnalytics analytics,
        ILogger<EntitlementService> logger)
    {
        _billing = billing;
        _secureStore = secureStore;
        _preferences = preferences;
        _analytics = analytics;
        _logger = logger;

        // Ending the local session drops the cached paid tier so it never leaks to the next identity
        // on a shared device; a restore re-grants from the store at any time.
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

    /// <summary>Sign-out reset: clear the cached tier + one-shot restore flag, drop to Free.</summary>
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

    /// <summary>Serverless: the local cache IS the authority — refresh just reports it.</summary>
    public Task<EntitlementTier> RefreshAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(CurrentTier);

    public async Task OnSignedInAsync(CancellationToken cancellationToken = default)
    {
        // Fresh install (the preference flag is wiped on uninstall): reconcile store purchases once.
        if (_preferences.Get(PreferenceKeys.AutoRestoreDone) is not null)
        {
            return;
        }

        try
        {
            await RestoreAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Auto-restore after sign-in failed; a later manual restore can reconcile.");
            return; // Leave the flag unset so the next sign-in retries.
        }

        _preferences.Set(PreferenceKeys.AutoRestoreDone, "1");
    }

    /// <summary>Serverless: there is no pending server-validation queue — nothing to reconcile.</summary>
    public Task ReconcilePendingAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public async Task<int> RestoreAsync(CancellationToken cancellationToken = default)
    {
        var receipts = await _billing.RestoreAsync(cancellationToken).ConfigureAwait(false);

        var reconciled = 0;
        var best = CurrentTier;
        StoreReceipt? grantingReceipt = null;
        foreach (var receipt in receipts)
        {
            var sku = Skus.All.FirstOrDefault(s => s.Id == receipt.SkuId);
            if (sku is null)
            {
                _logger.LogWarning("Restore: receipt for unknown SKU {Sku} ignored.", receipt.SkuId);
                continue;
            }

            reconciled++;
            if (sku.GrantsTier > best)
            {
                best = sku.GrantsTier;
                grantingReceipt = receipt;
            }
        }

        if (best != CurrentTier)
        {
            await SetTierAsync(best, cancellationToken).ConfigureAwait(false);

            // Serverless: the on-device grant IS the entitlement authority now, so the frozen
            // entitlement_granted event is emitted here (its former server emitter was deleted).
            _analytics.Track(FunnelEvents.EntitlementGranted, new Dictionary<string, string>
            {
                [FunnelEvents.Props.Tier] = best.ToString(),
                [FunnelEvents.Props.SourceReceiptId] = grantingReceipt?.Token ?? "local",
            });
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
