using Microsoft.Extensions.Logging;
using Trimix.App.Pages;
using Trimix.App.Services;
using Trimix.Core;
using Trimix.Core.Abstractions;

namespace Trimix.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                // Storyboard (Sonar A) typography, exact per Wreckline Storyboard.dc.html:
                //   Space Grotesk  -> headings, titles, button labels
                //   JetBrains Mono -> wordmark, tracked UPPERCASE eyebrows, badges, index + stat numerals
                //   Hanken Grotesk -> body copy, captions, quotes
                // OpenSans kept only as a last-resort fallback.
                fonts.AddFont("SpaceGrotesk-Light.ttf", "SpaceGroteskLight");
                fonts.AddFont("SpaceGrotesk-Regular.ttf", "SpaceGroteskRegular");
                fonts.AddFont("SpaceGrotesk-Medium.ttf", "SpaceGroteskMedium");
                fonts.AddFont("SpaceGrotesk-Bold.ttf", "SpaceGroteskBold");
                fonts.AddFont("JetBrainsMono-Regular.ttf", "JetBrainsMono");
                fonts.AddFont("JetBrainsMono-Medium.ttf", "JetBrainsMonoMedium");
                fonts.AddFont("JetBrainsMono-SemiBold.ttf", "JetBrainsMonoSemiBold");
                fonts.AddFont("JetBrainsMono-Bold.ttf", "JetBrainsMonoBold");
                fonts.AddFont("HankenGrotesk-Regular.ttf", "HankenGrotesk");
                fonts.AddFont("HankenGrotesk-Medium.ttf", "HankenGroteskMedium");
                fonts.AddFont("HankenGrotesk-SemiBold.ttf", "HankenGroteskSemiBold");
                fonts.AddFont("HankenGrotesk-Bold.ttf", "HankenGroteskBold");
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

#if ANDROID
        // Instrument numerals: tabular figures (design doc "FontFeatures tnum") for Labels that
        // carry StyleClass="tnum" (the StatNumeral* styles' companion class).
        Microsoft.Maui.Handlers.LabelHandler.Mapper.AppendToMapping("TrimixTabularNumerals", (handler, label) =>
        {
            if (label is Label { StyleClass: { } classes } && classes.Contains("tnum"))
            {
                handler.PlatformView.FontFeatureSettings = "tnum";
            }
        });
#endif

        // ---- Platform implementations of Core's abstractions ----------------------------------
        // Registered BEFORE AddTrimixCore so Core's TryAdd dev/test fallbacks never engage.
        builder.Services.AddSingleton<ISecureStore, MauiSecureStore>();
        builder.Services.AddSingleton<Trimix.Core.Abstractions.IPreferences, MauiPreferencesService>();
        builder.Services.AddSingleton<IConnectivityService, MauiConnectivityService>();
        builder.Services.AddSingleton<ILocationService, MauiLocationService>();
        builder.Services.AddSingleton<Trimix.Core.Abstractions.IAppInfo, MauiAppInfoService>();
        builder.Services.AddSingleton<INavigator, ShellNavigator>();
        builder.Services.AddSingleton<IUserDialogs, MauiUserDialogs>();
        builder.Services.AddSingleton<Trimix.Core.Abstractions.IPermissions, BlePermissionService>();

        // File-import picker (Connectors/FileImport): the real MAUI FilePicker over Android SAF / iOS
        // UIDocumentPicker. Registered before AddTrimixCore so Core's UnsupportedFilePicker fallback never engages.
        builder.Services.AddSingleton<Trimix.Core.Connectors.FileImport.IFilePicker, MauiFilePicker>();

        // Slice 1 — real export file sink (writes exporter bytes to cache, hands the file to the OS share
        // sheet / "Save to Files"). Registered before AddTrimixCore so it OVERRIDES Core's TryAdd
        // InMemoryExportFileSink on device, and the three FileExportDestinationConnector registrations
        // resolve the real sink. No cloud, no credentials — LegalPosture.UserFile only.
        builder.Services.AddSingleton<Trimix.Core.Connectors.Export.IExportFileSink, MauiExportFileSink>();

        // Slice 2 — Connected-accounts activation UI dependencies: a dependency-free QR encoder and the
        // SSI QR payload builder (neither is DI-registered in Core, and the ConnectedAccountsViewModel
        // needs both). Kept in the App graph so Core's AddTrimixCore ValidateOnBuild stays resolvable.
        builder.Services.AddSingleton<Trimix.Core.Connectors.Push.IQrEncoder, Trimix.Core.Connectors.Push.BasicQrEncoder>();
        builder.Services.AddSingleton<Trimix.Core.Connectors.Push.SsiQrPayloadBuilder>();

