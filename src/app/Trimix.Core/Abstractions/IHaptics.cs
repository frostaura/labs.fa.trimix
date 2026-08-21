namespace Trimix.Core.Abstractions;

/// <summary>
/// The app's whole vocabulary of touch feedback. Deliberately small and SEMANTIC — call sites say what
/// happened, not how hard to buzz, so the platform implementation can use each OS's native idiom
/// (Taptic Engine notification/impact/selection generators on iOS, <c>performHapticFeedback</c>
/// constants on Android) instead of a lowest-common-denominator vibration.
///
/// <para>Haptics here are a PERCEIVED-PERFORMANCE tool: a tick that lands on the touch, before any
/// layout or navigation work completes, makes the app feel instant even when the transition takes a
/// beat. That is why <see cref="HapticCue.Tap"/> and <see cref="HapticCue.Navigate"/> fire on press /
/// on the navigation intent, never after the destination has rendered.</para>
/// </summary>
public enum HapticCue
{
    /// <summary>The workhorse: a button, card or row was pressed. Lightest cue; fires on press.</summary>
    Tap,

    /// <summary>Moved between discrete choices — section switcher, chart scrub, segmented control.</summary>
    Selection,

    /// <summary>A page or tab transition was requested. Fires on the intent, not on arrival.</summary>
    Navigate,

    /// <summary>An operation completed: sync finished, provider linked, purchase granted.</summary>
    Success,

    /// <summary>A recoverable refusal: free-tier cap reached, validation rejected the input.</summary>
    Warning,

    /// <summary>An operation failed: sign-in rejected, sync errored.</summary>
    Error,
}

/// <summary>
/// Platform touch feedback. A no-op implementation is always registered, so call sites never guard —
/// unsupported hardware, a disabled system setting and a headless test host all just do nothing.
/// </summary>
public interface IHaptics
{
    /// <summary>Fire the given cue. Never throws, and never blocks the caller.</summary>
    void Perform(HapticCue cue);
}
