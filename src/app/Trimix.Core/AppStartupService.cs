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
/// asks for it (explicit "Delete my data" / "Reset app" in Settings, both awaited inside
/// <c>ConnectorPurgeService</c>).</para>
/// </summary>
public sealed class AppStartupService(
    ILocalStore store,
    IEntitlementService entitlements,
    IAnalytics analytics,
    IPreferences preferences)
{
    public async Task<bool> RunAsync(
        string installSource = AnalyticsDefaults.InstallSourceOrganic,
        CancellationToken cancellationToken = default)
    {
        await store.InitializeAsync(cancellationToken);
        await entitlements.InitializeAsync(cancellationToken);

        await analytics.EnsureInstallTrackedAsync(installSource, cancellationToken);
        await analytics.FlushAsync(cancellationToken);

        return preferences.Get(PreferenceKeys.FirstRunTracked) is null;
    }
}
