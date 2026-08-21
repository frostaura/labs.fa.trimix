using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Trimix.Core.Abstractions;
using Trimix.Core.Analytics;
using Trimix.Core.Billing;
using Trimix.Core.Connectors;
using Trimix.Core.Connectors.Cloud;
using Trimix.Core.Connectors.Infra;
using Trimix.Core.Connectors.Push;
using Trimix.Core.DiveComputer;
using Trimix.Core.Entitlements;
using Trimix.Core.Formatting;
using Trimix.Core.Platform;
using Trimix.Core.Privacy;
using Trimix.Core.Storage;
using Trimix.Core.ViewModels;

namespace Trimix.Core;

/// <summary>Host-configurable knobs for <see cref="ServiceCollectionExtensions.AddTrimixCore"/>.</summary>
public sealed class TrimixCoreOptions
{
    /// <summary>SQLite file path for <see cref="SqliteLocalStore"/>; null keeps <see cref="InMemoryLocalStore"/>.</summary>
    public string? LocalDatabasePath { get; set; }

    /// <summary>
    /// Sandbox billing (architecture overview §2): default in Debug, and forceable anywhere via
    /// <c>TRIMIX_SANDBOX_BILLING=1</c>. When false the App must register its store billing service.
    /// </summary>
    public bool UseSandboxBilling { get; set; } = IsDebugBuild || IsSandboxForced;

    /// <summary>
    /// Deploy master switch for the reverse-engineered cloud-pull connector family
    /// (Garmin/Suunto/Oceanic/Mares/ScubaPro). Default ON since the serverless rebuild promoted the
    /// connectors to the primary product surface (founder-directed escalation of the 2026-07-05 legal
    /// override — recorded, not silent): the family registers and appears in the Providers list. The
    /// user-protecting gate is UNCHANGED: every descriptor stays <c>ToSRiskReverseEng</c> +
    /// <c>EnabledByDefault=false</c>, and zero I/O happens before per-user consent behind the plain-language
    /// disclosure. This switch remains the deploy kill-switch (mapped from appsettings
    /// <c>Features.EnableCloudConnectors</c>) — OFF unregisters the family entirely.
    /// </summary>
    public bool EnableCloudConnectors { get; set; } = true;

    /// <summary>
    /// Deploy master switch for the reverse-engineered cert-agency PUSH connector family (PADI/SSI).
    /// Default ON, same promoted-surface semantics and same per-user consent gating as
    /// <see cref="EnableCloudConnectors"/> (mapped from appsettings <c>Features.EnablePushConnectors</c>).
    /// </summary>
    public bool EnablePushConnectors { get; set; } = true;

    public SandboxBillingOptions SandboxBilling { get; } = new();

    public AnalyticsOptions Analytics { get; } = new();

    internal static bool IsSandboxForced =>
        Environment.GetEnvironmentVariable("TRIMIX_SANDBOX_BILLING") == "1";

    internal const bool IsDebugBuild =
#if DEBUG
        true;
#else
        false;
#endif
}

