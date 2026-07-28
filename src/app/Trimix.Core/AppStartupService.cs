using Trimix.Core.Abstractions;
using Trimix.Core.Privacy;

namespace Trimix.Core;

/// <summary>
/// Launch orchestration, called once from the App's startup path:
/// initialize the local store + session + entitlement cache, emit <c>install</c> exactly once per
/// install, and flush analytics (to the local sink — serverless). Returns whether onboarding should
/// be shown (first run, i.e. <c>first_run_completed</c> has not fired yet).
/// </summary>
public sealed class AppStartupService(
    ILocalStore store,
    IAuthSession session,
    IEntitlementService entitlements,
    IAnalytics analytics,
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

        if (!session.IsAuthenticated)
        {
            // Startup reconciliation (belt-and-suspenders for the sign-out purge): an interrupted or failed
            // purge can strand a vendor consent flag / exchanged token in the secure enclave — which, on the
            // iOS Keychain, survives app uninstall. Re-run it once on any UNAUTHENTICATED launch so a stranded
            // link is cleared next time the app opens. Idempotent + a no-op when nothing is connected.
            await connectorPurge.PurgeAllAsync(cancellationToken);
        }

        await analytics.FlushAsync(cancellationToken);

        return preferences.Get(PreferenceKeys.FirstRunTracked) is null;
    }
}
