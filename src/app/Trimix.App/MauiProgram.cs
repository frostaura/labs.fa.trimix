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
            })
            .ConfigureMauiHandlers(handlers =>
            {
                // Native backdrop blur for the bottom sheet's frosted backdrop (real UIVisualEffectView
                // on iOS; no-op on Android where the scrim carries the frost).
#if IOS
                handlers.AddHandler<Controls.BlurView, Platforms.iOS.BlurViewHandler>();
#elif ANDROID
                handlers.AddHandler<Controls.BlurView, Platforms.Android.BlurViewHandler>();
#endif
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

        // ---- Billing (serverless: entitlements are granted ON-DEVICE from store receipts) -------
        // Debug: Core's SandboxBillingService (TryAdd inside AddTrimixCore). Release: the real store adapters.
        var coreDefaults = new TrimixCoreOptions();
#if ANDROID
        if (!coreDefaults.UseSandboxBilling)
        {
            builder.Services.AddSingleton<IBillingService, Billing.PlayBillingService>();
        }
#elif IOS
		if (!coreDefaults.UseSandboxBilling)
		{
			builder.Services.AddSingleton<IBillingService, Billing.StoreKitBillingService>();
		}
#else
		// Other platforms (e.g. Mac Catalyst): deterministic Core implementation so DI always resolves.
		if (!coreDefaults.UseSandboxBilling)
		{
			builder.Services.AddSingleton<IBillingService, Trimix.Core.Billing.SandboxBillingService>();
		}
#endif

        // ---- External client config (deploy inputs) -------------------------------------------
        // Load Resources/Raw/appsettings.json (safe dev defaults; missing/malformed => defaults, never
        // throws). Every reverse-eng endpoint here is a DEPLOY INPUT gated on legal sign-off; the shipped
        // asset carries null endpoints so the connectors stay dark. There is no API/BLE config any more —
        // the app is serverless and BLE was removed.
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

        // Map the generic per-vendor cloud config bags onto CloudSourceOptions' vendor-specific slots.
        // A registered instance makes the optional CloudSourceOptions ctor param resolve; null fields fall
        // through to each connector's in-source researched constant.
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
            MySsiLoginUrl = clientConfig.Connectors.Cloud.MySsi.AuthUrl,
            MySsiDivesUrl = clientConfig.Connectors.Cloud.MySsi.ListUrl,
        });

        // ---- Core (viewmodels, rails, import pipeline, local store) ---------------------------
        builder.Services.AddTrimixCore(options =>
        {
            options.LocalDatabasePath = Path.Combine(FileSystem.AppDataDirectory, "trimix.db3");

            // Reverse-eng connector family master switches. The shipped appsettings.json sets both TRUE
            // (serverless rebuild: the connectors are the primary product surface — all 7 providers appear
            // in the Providers list). Per-user consent behind each connector's plain-language ToS-risk
            // disclosure remains the only thing that permits any network I/O; these switches remain the
            // deploy kill-switch that unregisters a family entirely. NOTE: a missing/corrupt config asset
            // parses to Features.*=false (safe fallback) which turns the families OFF, not on.
            options.EnableCloudConnectors = clientConfig.Features.EnableCloudConnectors;
            options.EnablePushConnectors = clientConfig.Features.EnablePushConnectors;
        });

        // ---- Pages (Shell resolves routed pages through DI; viewmodels constructor-inject) ----
        builder.Services.AddTransient<OnboardingPage>();
        builder.Services.AddTransient<LogbookPage>();
        builder.Services.AddTransient<StatsPage>();
        builder.Services.AddTransient<DiveDetailPage>();
        builder.Services.AddTransient<PaywallPage>();
        builder.Services.AddTransient<ConnectedAccountsPage>();
        builder.Services.AddTransient<ProviderOAuthPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        var app = builder.Build();

        return app;
    }
}
