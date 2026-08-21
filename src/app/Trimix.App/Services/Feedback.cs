using Trimix.Core.Abstractions;

namespace Trimix.App.Services;

/// <summary>
/// The app's single haptics implementation, exposed two ways: as <see cref="IHaptics"/> for viewmodels
/// (see <see cref="MauiHaptics"/>) and as static methods for XAML code-behind, which has no constructor
/// to inject into. Both funnel through <see cref="Perform"/>, so there is exactly one place that decides
/// how a cue feels.
///
/// <para><b>Deliberately not MAUI Essentials.</b> <c>HapticFeedback.Perform</c> exposes only Click and
/// LongPress and, on Android, drives the vibrator directly — which needs the <c>VIBRATE</c> permission
/// and ignores the user's "touch feedback" system setting. Going native instead gets the real Taptic
/// Engine vocabulary on iOS (impact / selection / notification) and Android's
/// <c>performHapticFeedback</c>, which needs NO permission and respects the system setting for free.
/// That is why the manifest gained nothing for this feature.</para>
///
/// <para>Every path is silent-on-failure. A device with no haptic hardware, a user who turned touch
/// feedback off, and the unit-test host all take the same no-op route, so call sites never guard.</para>
/// </summary>
public static class Feedback
{
#if IOS
    // Generators are cached AND primed. Caching alone is not enough: on iOS it is Prepare() that spins the
    // Taptic Engine up, and an un-primed generator delivers its first impact noticeably late — so the first
    // tap of a session feels broken while every later one feels right. Warm() is called on page appearance
    // and Prepare() is re-armed after every cue, which is Apple's documented pattern.
    private static UIKit.UISelectionFeedbackGenerator? _selection;
    private static UIKit.UINotificationFeedbackGenerator? _notification;
    private static UIKit.UIImpactFeedbackGenerator? _light;
    private static UIKit.UIImpactFeedbackGenerator? _medium;
#endif

    /// <summary>
    /// Spins the haptic hardware up ahead of use, so the NEXT cue is immediate. Called when a page appears;
    /// safe to call as often as you like. No-op on Android, whose <c>performHapticFeedback</c> has no
    /// equivalent warm-up and no cold-start penalty.
    /// </summary>
    public static void Warm()
    {
#if IOS
        if (!MainThread.IsMainThread)
        {
            MainThread.BeginInvokeOnMainThread(Warm);
            return;
        }

        try
        {
            (_selection ??= new UIKit.UISelectionFeedbackGenerator()).Prepare();
            (_notification ??= new UIKit.UINotificationFeedbackGenerator()).Prepare();
            (_light ??= new UIKit.UIImpactFeedbackGenerator(UIKit.UIImpactFeedbackStyle.Light)).Prepare();
            (_medium ??= new UIKit.UIImpactFeedbackGenerator(UIKit.UIImpactFeedbackStyle.Medium)).Prepare();
        }
        catch
        {
            // No haptic hardware, or the engine refused to arm. Nothing to recover.
        }
#endif
    }

    /// <summary>A button, card or row was pressed. Fires on press, not on the resulting work.</summary>
    public static void Tap() => Perform(HapticCue.Tap);

    /// <summary>Moved between discrete choices — section switcher, chart scrub.</summary>
    public static void Selection() => Perform(HapticCue.Selection);

    /// <summary>A page or tab transition was requested.</summary>
    public static void Navigate() => Perform(HapticCue.Navigate);

    /// <summary>An operation completed.</summary>
    public static void Success() => Perform(HapticCue.Success);

    /// <summary>A recoverable refusal.</summary>
    public static void Warning() => Perform(HapticCue.Warning);

    /// <summary>An operation failed.</summary>
    public static void Error() => Perform(HapticCue.Error);

    /// <summary>
    /// Minimum gap between two consecutive light cues. Several hooks legitimately overlap on one gesture —
    /// a button press fires <see cref="HapticCue.Tap"/>, its command navigates, and Shell's Navigating
    /// hook fires <see cref="HapticCue.Navigate"/> a few milliseconds later. Two buzzes for one finger
    /// reads as a stutter, so the second is dropped. The terminal cues (Success / Warning / Error) are
    /// never coalesced: they report an outcome, and losing one loses information.
    /// </summary>
    private const long CoalesceWindowMs = 120;