/// <summary>DI bootstrap for all Core services, called from the MAUI app's MauiProgram.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers everything Trimix.Core provides (serverless, no accounts/auth): local analytics sink,
    /// on-device entitlements, sandbox billing (Debug default), the gated cloud/push connector families,
    /// the generic import pipeline, local store, and all viewmodels.
    ///
    /// Platform abstractions (<see cref="ISecureStore"/>, <see cref="IPreferences"/>,
    /// <see cref="IConnectivityService"/>, <see cref="ILocationService"/>, <see cref="IAppInfo"/>,
    /// <see cref="INavigator"/>, <see cref="IUserDialogs"/>) are TryAdd-ed with dev/test fallbacks —
    /// the App registers its real implementations BEFORE calling this.
    /// </summary>
    public static IServiceCollection AddTrimixCore(this IServiceCollection services, Action<TrimixCoreOptions>? configure = null)
    {
        var options = new TrimixCoreOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);
        services.AddSingleton(options.Analytics);
        services.AddSingleton(options.SandboxBilling);

        // Logging fallback so Core resolves without a host logging setup.
        services.TryAddSingleton<ILoggerFactory, NullLoggerFactory>();
        services.TryAddSingleton(typeof(ILogger<>), typeof(Logger<>));

        // Platform abstractions — dev/test fallbacks only (see summary).
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<ISecureStore, InMemorySecureStore>();
        services.TryAddSingleton<IPreferences, InMemoryPreferences>();
        services.TryAddSingleton<IConnectivityService, AlwaysOnlineConnectivity>();
        services.TryAddSingleton<ILocationService, NullLocationService>();
        services.TryAddSingleton<IMapLauncher, NullMapLauncher>();
        services.TryAddSingleton<IHaptics, NullHaptics>();
        services.TryAddSingleton<IAppInfo, DefaultAppInfo>();
        services.TryAddSingleton<INavigator, NoopNavigator>();
        services.TryAddSingleton<IUserDialogs, NoopUserDialogs>();

        // Local store: SQLite when a path is configured, in-memory otherwise.
        if (options.LocalDatabasePath is { } databasePath)
        {
            services.AddSingleton(new SqliteLocalStoreOptions { DatabasePath = databasePath });
            services.TryAddSingleton<ILocalStore, SqliteLocalStore>();
        }
        else
        {
            services.TryAddSingleton<ILocalStore, InMemoryLocalStore>();
        }

        // Single source of truth for the analytics pseudo-id.
        services.TryAddSingleton<IUserPseudoIdProvider, UserPseudoIdProvider>();

        // Rails: local analytics sink + on-device entitlements + billing (sandbox is the dev live path).
        services.TryAddSingleton<IAnalytics, AnalyticsService>();
        services.TryAddSingleton<IEntitlementService, EntitlementService>();
        if (options.UseSandboxBilling)
        {
            // Persist owned SKUs via IPreferences so on-device restore survives process restart/reinstall.
            services.TryAddSingleton<IBillingService>(sp => new SandboxBillingService(
                sp.GetRequiredService<SandboxBillingOptions>(),
                sp.GetRequiredService<IPreferences>()));
        }

        // ── Aggregator connector framework (docs/design/aggregator-architecture.md) ──
        // Shared connector infra ports — TryAdd fallbacks; the App registers platform-backed impls first.
        // HttpClientTransport owns its own HttpClient (connectors hit absolute vendor URLs — the app has no
        // backend of its own). The FIT decoder (Connectors/Fit) is the byte-level port the cloud connectors use.
        services.TryAddSingleton<IHttpTransport>(_ => new HttpClientTransport());
        services.TryAddSingleton<ISecureTokenStore, SecureStoreTokenStore>();
        services.TryAddSingleton<IConnectorConsentStore, PreferencesConnectorConsentStore>();
        services.TryAddSingleton<IFitActivityParser, FitActivityParser>();

        // Reverse-engineered cloud-pull source connectors (Connectors/Cloud). Every descriptor stays
        // Legal=ToSRiskReverseEng + EnabledByDefault=false (the structural dark-descriptor invariant):
        // AuthenticateAsync returns NeedsInteraction and does ZERO network I/O until
        // IConnectorConsentStore.HasConsent is true. Since the serverless rebuild the family registers by
        // default (EnableCloudConnectors defaults ON — the connectors ARE the product surface, shown in the
        // Providers list); availability comes from registration + the per-user consent/disclosure gate, never
        // from the descriptor flag. The master switch remains the deploy kill-switch: OFF unregisters the
        // family entirely. Mares reads through the reverse-engineered MySSI corridor (MySsiCloudClient);
        // ScubaPro reads LogTRAK's own Firebase backend (verified live 2026-08-07 — it does NOT use MySSI;
        // see docs/reverse-engineering/scubapro-logtrak-api.md), constructing its own FirebaseCloudClient.
        services.TryAddSingleton<MySsiCloudClient>();
        if (options.EnableCloudConnectors)
        {
            services.AddSingleton<IDiveSourceConnector, GarminCloudSourceConnector>();
            services.AddSingleton<IDiveSourceConnector, SuuntoCloudSourceConnector>();
            services.AddSingleton<IDiveSourceConnector, OceanicCloudSourceConnector>();
            services.AddSingleton<IDiveSourceConnector, MaresCloudSourceConnector>();
            services.AddSingleton<IDiveSourceConnector, ScubaProCloudSourceConnector>();
            // Shearwater ships as a dark "Coming soon" row only (no corridor exists yet — Shearwater Cloud is
            // desktop/BLE). Not in ProviderCatalog.LiveConnectorIds, so it is never connectable.
            services.AddSingleton<IDiveSourceConnector, ShearwaterCloudSourceConnector>();
        }

        // Reverse-engineered cert-agency PUSH destinations (Connectors/Push): PADI + SSI personal export.
        // Descriptors stay Legal=ToSRiskReverseEng + EnabledByDefault=false with null endpoints (report
        // Unsupported) until deploy config supplies real ones. Consent/token/label guardrails are structural
        // in GatedPushConnectorBase. The family registers by default (EnablePushConnectors defaults ON, same
        // promoted-surface semantics as the cloud family); the switch remains the deploy kill-switch. The
        // clean, ToS-safe MySSI QR export is unaffected — it lives on the VM, not on these connectors.
        services.TryAddSingleton<PadiPushOptions>();
        services.TryAddSingleton<SsiPushOptions>();
        // DL7 renderer retained from the removed file-export branch SOLELY as the PADI push payload
        // format (PADI only ever ingested DL7). Registered unconditionally (cheap, pure).
        services.TryAddSingleton<Dl7DiveExporter>();
        if (options.EnablePushConnectors)
        {
            services.AddSingleton<IDiveDestinationConnector, PadiPushDestinationConnector>();
            services.AddSingleton<IDiveDestinationConnector, SsiPushDestinationConnector>();
        }


        // User-defined provider priority (merge tie-break / which computer wins overlapping dives). Default
        // order = source-connector registration order, so it mirrors the Providers list before any reorder.
        services.TryAddSingleton<IProviderPriority, ProviderPriorityService>();

        services.TryAddSingleton<IImportService, ImportService>();

        // The generic "Sync now" coordinator — the production caller of the import pipeline. Pulls every
        // consented+authenticated source incrementally (persisted per-connector cursor), then pushes the
        // newly-imported dives to every consented destination. Phase-3 UI binds its SyncProgress stream.
        services.TryAddSingleton<ISyncCoordinator, SyncCoordinator>();

        // Connector privacy purge (docs/design/aggregator-architecture.md §9.4). Trimix has no accounts, so
        // there is no sign-out/identity edge to subscribe to: the "Reset app" flow on the You tab
        // (StatsViewModel) AWAITs its PurgeAllAsync explicitly. Interface + concrete map to the same singleton.
        services.TryAddSingleton<ConnectorPurgeService>();
        services.TryAddSingleton<IConnectorPurgeService>(sp => sp.GetRequiredService<ConnectorPurgeService>());

        services.TryAddSingleton<UnitFormatter>();
        services.TryAddSingleton<AppStartupService>();

        // The app-wide "Sync now" session: a SINGLETON view surface so the add-sheet, the Providers
        // radar button and the streaming sync bottom sheet all observe the same run.
        services.AddSingleton<SyncSessionViewModel>();

        // Viewmodels: transient — one per page instance.
        services.AddTransient<OnboardingViewModel>();
        services.AddTransient<LogbookViewModel>();
        services.AddTransient<DiveDetailViewModel>();
        services.AddTransient<StatsViewModel>();
        services.AddTransient<PaywallViewModel>();
        // Singleton (not transient): it subscribes to the singleton SyncSessionViewModel's
        // PropertyChanged for the rows' "Syncing…" state — a transient VM would leak one dead
        // instance per page visit through that subscription. LoadAsync rebuilds rows per appearance.
        services.AddSingleton<ConnectedAccountsViewModel>();
        services.AddTransient<ProviderOAuthViewModel>();

        return services;
    }
}
