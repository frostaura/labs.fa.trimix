namespace Trimix.App.Controls;

/// <summary>
/// Theme-aware access to the Sonar color tokens for code that draws (GraphicsView drawables).
/// Always resolves through Resources/Styles/Colors.xaml — never hardcode hex values in drawing
/// code; the design doc owns them.
/// </summary>
public static class ThemeTokens
{
    /// <summary>Resolves a canonical token name ("Sonar", "GlassStroke", …) for the active theme.</summary>
    public static Color Get(string tokenName)
    {
        var app = Application.Current;
        var isLight = app?.RequestedTheme == AppTheme.Light;
        var key = isLight ? "Light" + tokenName : tokenName;

        if (app?.Resources.TryGetValue(key, out var themed) == true && themed is Color themedColor)
        {
            return themedColor;
        }

        if (app?.Resources.TryGetValue(tokenName, out var fallback) == true && fallback is Color fallbackColor)
        {
            return fallbackColor;
        }

        // Resources unavailable (design-time/unit-test host): transparent, never a hardcoded token.
        return Colors.Transparent;
    }
}

/// <summary>
/// Platform reduced-motion signal. Every animation in the app (ping rings, chart draw-in) must
/// consult this (design doc: "Everything honors platform reduced-motion").
/// </summary>
public static class MotionSettings
{
    public static bool IsReducedMotionEnabled
    {
        get
        {
#if ANDROID
            try
            {
                var scale = Android.Provider.Settings.Global.GetFloat(
                    Platform.AppContext.ContentResolver,
                    Android.Provider.Settings.Global.AnimatorDurationScale,
                    1f);
                return scale == 0f;
            }
            catch
            {
                return false;
            }
#elif IOS
            return UIKit.UIAccessibility.IsReduceMotionEnabled;
#else
            return false;
#endif
        }
    }
}
