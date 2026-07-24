using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Trimix.Core.Abstractions;
using Trimix.Core.Analytics;
using Trimix.Core.Api;
using Trimix.Core.Auth;
using Trimix.Core.Billing;
using Trimix.Core.Connectors.Cloud;
using Trimix.Core.Connectors.Export;
using Trimix.Core.Connectors.FileImport;
using Trimix.Core.Connectors.Infra;
using Trimix.Core.Connectors.Push;
using Trimix.Core.DiveComputer;
using Trimix.Core.DiveComputer.Protocols;
using Trimix.Core.Entitlements;
using Trimix.Core.Formatting;
using Trimix.Core.Platform;
using Trimix.Core.Privacy;
using Trimix.Core.Storage;
using Trimix.Core.Sync;
using Trimix.Core.ViewModels;

namespace Trimix.Core;

/// <summary>Host-configurable knobs for <see cref="ServiceCollectionExtensions.AddTrimixCore"/>.</summary>
public sealed class TrimixCoreOptions
{
    /// <summary>Base address of the Trimix API (default: the API's dev launch profile).</summary>
    public Uri ApiBaseAddress { get; set; } = new("http://localhost:5210");

    /// <summary>SQLite file path for <see cref="SqliteLocalStore"/>; null keeps <see cref="InMemoryLocalStore"/>.</summary>
    public string? LocalDatabasePath { get; set; }

    /// <summary>
    /// Sandbox billing (architecture overview §2): default in Debug, and forceable anywhere via
    /// <c>TRIMIX_SANDBOX_BILLING=1</c>. When false the App must register its store billing service.
    /// </summary>
    public bool UseSandboxBilling { get; set; } = IsDebugBuild || IsSandboxForced;

    /// <summary>Simulator transport (architecture overview §4): default in Debug. When false the App registers BleTransport.</summary>
    public bool UseSimulatorTransport { get; set; } = IsDebugBuild;

    /// <summary>
    /// Deploy master switch for the reverse-engineered cloud-pull connector family (Garmin/Suunto/Oceanic).
    /// Default OFF: the family is NOT registered — so it never surfaces in Connected accounts, can never be
    /// consented, and sync can never pull it — until legal/BD sign-off flips this. A deploy gate ON TOP OF
    /// the per-connector consent gate (mapped from appsettings <c>Features.EnableCloudConnectors</c>).
    /// </summary>
    public bool EnableCloudConnectors { get; set; }

    /// <summary>
    /// Deploy master switch for the reverse-engineered cert-agency PUSH connector family (PADI/SSI). Default
    /// OFF, same "not even registered until legal sign-off" semantics as <see cref="EnableCloudConnectors"/>
    /// (mapped from appsettings <c>Features.EnablePushConnectors</c>).
    /// </summary>
    public bool EnablePushConnectors { get; set; }

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
    /// Registers everything Trimix.Core provides: analytics pipe, typed API client, auth session,
    /// entitlements, sandbox billing + simulator transport (Debug defaults), the four vendor
    /// protocols, import/sync services, local store, and all viewmodels.
    ///
    /// Platform abstractions (<see cref="ISecureStore"/>, <see cref="IPreferences"/>,
    /// <see cref="IConnectivityService"/>, <see cref="ILocationService"/>, <see cref="IPermissions"/>,
    /// <see cref="IAppInfo"/>, <see cref="INavigator"/>, <see cref="IUserDialogs"/>) are TryAdd-ed
    /// with dev/test fallbacks — the App registers its real implementations BEFORE calling this.
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
        services.TryAddSingleton<IPermissions, AlwaysGrantedPermissions>();
        services.TryAddSingleton<IAppInfo, DefaultAppInfo>();
        services.TryAddSingleton<INavigator, NoopNavigator>();
        services.TryAddSingleton<IUserDialogs, NoopUserDialogs>();
        // Passkey authenticator: the App registers the iOS/Android platform impl before this; the
        // fallback keeps DI + tests resolvable on platforms without a passkey authenticator.
        services.TryAddSingleton<IPasskeyAuthService, UnsupportedPasskeyAuthService>();

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

        // Single source of truth for the analytics pseudo-id (shared by the analytics pipe and the
        // API client's UserPseudoId header) so server-emitted funnel events stitch to the device funnel.
        services.TryAddSingleton<IUserPseudoIdProvider, UserPseudoIdProvider>();

