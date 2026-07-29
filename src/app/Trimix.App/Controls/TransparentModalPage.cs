using Microsoft.Maui.Controls.PlatformConfiguration;
using Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific;

namespace Trimix.App.Controls;

/// <summary>
/// A see-through modal page. Pushed over the whole window (i.e. ABOVE the Shell tab bar), so a hosted
/// <see cref="BottomSheet"/> overlays the tab bar with the menu dimmed <em>underneath</em> it — never
/// below it (Android) or punched through by it (the iOS floating island) as a page-hosted sheet is —
/// and reaches the true device bottom edge on both platforms.
///
/// <para>Transparency plumbing: the background is cleared, on iOS the modal is presented
/// <c>OverFullScreen</c> (a plain modal is opaque and would hide everything beneath), and safe-area
/// insetting is turned off so the sheet can bleed to the physical bottom — hosted content owns its
/// own bottom padding. On Android the window insets are CONSUMED so MAUI does not shrink the modal to
/// sit inside the nav bar (which would leave the sheet floating above it with a scrim strip below);
/// the sheet then reaches the true bottom and its own bottom padding clears the gesture bar. Reusable
/// app-wide: host any <see cref="BottomSheet"/> inside one of these.</para>
/// </summary>
public class TransparentModalPage : ContentPage
{
    public TransparentModalPage()
    {
        BackgroundColor = Colors.Transparent;
        Shell.SetNavBarIsVisible(this, false);
        On<iOS>().SetModalPresentationStyle(UIModalPresentationStyle.OverFullScreen);
        On<iOS>().SetUseSafeArea(false);
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
#if ANDROID
        if (Handler?.PlatformView is Android.Views.View view)
        {
            // Consume the system-bar insets so MAUI does not inset (shrink) this modal to fit inside
            // the nav bar — the sheet needs the full window height to reach the true bottom edge.
            AndroidX.Core.View.ViewCompat.SetOnApplyWindowInsetsListener(view, new ConsumeInsetsListener());
        }
#endif
    }

#if ANDROID
    private sealed class ConsumeInsetsListener : Java.Lang.Object, AndroidX.Core.View.IOnApplyWindowInsetsListener
    {
        public AndroidX.Core.View.WindowInsetsCompat OnApplyWindowInsets(
            Android.Views.View view, AndroidX.Core.View.WindowInsetsCompat insets)
            => AndroidX.Core.View.WindowInsetsCompat.Consumed;
    }
#endif
}