#if IOS
        // Apple HealthKit source connector (iOS-only, on-device consent, depth+temp). The platform IHealthStore
        // and the Core connector are wired HERE — never in AddTrimixCore — because Core targets net10.0 with no
        // HealthKit surface and there is no IHealthStore on Android (registering it in Core would fail DI
        // validation there). AddSingleton is additive, so on iOS the IDiveSourceConnector enumerable carries it.
        builder.Services.AddSingleton<Trimix.Core.Connectors.HealthKit.IHealthStore, Trimix.App.Health.HealthKitHealthStore>();
        builder.Services.AddSingleton<IDiveSourceConnector, Trimix.Core.Connectors.HealthKit.HealthStoreSourceConnector>();
#endif
#if ANDROID || IOS
        // Passkey (WebAuthn) authenticator — platform-native (iOS AuthenticationServices / Android
        // Credential Manager). Registered before AddTrimixCore so the Core unavailable-fallback never engages.
        builder.Services.AddSingleton<IPasskeyAuthService, Auth.PasskeyAuthService>();
#endif

        // ---- DI flags (architecture overview §2/§4) --------------------------------------------
        // Debug: SimulatorTransport + SandboxBillingService (the slice's live purchase path).
        // Release: Android registers the real adapters — BleTransport (skeleton) + PlayBillingService
        // (real Play Billing wiring, runtime-inert until a Play listing exists).
        var coreDefaults = new TrimixCoreOptions();
#if ANDROID
        if (!coreDefaults.UseSimulatorTransport)
        {
            builder.Services.AddSingleton<IDiveComputerTransport, Ble.BleTransport>();
        }

        if (!coreDefaults.UseSandboxBilling)
        {
            builder.Services.AddSingleton<IBillingService, Billing.PlayBillingService>();
        }
#elif IOS
		// iOS: native CoreBluetooth transport + StoreKit billing (Release only; Debug keeps the Core
		// SimulatorTransport + SandboxBillingService via Core's TryAdd below). Same Ble.BleTransport /
		// Billing.StoreKitBillingService class names as the Android twins keep the wiring uniform.
		if (!coreDefaults.UseSimulatorTransport)
		{
			builder.Services.AddSingleton<IDiveComputerTransport, Ble.BleTransport>();
		}

		if (!coreDefaults.UseSandboxBilling)
		{
			builder.Services.AddSingleton<IBillingService, Billing.StoreKitBillingService>();
		}
#else
		// Other platforms (e.g. Mac Catalyst): deterministic Core implementations so DI always resolves.
		if (!coreDefaults.UseSimulatorTransport)
		{
			builder.Services.AddSingleton<IDiveComputerTransport, Trimix.Core.DiveComputer.SimulatorTransport>();
		}

		if (!coreDefaults.UseSandboxBilling)
		{
			builder.Services.AddSingleton<IBillingService, Trimix.Core.Billing.SandboxBillingService>();
		}