        // Post-purchase retry queue (store-charged but validation failed).
        services.TryAddSingleton<IPendingPurchaseStore, PendingPurchaseStore>();

        // API client (typed wrapper over one HttpClient).
        services.TryAddSingleton(_ => new HttpClient { BaseAddress = options.ApiBaseAddress });
        services.TryAddSingleton<IApiClient, ApiClient>();

        // Rails: analytics + auth + entitlements + billing (sandbox is the slice's live path).
        services.TryAddSingleton<IAnalytics, AnalyticsService>();
        services.TryAddSingleton<IAuthSession, AuthSession>();
        services.TryAddSingleton<IEntitlementService, EntitlementService>();
        if (options.UseSandboxBilling)
        {
            // Persist owned SKUs via IPreferences so on-device restore survives process restart/reinstall.
            services.TryAddSingleton<IBillingService>(sp => new SandboxBillingService(
                sp.GetRequiredService<SandboxBillingOptions>(),
                sp.GetRequiredService<IPreferences>()));
        }

        // Dive-computer import stack: transport + the four-wide protocol abstraction.
        if (options.UseSimulatorTransport)
        {
            services.TryAddSingleton<IDiveComputerTransport, SimulatorTransport>();
        }

        // Six real clean-room BLE parsers (Shearwater in DiveComputer; Suunto/Mares/ScubaPro/Aqualung in
        // DiveComputer.Protocols) + Garmin, which stays a NotSupported placeholder (no third-party BLE download).
        services.AddSingleton<IDiveComputerProtocol, ShearwaterProtocol>();
        services.AddSingleton<IDiveComputerProtocol, SuuntoProtocol>();
        services.AddSingleton<IDiveComputerProtocol, GarminProtocol>();
        services.AddSingleton<IDiveComputerProtocol, MaresProtocol>();
        services.AddSingleton<IDiveComputerProtocol, ScubaProProtocol>();
        services.AddSingleton<IDiveComputerProtocol, AqualungProtocol>();

        // BLE is demoted to one source connector (architecture overview §2); it wraps the transport +
        // the protocols above. Singleton: holds per-session state, matching single-import-at-a-time.
        // ImportService resolves connectors via IEnumerable<IDiveSourceConnector> + OfType filtering.
        services.AddSingleton<IDiveSourceConnector, BleDeviceSourceConnector>();

        // ── Aggregator connector framework (docs/design/aggregator-architecture.md) ──
        // Shared connector infra ports — TryAdd fallbacks; the App registers platform-backed impls first.
        // HttpClientTransport owns its own HttpClient (connectors hit absolute vendor URLs, not the Trimix API).
        // The FIT decoder adapts the file-import GarminFitParser to the byte-level port the cloud connectors use.
        services.TryAddSingleton<IHttpTransport>(_ => new HttpClientTransport());
        services.TryAddSingleton<ISecureTokenStore, SecureStoreTokenStore>();
        services.TryAddSingleton<IConnectorConsentStore, PreferencesConnectorConsentStore>();
        services.TryAddSingleton<IFitActivityParser, FitActivityParser>();

        // File-import source connector + format parsers (Connectors/FileImport). The App registers the real
        // MauiFilePicker before AddTrimixCore, so this TryAdd fallback engages only in dev/tests.
        services.TryAddSingleton<IFilePicker, UnsupportedFilePicker>();
        services.AddSingleton<IDiveFileParser, GarminFitParser>();
        services.AddSingleton<IDiveFileParser, UddfParser>();
        services.AddSingleton<IDiveFileParser, DanDl7Parser>();
        services.AddSingleton<IDiveFileParser, SuuntoJsonParser>();
        services.AddSingleton<IDiveSourceConnector, FileImportSourceConnector>();

        // HealthKit (apple_health) is iOS-only and needs a platform IHealthStore, so it is registered in the
        // App's iOS bootstrap (#if IOS), NOT here — registering it in Core would fail DI validation on Android.