    private static long _lastFiredTicks;

    /// <summary>Views already given a touch-down warm-up recognizer, so one is never added twice.</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<View, View> _warmOnPress = new();

    /// <summary>
    /// Fire a cue. Marshals to the main thread (UIKit feedback generators and Android's
    /// <c>performHapticFeedback</c> are both main-thread-only) and swallows everything, because a
    /// missing buzz must never surface as an exception in a user's face.
    /// </summary>
    public static void Perform(HapticCue cue)
    {
        var isTerminal = cue is HapticCue.Success or HapticCue.Warning or HapticCue.Error;
        // TickCount64 rather than DateTime: monotonic, allocation-free, and immune to clock changes.
        var now = Environment.TickCount64;

        if (!isTerminal && now - _lastFiredTicks < CoalesceWindowMs)
        {
            return;
        }

        _lastFiredTicks = now;

        if (MainThread.IsMainThread)
        {
            PerformCore(cue);
            return;
        }

        MainThread.BeginInvokeOnMainThread(() => PerformCore(cue));
    }

    // ---- Automatic feedback for every TapGestureRecognizer ------------------------------------------
    // Almost every tap target in this app is a Border/Grid carrying a TapGestureRecognizer with a bound
    // Command — dive rows, paywall tiles, the + FAB, sheet rows, the entry-point card. Wiring each one by
    // hand would mean a dozen XAML edits plus a dozen code-behind methods, and the thirteenth would be
    // forgotten. Instead a page calls AttachTapFeedback(this) once and every tap target it will ever
    // contain, including ones materialized later from a DataTemplate, ticks on touch.
    //
    // Buttons are NOT handled here — they get their own Pressed hook in MauiProgram, which fires earlier
    // in the gesture than Tapped does.

    /// <summary>
    /// Overrides the cue a tap target fires. Defaults to <see cref="HapticCue.Tap"/>; set it in XAML
    /// (<c>services:Feedback.Cue="Selection"</c>) on anything that is really a choice rather than a press,
    /// such as the dive-detail section switcher.
    /// </summary>
    public static readonly BindableProperty CueProperty = BindableProperty.CreateAttached(
        "Cue", typeof(HapticCue), typeof(Feedback), HapticCue.Tap);

    public static HapticCue GetCue(BindableObject target) => (HapticCue)target.GetValue(CueProperty);

    public static void SetCue(BindableObject target, HapticCue value) => target.SetValue(CueProperty, value);

    /// <summary>
    /// Gives every <see cref="TapGestureRecognizer"/> in <paramref name="root"/>'s subtree — present and
    /// future — a haptic tick. Call once from a page constructor. Idempotent.
    /// </summary>
    public static void AttachTapFeedback(Element root)
    {
        HookSubtree(root);

        // Covers template-materialized children (dive rows, paywall tiles, pane chips) as they appear.
        root.DescendantAdded -= OnDescendantAdded;
        root.DescendantAdded += OnDescendantAdded;
    }

    private static void OnDescendantAdded(object? sender, ElementEventArgs e)
    {
        // Hook THIS element only — never its subtree. DescendantAdded fires once for every descendant at
        // every depth, so per-element hooking is already complete, whereas walking the subtree here re-walks
        // the same elements once per ancestor. That is quadratic in page size, it runs on the UI thread, and
        // it is re-triggered by every relayout that re-parents anything — including the one the soft keyboard
        // causes when a field is tapped, which is how it showed up as the app freezing on an input tap.
        HookElement(e.Element);

        // Belt and braces: XAML normally sets GestureRecognizers before the element is parented, but a
        // deferred second pass is O(1) here and removes the ordering assumption.
        if (sender is Element { Dispatcher: { } dispatcher })
        {
            dispatcher.Dispatch(() => HookElement(e.Element));
        }
    }

