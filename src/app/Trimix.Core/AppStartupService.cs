using Trimix.Core.Abstractions;
using Trimix.Core.Privacy;

namespace Trimix.Core;

/// <summary>
/// Launch orchestration, called once from the App's startup path:
/// initialize the local store + session + entitlement cache, emit <c>install</c> exactly once per
/// install, refresh the server-authoritative entitlement, sync dives, and flush analytics.
/// Returns whether onboarding should be shown (first run, i.e. <c>first_run_completed</c> has not
/// fired yet).
/// </summary>
public sealed class AppStartupService(
    ILocalStore store,
    IAuthSession session,
    IEntitlementService entitlements,
    IAnalytics analytics,
    ISyncService sync,
    IPreferences preferences,
    IConnectorPurgeService connectorPurge)
{
    public async Task<bool> RunAsync(
        string installSource = AnalyticsDefaults.InstallSourceOrganic,
        CancellationToken cancellationToken = default)
    {
        await store.InitializeAsync(cancellationToken);
        await session.InitializeAsync(cancellationToken);
        await entitlements.InitializeAsync(cancellationToken);

        await analytics.EnsureInstallTrackedAsync(installSource, cancellationToken);

        // Launch refresh (architecture overview §1); offline keeps the cached tier.
        await entitlements.RefreshAsync(cancellationToken);

        // Finish any purchase whose post-store validation failed on a prior run (store-charged but
        // not yet granted) so a paying user is never permanently stuck on Free.
        await entitlements.ReconcilePendingAsync(cancellationToken);

        if (session.IsAuthenticated)
        {
            await sync.SyncAsync(cancellationToken);
        }
        else
        {
            // Startup reconciliation (belt-and-suspenders for the sign-out purge): an interrupted or failed
            // purge can strand a vendor consent flag / exchanged token in the secure enclave — which, on the
            // iOS Keychain, survives app uninstall. Re-run it once on any UNAUTHENTICATED launch so a stranded
            // link is cleared next time the app opens. Idempotent + a no-op when nothing is connected, and
            // guarded off the authenticated path so it never disrupts a normal signed-in launch.
            await connectorPurge.PurgeAllAsync(cancellationToken);
        }

        await analytics.FlushAsync(cancellationToken);

        return preferences.Get(PreferenceKeys.FirstRunTracked) is null;
    }
}
