namespace Trimix.Contracts;

/// <summary>
/// FROZEN funnel event taxonomy — the binding analytics contract from <c>docs/prd.md</c> Layer 3.
/// Code MUST reference these constants; never event-name or property-key string literals.
/// Changing any name or required property is a spec change: update the PRD first, then this file.
/// </summary>
public static class FunnelEvents
{
    public const string Install = "install";
    public const string FirstRunCompleted = "first_run_completed";
    public const string AccountCreated = "account_created";
    public const string DiveLoggedManual = "dive_logged_manual";
    public const string BlePermissionRequested = "ble_permission_requested";
    public const string BlePermissionGranted = "ble_permission_granted";
    public const string BlePermissionDenied = "ble_permission_denied";
    public const string BleDeviceConnected = "ble_device_connected";
    public const string DivesFetched = "dives_fetched";
    public const string DiveLoggedImport = "dive_logged_import";
    public const string PaywallShown = "paywall_shown";
    public const string PurchaseStarted = "purchase_started";
    public const string PurchaseCompleted = "purchase_completed";
    public const string PurchaseFailed = "purchase_failed";
    public const string PurchaseCancelled = "purchase_cancelled";
    public const string EntitlementGranted = "entitlement_granted";
    public const string EntitlementRestored = "entitlement_restored";
    public const string SiteViewed = "site_viewed";
    public const string SiteContributionSubmitted = "site_contribution_submitted";
    public const string SiteContributionResolved = "site_contribution_resolved";

    // Cloud-aggregator additions (PRD Amendments set 2, item 10 — strictly additive, no renames):
    // the generic multi-source import/sync path emits these; the BLE-only path keeps ble_* + ble_import_cap.
    public const string SourceConnected = "source_connected";
    public const string DivesSynced = "dives_synced";

    /// <summary>Frozen property keys used by the taxonomy.</summary>
    public static class Props
    {
        public const string Platform = "platform";
        public const string Source = "source";
        public const string AppVersion = "app_version";
        public const string UserPseudoId = "user_pseudo_id";
        public const string AuthProvider = "auth_provider";
        public const string DiveId = "dive_id";
        public const string SiteMatchedBool = "site_matched_bool";
        public const string Outcome = "outcome";
        public const string Vendor = "vendor";
        public const string Model = "model";
        public const string Count = "count";
        public const string Trigger = "trigger";
        public const string EntitlementState = "entitlement_state";
        public const string SkuId = "sku_id";
        public const string GrossPrice = "gross_price";
        public const string Currency = "currency";
        public const string Reason = "reason";
        public const string Tier = "tier";
        public const string SourceReceiptId = "source_receipt_id";
        public const string SiteId = "site_id";
        public const string Status = "status";
    }

    /// <summary>Fixed / enumerated property values named by the taxonomy.</summary>
    public static class Values
    {
        /// <summary><c>dive_logged_import</c> requires <c>source=import</c>.</summary>
        public const string SourceImport = DiveSources.Import;

        /// <summary><c>paywall_shown</c> requires <c>entitlement_state=free</c>.</summary>
        public const string EntitlementStateFree = "free";

        public const string TriggerBleImportCap = "ble_import_cap";
        public const string TriggerCloudSyncCap = "cloud_sync_cap";
        public const string TriggerLogbookCap = "logbook_cap";
        public const string TriggerSiteContribute = "site_contribute";

        public const string StatusPending = ContributionStatus.Pending;
        public const string StatusApproved = ContributionStatus.Approved;
        public const string StatusRejected = ContributionStatus.Rejected;
    }

    /// <summary>
    /// Required properties per event. <c>IAnalytics.Track</c> validates against this:
    /// unknown event name or missing required prop throws in Debug, drops + logs in Release.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> RequiredProps =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [Install] = [Props.Platform, Props.Source, Props.AppVersion],
            [FirstRunCompleted] = [Props.UserPseudoId],
            [AccountCreated] = [Props.AuthProvider],
            [DiveLoggedManual] = [Props.DiveId, Props.SiteMatchedBool],
            [BlePermissionRequested] = [Props.Outcome],
            [BlePermissionGranted] = [Props.Outcome],
            [BlePermissionDenied] = [Props.Outcome],
            [BleDeviceConnected] = [Props.Vendor, Props.Model],
            [DivesFetched] = [Props.Vendor, Props.Count],
            [DiveLoggedImport] = [Props.DiveId, Props.Vendor, Props.Source],
            [PaywallShown] = [Props.Trigger, Props.EntitlementState],
            [PurchaseStarted] = [Props.SkuId],
            [PurchaseCompleted] = [Props.SkuId, Props.GrossPrice, Props.Currency],
            [PurchaseFailed] = [Props.SkuId, Props.Reason],
            [PurchaseCancelled] = [Props.SkuId, Props.Reason],
            [EntitlementGranted] = [Props.Tier, Props.SourceReceiptId],
            [EntitlementRestored] = [Props.Tier],
            [SiteViewed] = [Props.SiteId],
            [SiteContributionSubmitted] = [Props.Status],
            [SiteContributionResolved] = [Props.Status],
            // Cloud-aggregator additions. source is the connector id (free-text channel, like install.source);
            // it is NOT the frozen dive_logged_import 'import' value, so it validates per-event, not against AllowedValues[source].
            [SourceConnected] = [Props.Source, Props.Platform],
            [DivesSynced] = [Props.Source, Props.Count],
        };

    /// <summary>Every frozen event name (22 events).</summary>
    public static readonly IReadOnlyCollection<string> All = RequiredProps.Keys.ToArray();

    /// <summary>
    /// Events only the SERVER may emit (server-authoritative state changes). The analytics ingest
    /// endpoint MUST reject client submissions naming any of these — a client claiming, e.g.,
    /// <c>entitlement_granted</c> is spoofing entitlement state. Server-side emission goes through
    /// <c>ServerAnalytics.Emit</c>, never the client batch.
    /// </summary>
    public static readonly IReadOnlyCollection<string> ServerOnly = new HashSet<string>(StringComparer.Ordinal)
    {
        AccountCreated,
        EntitlementGranted,
        EntitlementRestored,
    };

    /// <summary>
    /// Allowed values for enumerated properties, per the PRD Layer-3 table. Keyed by property key
    /// (<see cref="Props"/>). Ingest MAY validate a present value against this set; a value outside
    /// the set is a contract violation. Only genuinely enumerated props appear here — free-text /
    /// numeric / id props (e.g. <c>dive_id</c>, <c>vendor</c>, <c>sku_id</c>) are intentionally absent.
    /// <para>
    /// NOTE (<c>outcome</c>): the terminal BLE-permission outcomes are <c>granted</c>/<c>denied</c> as
    /// specified. The <c>ble_permission_requested</c> lifecycle event currently emits
    /// <c>outcome=requested</c> (see <c>ImportViewModel</c>); wiring validation against this map must
    /// account for that lifecycle value or move it to a distinct property.
    /// </para>
    /// </summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyCollection<string>> AllowedValues =
        new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal)
        {
            [Props.Trigger] = new[] { Values.TriggerBleImportCap, Values.TriggerCloudSyncCap, Values.TriggerLogbookCap, Values.TriggerSiteContribute },
            [Props.EntitlementState] = new[] { Values.EntitlementStateFree },
            [Props.Status] = new[] { Values.StatusPending, Values.StatusApproved, Values.StatusRejected },
            [Props.Source] = new[] { Values.SourceImport },
            [Props.Outcome] = new[] { "granted", "denied" },
        };
}
