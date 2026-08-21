using Trimix.Core.Abstractions;

namespace Trimix.Core;

/// <summary>
/// Launch orchestration, called once from the App's startup path:
/// initialize the local store + entitlement cache, emit <c>install</c> exactly once per install,
/// and flush analytics (to the local sink — serverless). Returns whether onboarding should be shown
/// (first run, i.e. <c>first_run_completed</c> has not fired yet).
///
/// <para><b>No auth, no account.</b> Trimix has no sign-in of any kind — the phone IS the account.
/// There is nothing to initialize or unlock at launch; connector erasure runs only where the user
/// asks for it (the explicit "Reset app" factory reset on the "You" tab, awaited inside
/// <c>ConnectorPurgeService</c>).</para>
/// </summary>
public sealed class AppStartupService(
    ILocalStore store,
    IEntitlementService entitlements,
    IAnalytics analytics,
    IPreferences preferences,
    IEnumerable<IDiveSourceConnector> sources,
    IConnectorConsentStore consent)
{
    public async Task<bool> RunAsync(
        string installSource = AnalyticsDefaults.InstallSourceOrganic,
        CancellationToken cancellationToken = default)
    {
        await store.InitializeAsync(cancellationToken);
        await entitlements.InitializeAsync(cancellationToken);

        await PurgeOrphanDivesAsync(cancellationToken);

        await analytics.EnsureInstallTrackedAsync(installSource, cancellationToken);
        await analytics.FlushAsync(cancellationToken);

        return preferences.Get(PreferenceKeys.FirstRunTracked) is null;
    }

    /// <summary>
    /// Startup orphan sweep: remove every dive whose vendor is a KNOWN source connector that is NOT
    /// currently connected. Unlinking a provider deletes its dives, but dives orphaned by an unlink that
    /// happened before that cleanup existed (or by any other path) would otherwise linger forever — the
    /// logbook must only ever show dives from linked providers. Dives with no vendor, or a vendor that is
    /// not one of our source connectors, are left untouched.
    /// </summary>
    private async Task PurgeOrphanDivesAsync(CancellationToken cancellationToken)
    {
        foreach (var source in sources)
        {
            var connectorId = source.Descriptor.ConnectorId;
            if (!consent.HasConsent(connectorId))
            {
                await store.DeleteDivesByVendorAsync(connectorId, cancellationToken);
            }
        }
    }
}
