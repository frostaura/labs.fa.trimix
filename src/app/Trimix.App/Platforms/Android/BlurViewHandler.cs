using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Handlers;
using Trimix.App.Controls;
using AView = Android.Views.View;

namespace Trimix.App.Platforms.Android;

/// <summary>
/// Android handler for <see cref="BlurView"/>. Android has no cheap LIVE backdrop blur, but the
/// content behind a modal is frozen while the sheet is open, so a one-shot snapshot blur reads
/// identically to iOS's live blur: at open we grab the window's current frame (the logbook + tab bar)
/// as the view's background, then apply a hardware <c>RenderEffect</c> gaussian blur (API 31+) for a
/// smooth frost — falling back to a downscale/upscale approximation on older devices. The sheet's
/// black scrim then tints it, so the Android backdrop matches the iOS black frosted glass.
/// </summary>
public sealed class BlurViewHandler : ViewHandler<BlurView, AView>
{
    public static readonly IPropertyMapper<BlurView, BlurViewHandler> BlurMapper =
        new PropertyMapper<BlurView, BlurViewHandler>(ViewMapper);

    private const float BlurRadius = 32f;   // hardware gaussian radius (API 31+)
    private const int FallbackDownscale = 16; // downscale divisor for the pre-31 approximation

    public BlurViewHandler()
        : base(BlurMapper)
    {
    }

    protected override AView CreatePlatformView() => new(Context)
    {
        Clickable = false,
        Focusable = false,
    };

    protected override void ConnectHandler(AView platformView)
    {
        base.ConnectHandler(platformView);
        // Defer one frame so the window has a current frame to snapshot.
        platformView.Post(() => ApplyBackdropBlur(platformView));
    }

    private static void ApplyBackdropBlur(AView view)
    {
        var decor = Platform.CurrentActivity?.Window?.DecorView;
        if (decor is null || decor.Width == 0 || decor.Height == 0)
        {
            return;
        }

        try
        {
            var snapshot = Bitmap.CreateBitmap(decor.Width, decor.Height, Bitmap.Config.Argb8888!);
            using (var canvas = new Canvas(snapshot))
            {
                decor.Draw(canvas);
            }

            if (Build.VERSION.SdkInt >= BuildVersionCodes.S)
            {
                // Real hardware gaussian blur of the snapshot — smooth, like the iOS material.
                view.Background = new BitmapDrawable(view.Resources, snapshot);
                view.SetRenderEffect(RenderEffect.CreateBlurEffect(BlurRadius, BlurRadius, Shader.TileMode.Clamp!));
                return;
            }

            // Pre-31 fallback: shrink hard then bilinear-upscale back (a coarser, blockier frost).
            var w = Math.Max(1, decor.Width / FallbackDownscale);
            var h = Math.Max(1, decor.Height / FallbackDownscale);
            using var small = Bitmap.CreateScaledBitmap(snapshot, w, h, true);
            var blurred = Bitmap.CreateScaledBitmap(small, decor.Width, decor.Height, true);
            snapshot.Recycle();
            view.Background = new BitmapDrawable(view.Resources, blurred);
        }
        catch (Exception)
        {
            // A snapshot failure just means no blur this open — the scrim still frosts it.
        }
    }
}