        // Reverse-engineered cloud-pull source connectors (Connectors/Cloud). Every one ships DARK
        // (Legal=ToSRiskReverseEng, EnabledByDefault=false): AuthenticateAsync returns NeedsInteraction and does
        // ZERO network I/O until IConnectorConsentStore.HasConsent is true. The family is ALSO gated behind the
        // deploy master switch: with EnableCloudConnectors OFF (the ship default) it is not registered at all,
        // so it never appears in Connected accounts, can never be consented, and sync can never pull it — the
        // legal/BD deploy gate ON TOP OF the per-connector consent gate. These carry live in-source vendor
        // endpoints, so "dark by null endpoint" does not hold for this family — the flag is the real gate.
        if (options.EnableCloudConnectors)
        {
            services.AddSingleton<IDiveSourceConnector, GarminCloudSourceConnector>();
            services.AddSingleton<IDiveSourceConnector, SuuntoCloudSourceConnector>();
            services.AddSingleton<IDiveSourceConnector, OceanicCloudSourceConnector>();
        }

        // Clean, ToS-safe file-export destinations (Connectors/Export): one connector per standards-based
        // format the diver controls (UDDF / DL7 / PDF). The export sink's real impl (share-sheet / save) lives
        // in the App; Core ships an in-memory fallback for dev/tests.
        services.AddSingleton<UddfDiveExporter>();
        services.AddSingleton<Dl7DiveExporter>();
        services.AddSingleton<MinimalPdfDiveExporter>();
        services.TryAddSingleton<IExportFileSink, InMemoryExportFileSink>();
        services.AddSingleton<IDiveDestinationConnector>(sp =>
            new FileExportDestinationConnector(sp.GetRequiredService<UddfDiveExporter>(), sp.GetRequiredService<IExportFileSink>()));
        services.AddSingleton<IDiveDestinationConnector>(sp =>
            new FileExportDestinationConnector(sp.GetRequiredService<Dl7DiveExporter>(), sp.GetRequiredService<IExportFileSink>()));
        services.AddSingleton<IDiveDestinationConnector>(sp =>
            new FileExportDestinationConnector(sp.GetRequiredService<MinimalPdfDiveExporter>(), sp.GetRequiredService<IExportFileSink>()));

        // Reverse-engineered cert-agency PUSH destinations (Connectors/Push): PADI + SSI personal export.
        // Ship DARK (Legal=ToSRiskReverseEng, EnabledByDefault=false) with null endpoints (report Unsupported)
        // until legal/BD supply a reverse-engineered endpoint via the options. Consent/token/label guardrails
        // are structural in GatedPushConnectorBase. Also gated behind the deploy master switch: with
        // EnablePushConnectors OFF (the ship default) the family is not registered at all (same legal/BD deploy
        // gate as the cloud family). The clean, ToS-safe MySSI QR export is unaffected — it lives on the VM, not
        // on these connectors.
        services.TryAddSingleton<PadiPushOptions>();
        services.TryAddSingleton<SsiPushOptions>();
        if (options.EnablePushConnectors)
        {
            services.AddSingleton<IDiveDestinationConnector, PadiPushDestinationConnector>();
            services.AddSingleton<IDiveDestinationConnector, SsiPushDestinationConnector>();
        }

        services.TryAddSingleton<IImportService, ImportService>();

        services.TryAddSingleton<ISyncService, SyncService>();

        // Connector privacy purge (docs/design/aggregator-architecture.md §9.4). Its constructor subscribes to
        // IAuthSession.AuthStateChanged + IdentityChanged, so — like SyncService — it MUST be resolved once at
        // startup or it never subscribes; the App forces that resolution after building the graph. The interface
        // is what the sign-out/GDPR-delete flow (SettingsViewModel) and the unauthenticated-startup
        // reconciliation (AppStartupService) AWAIT — both map to the same singleton instance.
        services.TryAddSingleton<ConnectorPurgeService>();
        services.TryAddSingleton<IConnectorPurgeService>(sp => sp.GetRequiredService<ConnectorPurgeService>());

        services.TryAddSingleton<UnitFormatter>();
        services.TryAddSingleton<AppStartupService>();

        // Viewmodels: transient — one per page instance.
        services.AddTransient<OnboardingViewModel>();
        services.AddTransient<AuthViewModel>();
        services.AddTransient<LogbookViewModel>();
        services.AddTransient<DiveDetailViewModel>();
        services.AddTransient<ExportDiveViewModel>();
        services.AddTransient<StatsViewModel>();
        services.AddTransient<ManualEntryViewModel>();
        services.AddTransient<ImportViewModel>();
        services.AddTransient<PaywallViewModel>();
        services.AddTransient<SitesViewModel>();
        services.AddTransient<SiteDetailViewModel>();
        services.AddTransient<ContributeViewModel>();
        services.AddTransient<SettingsViewModel>();

        return services;
    }
}
