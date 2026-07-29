using Trimix.App.Controls;
using Trimix.Core.ViewModels;

namespace Trimix.App.Pages;

/// <summary>
/// Onboarding (S1 + S2). Code-behind owns one view concern: the SYNCS WITH marquee — the design's
/// <c>brandSlide 22s linear infinite</c>.
///
/// <para><b>Why the loop is seamless.</b> The strip renders the brand row TWICE with EVERY chip carrying
/// a uniform trailing gap (a right margin) and every StackLayout <c>Spacing=0</c>, so the doubled row is a
/// perfectly periodic strip. We translate left by EXACTLY one copy's width; at that offset copy B sits
/// pixel-on where copy A began, so resetting the offset to 0 is invisible.</para>
///
/// <para><b>The width trap.</b> The track overflows its parent, and MAUI reports the CLAMPED arranged
/// <c>Width</c> (≈ the screen width), not the real content width — using it made the marquee scroll half a
/// screen and snap (the visible "flip"). So we take the UNCONSTRAINED desired size via
/// <c>Measure(∞, ∞)</c> and shift by half of that. We also arm only once the two copies have actually laid
/// out (their BindableLayout populates a beat after Loaded), retrying briefly until the measure is real.
/// Honors reduced motion (static row).</para>
/// </summary>
public partial class OnboardingPage : TrimixPage
{
    private const string MarqueeAnimationHandle = "trimix.brandSlide";
    private const uint MarqueePeriodMs = 22000;

    private HorizontalStackLayout? _marqueeTrack;

    public OnboardingPage(OnboardingViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    private void OnMarqueeLoaded(object? sender, EventArgs e)
    {
        if (sender is not HorizontalStackLayout track)
        {
            return;
        }

        _marqueeTrack = track;
        ArmWhenReady(0);
    }

    private void OnMarqueeUnloaded(object? sender, EventArgs e)
    {
        if (sender is HorizontalStackLayout track)
        {
            track.AbortAnimation(MarqueeAnimationHandle);
        }

        _marqueeTrack = null;
    }

    /// <summary>
    /// Arm the marquee once the doubled row has a real (unconstrained) width. The inner copies populate a
    /// frame or two after Loaded, so poll briefly (≤ ~4s) until the measure is sane, then commit the loop.
    /// </summary>
    private void ArmWhenReady(int attempt)
    {
        var track = _marqueeTrack;
        if (track is null || track.Handler is null || MotionSettings.IsReducedMotionEnabled)
        {
            return;
        }

        // Unconstrained content width = both copies laid out edge to edge (NOT the clamped arranged Width).
        var contentWidth = ((IView)track).Measure(double.PositiveInfinity, double.PositiveInfinity).Width;

        // One copy must be wider than the viewport for the two-copy trick to work; require the doubled
        // width to clearly exceed the screen before trusting it.
        if (contentWidth > track.Width + 40)
        {
            StartMarquee(track, contentWidth / 2);
            return;
        }

        if (attempt < 40)
        {
            Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(100), () => ArmWhenReady(attempt + 1));
        }
    }

    private static void StartMarquee(HorizontalStackLayout track, double shift)
    {
        if (shift <= 0)
        {
            return;
        }

        track.AbortAnimation(MarqueeAnimationHandle);

        // Constant-velocity infinite loop: position is a pure function of wall-clock time (elapsed %
        // period), so nothing accumulates. At the seam the offset returns from -shift to 0 — invisible
        // because copy B is pixel-identical to copy A at exactly -shift (one full, uniformly-gapped copy).
        var clock = System.Diagnostics.Stopwatch.StartNew();
        new Animation(_ =>
        {
            var phase = clock.Elapsed.TotalMilliseconds % MarqueePeriodMs / MarqueePeriodMs;
            track.TranslationX = -shift * phase;
        })
            .Commit(
                track, MarqueeAnimationHandle, rate: 16, length: MarqueePeriodMs, Easing.Linear,
                finished: null,
                repeat: () => track.Handler is not null && !MotionSettings.IsReducedMotionEnabled);
    }
}