    /// <summary>Hooks one element's own gesture recognizers. O(1) — no recursion.</summary>
    private static void HookElement(Element element)
    {
        if (element is not View view)
        {
            return;
        }

        // Arm the haptic hardware on touch-DOWN. This is the fix for "the tick arrives after the page has
        // loaded" on iOS: a UIFeedbackGenerator that has not been Prepare()d recently is slow to fire, and
        // the Taptic Engine idles back down within a couple of seconds — so reading the logbook for a moment
        // and then tapping means every tap is the cold one. Warm() emits NO cue, so touch-down stays silent
        // and a press-then-scroll still produces nothing; only the timing of the release cue improves.
        // Android needs none of this (performHapticFeedback is immediate) but the call is a cheap no-op there.
        if (view.GestureRecognizers.OfType<TapGestureRecognizer>().Any()
            && !_warmOnPress.TryGetValue(view, out _))
        {
            _warmOnPress.Add(view, view);
            var pointer = new PointerGestureRecognizer();
            pointer.PointerPressed += OnPointerPressedWarm;
            view.GestureRecognizers.Add(pointer);
        }

        foreach (var recognizer in view.GestureRecognizers)
        {
            switch (recognizer)
            {
                case TapGestureRecognizer tap:
                    // A recognizer's Command usually arrives from a binding that resolves AFTER the
                    // template is materialized (the dive-row and paywall-tile recognizers both bind theirs
                    // via RelativeSource), so a one-shot wrap here would miss them. Watching Command is
                    // what makes the wrap stick.
                    tap.PropertyChanged -= OnRecognizerPropertyChanged;
                    tap.PropertyChanged += OnRecognizerPropertyChanged;
                    ApplyTapFeedback(tap);
                    break;

                case SwipeGestureRecognizer swipe:
                    swipe.PropertyChanged -= OnRecognizerPropertyChanged;
                    swipe.PropertyChanged += OnRecognizerPropertyChanged;
                    ApplySwipeFeedback(swipe);
                    break;
            }
        }
    }

