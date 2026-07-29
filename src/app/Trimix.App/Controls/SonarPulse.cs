using Microsoft.Maui.Graphics;

namespace Trimix.App.Controls;

/// <summary>
/// The design's animated sonar rings (<c>blePulse</c>): two staggered concentric rings expanding
/// from scale .4 to 1.15 while fading (opacity 0 → 1 at 15% → 0), second ring offset half a period.
/// Place it BEHIND the button it haloes, centered in the same Grid cell:
/// <code>
/// &lt;Grid&gt;
///   &lt;controls:SonarPulse IsActive="{Binding Sync.HasLinkedSources}" RingDiameter="62" /&gt;
///   &lt;Button … /&gt;
/// &lt;/Grid&gt;
/// </code>
/// Design timings: sync button rings 2.8s linear; onboarding hero rings 3.2s (set
/// <see cref="PulseDuration"/>). Pulses only while <see cref="IsActive"/> (design gates on
/// <c>anyLinked</c>) — inactive renders NOTHING; honors
/// <see cref="MotionSettings.IsReducedMotionEnabled"/> by showing a static faint ring instead of
/// animating.
///
/// <para>Implementation note: rings are drawn on a <see cref="GraphicsView"/> canvas — perfectly
/// concentric about the control's center, contained in a fixed-size box (max pulse extent), never
/// spilling across the page. View-level <c>Scale</c> on MAUI Shape Ellipses proved unreliable on
/// Android (off-center anchors, runaway sizes), so no view transforms are used at all.</para>
/// </summary>
public sealed class SonarPulse : GraphicsView
{
    private const string AnimationHandle = "trimix.sonarPulse";
    private const double ScaleFrom = 0.2;
    private const double ScaleTo = 1.15;
    private const double OpacityPeakAt = 0.15;
    private const double StrokeAllowance = 2;

    public static readonly BindableProperty IsActiveProperty = BindableProperty.Create(
        nameof(IsActive), typeof(bool), typeof(SonarPulse), false,
        propertyChanged: (b, _, _) => ((SonarPulse)b).UpdateState());

    public static readonly BindableProperty RingDiameterProperty = BindableProperty.Create(
        nameof(RingDiameter), typeof(double), typeof(SonarPulse), 62.0,
        propertyChanged: (b, _, _) => ((SonarPulse)b).ApplySize());

    public static readonly BindableProperty PulseDurationProperty = BindableProperty.Create(
        nameof(PulseDuration), typeof(double), typeof(SonarPulse), 2800.0,
        propertyChanged: (b, _, _) => ((SonarPulse)b).UpdateState());

    public static readonly BindableProperty RingColorProperty = BindableProperty.Create(
        nameof(RingColor), typeof(Color), typeof(SonarPulse), null,
        propertyChanged: (b, _, _) => ((SonarPulse)b).Invalidate());

    /// <summary>Current animation phase t ∈ [0,1) driving ring 1 (ring 2 runs t + ½ period).</summary>
    private double _phase;

    public SonarPulse()
    {
        Drawable = new RingsDrawable(this);
        BackgroundColor = Colors.Transparent;
        InputTransparent = true;
        HorizontalOptions = LayoutOptions.Center;
        VerticalOptions = LayoutOptions.Center;
        ApplySize();

        Loaded += (_, _) => UpdateState();
        Unloaded += (_, _) => this.AbortAnimation(AnimationHandle);
    }

    /// <summary>Pulse only while true (design: gated on any linked provider). False hides the rings.</summary>
    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    /// <summary>Ring diameter at scale 1 (design: 62 around the 34px sync button, 164 on the hero).</summary>
    public double RingDiameter
    {
        get => (double)GetValue(RingDiameterProperty);
        set => SetValue(RingDiameterProperty, value);
    }

    /// <summary>One pulse period in milliseconds (design: 2800 sync button / 3200 hero).</summary>
    public double PulseDuration
    {
        get => (double)GetValue(PulseDurationProperty);
        set => SetValue(PulseDurationProperty, value);
    }

    /// <summary>Ring stroke color; null uses the design's mint at 50% opacity.</summary>
    public Color? RingColor
    {
        get => (Color?)GetValue(RingColorProperty);
        set => SetValue(RingColorProperty, value);
    }

    /// <summary>Fixed intrinsic size = the maximum pulse extent, so the halo never spills.</summary>
    private void ApplySize()
    {
        var extent = (RingDiameter * ScaleTo) + StrokeAllowance;
        WidthRequest = extent;
        HeightRequest = extent;
        Invalidate();
    }

    private void UpdateState()
    {
        this.AbortAnimation(AnimationHandle);

        if (!IsActive || MotionSettings.IsReducedMotionEnabled)
        {
            // Inactive: draw nothing. Reduced motion + active: one static faint full-size ring.
            Invalidate();
            return;
        }

        var animation = new Animation(t =>
        {
            _phase = t;
            Invalidate();
        });
        animation.Commit(
            this, AnimationHandle, rate: 16, length: (uint)PulseDuration, Easing.Linear,
            repeat: () => IsActive && Handler is not null);
    }

    private sealed class RingsDrawable(SonarPulse owner) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            if (!owner.IsActive)
            {
                return;
            }

            var baseColor = owner.RingColor ?? ThemeTokens.Get("Sonar").WithAlpha(0.5f);
            var cx = dirtyRect.Center.X;
            var cy = dirtyRect.Center.Y;

            canvas.SaveState();
            canvas.Antialias = true;
            canvas.StrokeSize = 1;

            if (MotionSettings.IsReducedMotionEnabled)
            {
                // Motionless equivalent: one faint full-size ring still signals the active state.
                canvas.StrokeColor = baseColor.MultiplyAlpha(0.35f);
                canvas.DrawCircle(cx, cy, (float)(owner.RingDiameter / 2));
                canvas.RestoreState();
                return;
            }

            // Two rings on one driver; ring 2 phase-shifted half a period (design: same animation,
            // delay = duration / 2). Both expand from the SAME center — concentric by construction.
            DrawRing(canvas, baseColor, cx, cy, owner._phase, owner.RingDiameter);
            DrawRing(canvas, baseColor, cx, cy, (owner._phase + 0.5) % 1.0, owner.RingDiameter);
            canvas.RestoreState();
        }

        private static void DrawRing(ICanvas canvas, Color baseColor, float cx, float cy, double t, double diameter)
        {
            var scale = ScaleFrom + ((ScaleTo - ScaleFrom) * t);
            var opacity = t < OpacityPeakAt ? t / OpacityPeakAt : 1 - ((t - OpacityPeakAt) / (1 - OpacityPeakAt));
            canvas.StrokeColor = baseColor.MultiplyAlpha((float)opacity);
            canvas.DrawCircle(cx, cy, (float)(diameter * scale / 2));
        }
    }
}
