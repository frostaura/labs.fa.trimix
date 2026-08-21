namespace Trimix.Core;

/// <summary>Keys used in <see cref="Abstractions.ISecureStore"/>.</summary>
public static class SecureStoreKeys
{
    public const string EntitlementTier = "trimix.entitlement.tier";
}

/// <summary>Keys used in <see cref="Abstractions.IPreferences"/>.</summary>
public static class PreferenceKeys
{
    public const string UserPseudoId = "trimix.analytics.user_pseudo_id";
    public const string InstallTracked = "trimix.analytics.install_tracked";
    public const string FirstRunTracked = "trimix.analytics.first_run_tracked";
    public const string AutoRestoreDone = "trimix.entitlement.autorestore_done";
    public const string DepthUnit = "trimix.units.depth";
    public const string TempUnit = "trimix.units.temp";

    /// <summary>Round-trip ("O") UTC instant of the last completed "Sync now" run (Providers header label).</summary>
    public const string LastSyncUtc = "trimix.sync.last_utc";

    /// <summary>The You tab's inline-editable display name (local-only; C8).</summary>
    public const string UserName = "trimix.profile.name";

    /// <summary>The You tab's certification line, e.g. "PADI Advanced OW" (local-only; C8).</summary>
    public const string CertLabel = "trimix.profile.cert";

    /// <summary>User-defined provider priority as a CSV of connector ids, highest first. Decides which
    /// provider wins when overlapping dives are merged. See <see cref="Connectors.IProviderPriority"/>.</summary>
    public const string ProviderPriority = "trimix.providers.priority";

    /// <summary>One-shot flag: the Providers reorder hint (drag-to-set-priority) has been shown once.</summary>
    public const string ProviderReorderHintSeen = "trimix.providers.reorder_hint_seen";
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
    public const string Logbook = "logbook";

    /// <summary>The career-analytics "You" tab (aggregator architecture §8.5).</summary>
    public const string Stats = "stats";
    public const string DiveDetail = "dive-detail";
    public const string Paywall = "paywall";

    /// <summary>The Providers screen (prototype S5) — the connector registry, pushed from anywhere.</summary>
    public const string Providers = "providers";

    /// <summary>The in-app provider sign-in sheet (prototype S6), pushed from Providers with a connector id.</summary>
    public const string ProviderOAuth = "provider-oauth";

    /// <summary>Navigation parameter names.</summary>
    public static class Params
    {
        public const string DiveId = "diveId";

        /// <summary>Connector id for the provider sign-in sheet.</summary>
        public const string ConnectorId = "connectorId";

        /// <summary>"1" when Providers should nudge the Connect buttons (Sync-now tapped while unlinked).</summary>
        public const string Nudge = "nudge";

        /// <summary>Count of dives left behind on providers by the free cap (paywall sub-copy).</summary>
        public const string PendingDives = "pendingDives";
    }
}
