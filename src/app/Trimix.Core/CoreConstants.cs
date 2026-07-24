namespace Trimix.Core;

/// <summary>Keys used in <see cref="Abstractions.ISecureStore"/>.</summary>
public static class SecureStoreKeys
{
    public const string AuthToken = "trimix.auth.token";
    public const string AuthUser = "trimix.auth.user";
    public const string EntitlementTier = "trimix.entitlement.tier";

    /// <summary>
    /// The user-id of the last REAL account established on this device (written on sign-in, NOT cleared on
    /// sign-out). Used to detect a genuine identity change (account→different-account) so the previous
    /// identity's device-global connector consent+tokens are purged on a shared-device handoff. Lives in the
    /// secure enclave so it survives uninstall in lockstep with the connector tokens it guards (iOS Keychain).
    /// </summary>
    public const string LastAccountId = "trimix.auth.last_account_id";
}

/// <summary>Keys used in <see cref="Abstractions.IPreferences"/>.</summary>
public static class PreferenceKeys
{
    public const string UserPseudoId = "trimix.analytics.user_pseudo_id";
    public const string InstallTracked = "trimix.analytics.install_tracked";
    public const string FirstRunTracked = "trimix.analytics.first_run_tracked";
    public const string AutoRestoreDone = "trimix.entitlement.autorestore_done";
    public const string LastSyncUtc = "trimix.sync.last_utc";

    /// <summary>Receipts whose server validation has not yet succeeded (post-purchase retry queue).</summary>
    public const string PendingReceipts = "trimix.billing.pending_receipts";
    public const string DepthUnit = "trimix.units.depth";
    public const string TempUnit = "trimix.units.temp";
}

/// <summary>Client-side analytics defaults.</summary>
public static class AnalyticsDefaults
{
    /// <summary>Attribution source for organic installs (the only source before any paid UA — M3 gate).</summary>
    public const string InstallSourceOrganic = "organic";

    /// <summary>Auth provider prop value for email+password accounts (the slice's only provider).</summary>
    public const string AuthProviderEmail = "email";
}

/// <summary>Shell route names shared by viewmodels and the App's route registration.</summary>
public static class Routes
{
    public const string Onboarding = "onboarding";
    public const string Auth = "auth";
    public const string Logbook = "logbook";
    public const string Sites = "sites";
    public const string Import = "import";
    public const string Settings = "settings";

    /// <summary>The career-analytics "You" tab (aggregator architecture §8.5).</summary>
    public const string Stats = "stats";
    public const string ManualEntry = "manual-entry";
    public const string DiveDetail = "dive-detail";
    public const string SiteDetail = "site-detail";
    public const string Contribute = "contribute";
    public const string Paywall = "paywall";

    /// <summary>Navigation parameter names.</summary>
    public static class Params
    {
        public const string DiveId = "diveId";
        public const string SiteId = "siteId";
    }
}