    private static void OnRecognizerPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (sender)
        {
            case TapGestureRecognizer tap when e.PropertyName == TapGestureRecognizer.CommandProperty.PropertyName:
                ApplyTapFeedback(tap);
                break;

            case SwipeGestureRecognizer swipe when e.PropertyName == SwipeGestureRecognizer.CommandProperty.PropertyName:
                ApplySwipeFeedback(swipe);
                break;
        }
    }

    /// <summary>
    /// Routes a tap recognizer's cue through whichever mechanism can fire it FIRST. With a Command that
    /// means wrapping it (see <see cref="HapticCommand"/>); without one, the Tapped event is all there is.
    ///
    /// <para>Only ever one of the two is active. Doing both would fire the cue twice, and because a
    /// navigating command can easily take longer than the coalesce window, the second tick would land as a
    /// real audible double-buzz rather than being swallowed.</para>
    ///
    /// <para>Note for anyone adding a recognizer later: a recognizer carrying BOTH a Command and a
    /// code-behind Tapped handler will tick once, from the command. None exists today.</para>
    /// </summary>
    private static void ApplyTapFeedback(TapGestureRecognizer tap)
    {
        if (tap.Command is { } command)
        {
            if (command is not HapticCommand)
            {
                // Re-entrant: assigning Command raises PropertyChanged, which lands back here and sees a
                // HapticCommand, so this converges after exactly one extra pass.
                tap.Command = new HapticCommand(command, GetCue(tap));
            }

            tap.Tapped -= OnTapped;
            return;
        }

        // Delegate.Remove matches on target+method, so re-hooking never stacks subscriptions.
        tap.Tapped -= OnTapped;
        tap.Tapped += OnTapped;
    }

    /// <summary>
    /// Same contract for swipes, which page the dive-detail sections. Defaults to
    /// <see cref="HapticCue.Selection"/> rather than Tap — a swipe between sections is the same action as
    /// tapping the section switcher, which already uses Selection, so they must feel identical.
    /// </summary>
    private static void ApplySwipeFeedback(SwipeGestureRecognizer swipe)
    {
        var cue = swipe.IsSet(CueProperty) ? GetCue(swipe) : HapticCue.Selection;

        if (swipe.Command is { } command)
        {
            if (command is not HapticCommand)
            {
                swipe.Command = new HapticCommand(command, cue);
            }

            swipe.Swiped -= OnSwiped;
            return;
        }

        swipe.Swiped -= OnSwiped;
        swipe.Swiped += OnSwiped;
    }

    /// <summary>Touch-down: prime the hardware only. Deliberately emits no cue.</summary>
    private static void OnPointerPressedWarm(object? sender, PointerEventArgs e) => Warm();

    private static void OnSwiped(object? sender, SwipedEventArgs e) =>
        Perform(sender is BindableObject target && target.IsSet(CueProperty)
            ? GetCue(target)
            : HapticCue.Selection);

    /// <summary>
    /// Recursive walk, used ONLY for the initial attach to cover elements that already exist by the time the
    /// page constructor runs. Everything added later arrives via <see cref="OnDescendantAdded"/> instead.
    /// </summary>
    private static void HookSubtree(Element element)
    {
        HookElement(element);

        if (element is IVisualTreeElement tree)
        {
            foreach (var child in tree.GetVisualChildren())
            {
                if (child is Element childElement)
                {
                    HookSubtree(childElement);
                }
            }
        }
    }

    private static void OnTapped(object? sender, TappedEventArgs e) =>
        Perform(sender is BindableObject target ? GetCue(target) : HapticCue.Tap);

    private static void PerformCore(HapticCue cue)
    {
        try
        {
#if ANDROID
            // The decor view is used purely as a handle onto the window's haptic channel — any attached
            // view would do, and using the window's avoids threading a View through every call site.
            if (Platform.CurrentActivity?.Window?.DecorView is not { } view)
            {
                return;
            }

            var isApi30 = Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.R;
            var constant = cue switch
            {
                // Confirm/Reject are the semantic API-30 constants; below that fall back to the
                // closest pre-30 feel rather than skipping the feedback entirely.
                HapticCue.Success => isApi30
                    ? Android.Views.FeedbackConstants.Confirm
                    : Android.Views.FeedbackConstants.VirtualKey,
                HapticCue.Error => isApi30
                    ? Android.Views.FeedbackConstants.Reject
                    : Android.Views.FeedbackConstants.LongPress,
                HapticCue.Warning => Android.Views.FeedbackConstants.LongPress,
                HapticCue.Navigate => Android.Views.FeedbackConstants.ContextClick,
                HapticCue.Selection => Android.Views.FeedbackConstants.ClockTick,
                _ => Android.Views.FeedbackConstants.VirtualKey,
            };

            view.PerformHapticFeedback(constant);
#elif IOS
            switch (cue)
            {
                case HapticCue.Selection:
                    var selection = _selection ??= new UIKit.UISelectionFeedbackGenerator();
                    selection.SelectionChanged();
                    // Re-arm for the next cue — a scrub or a section page usually comes in a burst.
                    selection.Prepare();
                    break;

                case HapticCue.Success:
                case HapticCue.Warning:
                case HapticCue.Error:
                    var type = cue switch
                    {
                        HapticCue.Success => UIKit.UINotificationFeedbackType.Success,
                        HapticCue.Warning => UIKit.UINotificationFeedbackType.Warning,
                        _ => UIKit.UINotificationFeedbackType.Error,
                    };
                    var notification = _notification ??= new UIKit.UINotificationFeedbackGenerator();
                    notification.NotificationOccurred(type);
                    notification.Prepare();
                    break;

                case HapticCue.Navigate:
                    var medium = _medium ??= new UIKit.UIImpactFeedbackGenerator(UIKit.UIImpactFeedbackStyle.Medium);
                    medium.ImpactOccurred();
                    medium.Prepare();
                    break;

                default:
                    var light = _light ??= new UIKit.UIImpactFeedbackGenerator(UIKit.UIImpactFeedbackStyle.Light);
                    light.ImpactOccurred();
                    light.Prepare();
                    break;
            }
#endif
        }
        catch
        {
            // No hardware, feedback disabled, or the window went away mid-gesture. Nothing to recover.
        }
    }
}

/// <summary><see cref="IHaptics"/> for viewmodels; a thin adapter over <see cref="Feedback"/>.</summary>
public sealed class MauiHaptics : IHaptics
{
    public void Perform(HapticCue cue) => Feedback.Perform(cue);
}
