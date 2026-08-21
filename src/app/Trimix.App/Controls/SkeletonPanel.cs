namespace Trimix.App.Controls;

/// <summary>
/// A loading placeholder that pulses. Put plain <c>Border</c>/<c>BoxView</c> blocks inside it laid out roughly
/// like the real content; this panel animates its own opacity so the whole group shimmers as one, which is far
/// cheaper than a per-block gradient sweep and reads the same at a glance.
///
/// <para>It exists because both the logbook and the dive detail page used to render their EMPTY state first and
/// swap in real data a moment later — "No dives yet" flashing before four dives appear reads as a bug. A
/// skeleton communicates "loading" honestly instead of asserting "empty".</para>
///
/// <para>The pulse starts and stops with visibility, so a hidden skeleton costs nothing, and reduced motion
/// pins it to a static mid-opacity rather than animating.</para>
/// </summary>
public sealed class SkeletonPanel : ContentView
{
    private const string PulseHandle = "trimix.skeletonPulse";
    private const double PulseLow = 0.35;
    private const double PulseHigh = 0.75;
    private const uint PulseDurationMs = 900;

    public SkeletonPanel()
    {
        InputTransparent = true;
        Opacity = PulseLow;

        // A skeleton is decorative: screen readers should hear the page's own busy/loaded state, not a tree of
        // meaningless placeholder blocks.
        AutomationProperties.SetIsInAccessibleTree(this, false);

        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IsVisible))
            {
                UpdatePulse();
            }
        };
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        UpdatePulse();
    }

    private void UpdatePulse()
    {
        this.AbortAnimation(PulseHandle);

        if (!IsVisible)
        {
            return;
        }

        if (MotionSettings.IsReducedMotionEnabled)
        {
            Opacity = (PulseLow + PulseHigh) / 2;
            return;
        }

        var pulse = new Animation(v => Opacity = v, PulseLow, PulseHigh, Easing.SinInOut);
        pulse.Commit(
            this, PulseHandle, rate: 16, length: PulseDurationMs, Easing.Linear,
            // Ping-pong forever while visible; the repeat predicate is what stops it once hidden.
            finished: (_, _) => Opacity = PulseLow,
            repeat: () => IsVisible && !MotionSettings.IsReducedMotionEnabled);
    }
}
