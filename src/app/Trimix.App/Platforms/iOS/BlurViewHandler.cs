using Microsoft.Maui.Handlers;
using Trimix.App.Controls;
using UIKit;

namespace Trimix.App.Platforms.iOS;

/// <summary>
/// iOS handler for <see cref="BlurView"/>: a live <c>UIVisualEffectView</c> that blurs whatever sits
/// behind the transparent sheet host modal (the logbook + tab bar) into frosted glass. The blur is
/// driven at PARTIAL intensity via a paused <see cref="UIViewPropertyAnimator"/> — a full-strength
/// material reads as a near-solid slab, whereas a half-scrubbed ultra-thin material keeps the content
/// behind clearly visible, just softened. Input-transparent so the scrim above still takes the tap.
/// </summary>
public sealed class BlurViewHandler : ViewHandler<BlurView, UIVisualEffectView>
{
    public static readonly IPropertyMapper<BlurView, BlurViewHandler> BlurMapper =
        new PropertyMapper<BlurView, BlurViewHandler>(ViewMapper);

    // How far to scrub the blur (0 = no blur / fully clear, 1 = full ultra-thin material). ~0.5 keeps
    // the content behind readable through the frost.
    private const float BlurIntensity = 0.5f;

    private UIViewPropertyAnimator? _animator;

    public BlurViewHandler()
        : base(BlurMapper)
    {
    }

    protected override UIVisualEffectView CreatePlatformView() => new()
    {
        UserInteractionEnabled = false,
    };

    protected override void ConnectHandler(UIVisualEffectView platformView)
    {
        base.ConnectHandler(platformView);
        ApplyPartialBlur(platformView);
    }

    protected override void DisconnectHandler(UIVisualEffectView platformView)
    {
        _animator?.StopAnimation(true);
        _animator = null;
        base.DisconnectHandler(platformView);
    }

    private void ApplyPartialBlur(UIVisualEffectView view)
    {
        _animator?.StopAnimation(true);
        view.Effect = null;

        // Scrub a "no effect → dark blur" animation to a fraction; the paused animator holds the view
        // at that intermediate strength. The legacy Dark style tints BLACK (the newer *MaterialDark
        // styles read grey); a black-tint scrim over it deepens that further while the partial blur
        // keeps the content behind softly visible.
        _animator = new UIViewPropertyAnimator(1.0, UIViewAnimationCurve.Linear, () =>
            view.Effect = UIBlurEffect.FromStyle(UIBlurEffectStyle.Dark))
        {
            PausesOnCompletion = true,
        };
        _animator.FractionComplete = BlurIntensity;
    }
}
