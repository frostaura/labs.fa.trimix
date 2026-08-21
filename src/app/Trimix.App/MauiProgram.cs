using Microsoft.Extensions.Logging;
using Trimix.App.Pages;
using Trimix.App.Services;
using Trimix.Core;
using Trimix.Core.Abstractions;

namespace Trimix.App;

public static class MauiProgram
{
#if ANDROID
    /// <summary>
    /// Borders already converted to a Material filled-field container. Weak keys, so a page that goes away
    /// takes its entry with it. See the BorderHandler mapping for why once-only is mandatory and not merely
    /// an optimisation.
    /// </summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Border, Border> _inputContainersStyled = new();
#endif

    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

#if IOS
        // iOS only — the embedded entry-point map is MapKit, which is free and keyless. Android has no map
        // control registered and hands off to the Google Maps app instead (see EntryPointCard).
        builder.UseMauiMaps();
#endif

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


        // ---- Text-input chrome, per platform idiom --------------------------------------------
        // Every Entry/Editor in this app sits inside a Border. What that Border and the field should look
        // like is NOT the same on both platforms, and an earlier pass got it wrong by stripping both to a
        // bare rounded box — which is why the Android build read as an iOS app:
        //
        //   Android -> a Material FILLED field. The field draws its own bottom indicator (dim at rest,
        //              mint and 2dp on focus) and owns the 16dp horizontal gutter so that indicator spans
        //              the container's full width; the container drops its outline and squares its bottom
        //              corners (see the BorderHandler mapping below).
        //   iOS     -> a borderless field inside the app's outlined card. iOS text fields have no
        //              indicator and no focus stroke; borderless-inside-a-grouped-container is exactly how
        //              iOS forms look, and a mint underline there would look like an Android app.
        //
        // This divergence is intentional: one layout, each platform's native input language.
        Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("TrimixInputChrome", (handler, _) =>
        {
#if ANDROID
            ApplyMaterialUnderline(handler.PlatformView);
#elif IOS
            handler.PlatformView.BorderStyle = UIKit.UITextBorderStyle.None;
#endif
        });

        Microsoft.Maui.Handlers.EditorHandler.Mapper.AppendToMapping("TrimixInputChrome", (handler, _) =>
        {
#if ANDROID
            ApplyMaterialUnderline(handler.PlatformView);
#elif IOS
            // UITextView has no border, but it does carry a default text inset that misaligns it with the
            // card's padding.
            handler.PlatformView.TextContainerInset = UIKit.UIEdgeInsets.Zero;
            handler.PlatformView.TextContainer.LineFragmentPadding = 0;
#endif
        });

#if ANDROID
        // ---- Android: a text field's container is a Material FILLED field, not an outlined card ----
        // Detected by content type rather than a StyleClass so a future input cannot be missed: any Border
        // whose content is an Entry or Editor IS a text-field container.
        //
        // The outline is dropped and the shape becomes top-rounded/bottom-square, which is the Material
        // filled field: fill + bottom indicator, no outline. Keeping the rounded outline AND adding the
        // indicator is the doubled-container look — a line inside a box — that this whole change exists to
        // remove. iOS keeps the outlined card, which is its own native idiom.
        //
        // This is set from a handler rather than OnPlatform setters in the style: OnPlatform applies for
        // Padding but was observed to have no effect on StrokeThickness/StrokeShape, so the style is not a
        // reliable place to express it.
        //
        // ⚠️ IT MUST RUN EXACTLY ONCE PER BORDER. A handler mapping re-runs on every property update of its
        // view, and StrokeShape/Background are reference types — assigning a fresh RoundRectangle or
        // SolidColorBrush raises property-changed, which invalidates, which re-measures, which re-runs this
        // mapping, which assigns two more instances that can never compare equal. That is an unbounded
        // layout loop: the page looks fine at rest and hard-freezes the moment anything triggers a layout
        // pass (tapping a field raises the keyboard, which resizes the page — the reported freeze).
        // The weak table is what bounds it; do not replace it with an "only assign if different" check,
        // because a fresh brush is never equal to the previous one.
        Microsoft.Maui.Handlers.BorderHandler.Mapper.AppendToMapping("TrimixInputContainer", (handler, view) =>
        {
            _ = handler;
            if (view is not Border { Content: Entry or Editor } border
                || _inputContainersStyled.TryGetValue(border, out var alreadyStyled) && alreadyStyled is not null)
            {
                return;
            }

            _inputContainersStyled.Add(border, border);

            border.StrokeThickness = 0;
            border.StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
            {
                CornerRadius = new CornerRadius(10, 10, 0, 0),
            };
            // A filled field must be distinguishable from the page behind it, or all that is left is a
            // floating underline.
            if (Application.Current?.Resources.TryGetValue(
                    Application.Current.RequestedTheme == AppTheme.Light ? "LightSurfaceRaised" : "SurfaceRaised",
                    out var fill) == true && fill is Color fillColor)
            {
                border.Background = new SolidColorBrush(fillColor);
            }
        });
#endif