#endif

        // ---- Slice 4 — external client config (deploy inputs) ---------------------------------
        // Load Resources/Raw/appsettings.json (safe dev defaults; missing/malformed => defaults, never
        // throws). Every reverse-eng endpoint + BLE UUID here is a DEPLOY INPUT gated on legal / name
        // clearance; the shipped asset carries null endpoints so the connectors stay dark.
        var clientConfig = AppConfigLoader.LoadAsync().GetAwaiter().GetResult();

        // Pre-register the connector option objects BEFORE AddTrimixCore so its TryAdd fallbacks don't win.
        // Null fields keep each connector's in-source dark default (PADI/SSI report Unsupported; cloud uses
        // its researched-const fallback) — no guessed value is presented as verified.
        builder.Services.AddSingleton(new Trimix.Core.Connectors.Push.PadiPushOptions
        {
            AuthUrl = clientConfig.Connectors.Padi.AuthUrl,
            PushUrl = clientConfig.Connectors.Padi.PushUrl,
        });
        builder.Services.AddSingleton(new Trimix.Core.Connectors.Push.SsiPushOptions
        {
            AuthUrl = clientConfig.Connectors.Ssi.AuthUrl,
            PushUrl = clientConfig.Connectors.Ssi.PushUrl,
        });

        // Slice 2 — map the generic per-vendor cloud config bags onto CloudSourceOptions' vendor-specific
        // slots. A registered instance makes the optional CloudSourceOptions ctor param resolve; null
        // fields fall through to each connector's in-source researched constant.
        builder.Services.AddSingleton(new Trimix.Core.Connectors.Cloud.CloudSourceOptions
        {
            GarminSigninUrl = clientConfig.Connectors.Cloud.Garmin.AuthUrl,
            GarminTokenExchangeUrl = clientConfig.Connectors.Cloud.Garmin.TokenUrl,
            GarminActivitySearchUrl = clientConfig.Connectors.Cloud.Garmin.ListUrl,
            GarminDownloadUrl = clientConfig.Connectors.Cloud.Garmin.DownloadUrl,
            SuuntoTokenUrl = clientConfig.Connectors.Cloud.Suunto.TokenUrl ?? clientConfig.Connectors.Cloud.Suunto.AuthUrl,
            SuuntoWorkoutsUrl = clientConfig.Connectors.Cloud.Suunto.ListUrl,
            SuuntoExportFitUrl = clientConfig.Connectors.Cloud.Suunto.DownloadUrl,
            OceanicLoginUrl = clientConfig.Connectors.Cloud.Oceanic.AuthUrl,
            OceanicDivesUrl = clientConfig.Connectors.Cloud.Oceanic.ListUrl,
        });

        // Slice 3 — merge deploy-verified per-vendor BLE GATT overrides (service/char UUIDs, handshake
        // hex) into the static profile table. Empty by default (only Shearwater's public service UUID is
        // pinned in-source); real per-vendor UUIDs/handshakes remain hardware-verified deploy inputs.
        Trimix.Core.DiveComputer.BleGattOverrides.Apply(clientConfig.Ble);

        // ---- Core (viewmodels, rails, import/sync, local store) -------------------------------
        builder.Services.AddTrimixCore(options =>
        {
            options.LocalDatabasePath = Path.Combine(FileSystem.AppDataDirectory, "trimix.db3");

            // Slice 4 — reverse-eng connector family master switches (deploy gate ON TOP OF per-connector
            // consent). Both ship OFF (Features.* default false): a disabled family is not registered, so it
            // never surfaces in Connected accounts, can never be consented, and sync can never pull it — the
            // legal/BD kill-switch the shipped appsettings.json documents. Flip ONLY after legal sign-off.
            options.EnableCloudConnectors = clientConfig.Features.EnableCloudConnectors;
            options.EnablePushConnectors = clientConfig.Features.EnablePushConnectors;
#if DEBUG
            // Dev-only cleartext loopback: the Android emulator sees the host's loopback as 10.0.2.2
            // (docker-compose API on localhost:5210). Physical devices need the host LAN address —
            // dev-loop concern, documented in the README. Permitted by network_security_config's
            // loopback domain-config; Release cannot reach these over cleartext.
            options.ApiBaseAddress = new Uri(
                DeviceInfo.Current.Platform == DevicePlatform.Android && DeviceInfo.Current.DeviceType == DeviceType.Virtual
                    ? "http://10.0.2.2:5210"
                    : "http://localhost:5210");
#else
            // Release ships https-only (name-clearance gate sets the real host before a store listing).
            options.ApiBaseAddress = new Uri("https://api.trimix.frostaura.net");
#endif

            // Slice 4 — a configured Api.BaseUrl (deploy input) wins over the compiled default above;
            // null/empty keeps the code default so a missing asset never changes behaviour.
            if (clientConfig.Api.BaseUrl is { Length: > 0 } configuredBaseUrl)
            {
                options.ApiBaseAddress = new Uri(configuredBaseUrl);
            }
        });

        // ---- Pages (Shell resolves routed pages through DI; viewmodels constructor-inject) ----
        builder.Services.AddTransient<OnboardingPage>();
        builder.Services.AddTransient<AuthPage>();
        builder.Services.AddTransient<LogbookPage>();
        builder.Services.AddTransient<SitesPage>();
        builder.Services.AddTransient<StatsPage>();
        builder.Services.AddTransient<ImportPage>();
        builder.Services.AddTransient<SettingsPage>();
        builder.Services.AddTransient<ManualEntryPage>();
        builder.Services.AddTransient<DiveDetailPage>();
        builder.Services.AddTransient<SiteDetailPage>();
        builder.Services.AddTransient<ContributePage>();
        builder.Services.AddTransient<PaywallPage>();
        builder.Services.AddTransient<ExportDivePage>();          // Slice 1
        builder.Services.AddTransient<ConnectedAccountsPage>();   // Slice 2

        // Slice 2 — the activation VM lives in the App graph (not AddTrimixCore) because it depends on the
        // App-registered IQrEncoder + SsiQrPayloadBuilder; keeping it here preserves Core's ValidateOnBuild.
        builder.Services.AddTransient<Trimix.Core.ViewModels.ConnectedAccountsViewModel>();

        // Slice 4 — connector privacy purge is now registered inside AddTrimixCore (concrete +
        // IConnectorPurgeService, one singleton) so the sign-out/GDPR-delete flow and the unauthenticated-
        // startup reconciliation can AWAIT it. Its constructor subscribes to IAuthSession.AuthStateChanged +
        // IdentityChanged, so it MUST still be resolved once at startup (see below) or it never subscribes.

#if DEBUG
        builder.Logging.AddDebug();
#endif

        var app = builder.Build();

        // Slice 4 — force the purge service to construct now so its sign-out + identity-change subscriptions
        // are live for the whole app lifetime (an unresolved singleton never subscribes).
        _ = app.Services.GetRequiredService<Trimix.Core.Privacy.ConnectorPurgeService>();

        return app;
    }
}
