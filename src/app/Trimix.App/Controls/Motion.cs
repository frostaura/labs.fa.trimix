namespace Trimix.App.Controls;

/// <summary>
/// The shared motion layer, matching the design's keyframes exactly and honoring
/// <see cref="MotionSettings.IsReducedMotionEnabled"/> (reduced motion ⇒ jump to the final state).
/// <list type="bullet">
/// <item><see cref="ScreenInAsync"/> — the design's <c>screenIn .35s ease</c> (fade + translateY 10→0),
/// run by <see cref="TrimixPage"/> on every appearance.</item>
/// <item><see cref="RowStaggerAsync"/> — the design's <c>rowIn .4s ease</c> list-row entrance
/// (fade + translateX −8→0), staggered per row.</item>
/// </list>
/// </summary>
public static class Motion
{
    /// <summary>screenIn: .35s ease, opacity 0→1 + translateY 10→0. Safe to fire-and-forget.</summary>
    public static async Task ScreenInAsync(VisualElement target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (MotionSettings.IsReducedMotionEnabled)
        {
            target.Opacity = 1;
            target.TranslationY = 0;
            return;
        }

        target.Opacity = 0;
        target.TranslationY = 10;
        await Task.WhenAll(
            target.FadeTo(1, 350, Easing.CubicOut),
            target.TranslateTo(0, 0, 350, Easing.CubicOut));
    }

    /// <summary>rowIn: each row .4s ease, opacity 0→1 + translateX −8→0, staggered by
    /// <paramref name="staggerMs"/>. Prepares all rows invisible first so late rows don't flash.</summary>
    public static async Task RowStaggerAsync(IReadOnlyList<VisualElement> rows, int staggerMs = 60)
    {
        ArgumentNullException.ThrowIfNull(rows);

        if (MotionSettings.IsReducedMotionEnabled)
        {
            foreach (var row in rows)
            {
                row.Opacity = 1;
                row.TranslationX = 0;
            }

            return;
        }

        foreach (var row in rows)
        {
            row.Opacity = 0;
            row.TranslationX = -8;
        }

        var animations = new List<Task>(rows.Count);
        for (var i = 0; i < rows.Count; i++)
        {
            animations.Add(RunRowAsync(rows[i], i * staggerMs));
        }

        await Task.WhenAll(animations);
    }

    private static async Task RunRowAsync(VisualElement row, int delayMs)
    {
        if (delayMs > 0)
        {
            await Task.Delay(delayMs);
        }

        await Task.WhenAll(
            row.FadeTo(1, 400, Easing.CubicOut),
            row.TranslateTo(0, row.TranslationY, 400, Easing.CubicOut));
    }
}

/// <summary>
/// Base page for every Trimix screen: runs the design's <c>screenIn</c> entrance on the page content
/// each time the page appears (the design animates EVERY screen enter). Pages opt out by overriding
/// <see cref="EnableScreenInMotion"/>. Phase B migrates all pages onto this base; SettingsPage is
/// the reference adoption.
/// </summary>
public class TrimixPage : ContentPage
{
    /// <summary>Override false for pages that own a custom entrance.</summary>
    protected virtual bool EnableScreenInMotion => true;

    private bool _suppressNextScreenIn;

    /// <summary>
    /// Skip the screenIn on the NEXT appearance only. Call this before pushing a transparent modal
    /// overlay over this page: the page never actually left the screen, so replaying the entrance when
    /// the overlay pops reads as the whole screen (header included) re-rendering. The page is already
    /// at its resting state, so skipping simply leaves it put.
    /// </summary>
    protected void SuppressNextScreenIn() => _suppressNextScreenIn = true;

    protected override void OnAppearing()
    {
        base.OnAppearing();

        if (_suppressNextScreenIn)
        {
            _suppressNextScreenIn = false;
            return;
        }

        if (EnableScreenInMotion && Content is not null)
        {
            _ = Motion.ScreenInAsync(Content);
        }
    }
}