        // ---- Haptics on every Button, with no per-page wiring ----------------------------------
        // Buttons are the single biggest tap surface in the app, so they are hooked centrally here rather
        // than at ~19 call sites. Pressed (not Clicked) is deliberate: the tick must land under the
        // finger, before the command runs, or it stops reading as a response to the touch.
        Microsoft.Maui.Handlers.ButtonHandler.Mapper.AppendToMapping("TrimixHaptics", (_, view) =>
        {
            if (view is Button button)
            {
                // The mapper re-runs on every property change; Delegate.Remove matches on target+method,
                // so this stays a single subscription however many times it fires.
                button.Pressed -= OnButtonPressed;
                button.Pressed += OnButtonPressed;
            }
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
        builder.Services.AddSingleton<IMapLauncher, MauiMapLauncher>();
        builder.Services.AddSingleton<IHaptics, MauiHaptics>();
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
            // Garmin's generic bag maps onto the observed API: AuthUrl→login, ListUrl→dive summary,
            // DownloadUrl→per-dive detail (the "fetch one dive" slot; no FIT download exists on this path).
            GarminLoginUrl = clientConfig.Connectors.Cloud.Garmin.AuthUrl,
            GarminTokenUrl = clientConfig.Connectors.Cloud.Garmin.TokenUrl,
            GarminDiveSummaryUrl = clientConfig.Connectors.Cloud.Garmin.ListUrl,
            GarminDiveDetailUrl = clientConfig.Connectors.Cloud.Garmin.DownloadUrl,
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

    /// <summary>Every <see cref="Button"/> press ticks; see the ButtonHandler mapping above.</summary>
    private static void OnButtonPressed(object? sender, EventArgs e) => Feedback.Tap();

#if ANDROID
    /// <summary>
    /// Replaces Android's stock EditText background with the app's own Material bottom indicator — dim
    /// <c>GlassStroke</c> at rest, mint <c>Sonar</c> and 2dp when focused, <c>TextDisabled</c> when
    /// disabled.
    ///
    /// <para>Replacing rather than tinting is deliberate. On a Material3 host theme the stock background
    /// is a FILLED box, so tinting it paints a solid rectangle inside the GlassCard that already frames
    /// the field. See <see cref="Platforms.Android.MaterialUnderlineDrawable"/>.</para>
    ///
    /// <para>Padding is set explicitly because the stock background carried its own: zero horizontally so
    /// the text aligns with the card's padding instead of being indented twice, and 6dp under the
    /// baseline so the glyphs do not sit on the indicator.</para>
    /// </summary>
    private static void ApplyMaterialUnderline(Android.Widget.EditText field)
    {
        static Android.Graphics.Color ToPlatform(Color c) => Android.Graphics.Color.Argb(
            (int)(c.Alpha * 255), (int)(c.Red * 255), (int)(c.Green * 255), (int)(c.Blue * 255));

        if (field.Background is Platforms.Android.MaterialUnderlineDrawable)
        {
            // The mapper re-runs on every property change; rebuilding the drawable each time would drop
            // the focus state it is currently holding.
            return;
        }

        // Tokens resolve through Resources/Styles/Colors.xaml for the active theme — never hardcoded.
        var density = field.Context?.Resources?.DisplayMetrics?.Density ?? 1f;
        field.Background = new Platforms.Android.MaterialUnderlineDrawable(
            density,
            idle: ToPlatform(Controls.ThemeTokens.Get("GlassStroke")),
            accent: ToPlatform(Controls.ThemeTokens.Get("Sonar")),
            disabled: ToPlatform(Controls.ThemeTokens.Get("TextDisabled")));

        // The 16dp Material gutter lives HERE, not on the container, so the indicator (drawn as this
        // field's background) spans the container's full width. 6dp under the baseline keeps the glyphs
        // off the indicator.
        var gutter = (int)(16 * density);
        field.SetPadding(gutter, 0, gutter, (int)(6 * density));
    }
#endif
}
