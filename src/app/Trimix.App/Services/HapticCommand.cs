using System.Windows.Input;
using Trimix.Core.Abstractions;

namespace Trimix.App.Services;

/// <summary>
/// Wraps a gesture recognizer's <see cref="ICommand"/ > so the haptic cue is emitted one statement BEFORE
/// the real command runs.
///
/// <para><b>Why a command decorator rather than the <c>Tapped</c> event.</b> A
/// <c>TapGestureRecognizer</c> raises <c>Tapped</c> and invokes its bound <c>Command</c> in the same
/// dispatch, and which of the two goes first is a MAUI implementation detail. If the command wins, it
/// starts navigation — page construction, layout, a transition — and the tick that was supposed to
/// acknowledge the touch lands after the screen has already moved. A tick that arrives after the
/// transition confirms nothing and makes the app feel SLOWER than no haptic at all.
///
/// Emitting from inside <see cref="Execute"/> removes the question entirely: the cue is guaranteed to
/// precede the command no matter how MAUI orders the two. That guarantee is the whole reason this type
/// exists — do not "simplify" it back to an event subscription.</para>
///
/// <para><see cref="CanExecuteChanged"/> is forwarded rather than re-raised so <c>[RelayCommand]</c>'s
/// disabled-state gating keeps working; a wrapper that swallowed it would silently strand buttons in a
/// stale enabled/disabled state.</para>
/// </summary>
internal sealed class HapticCommand(ICommand inner, HapticCue cue) : ICommand
{
    /// <summary>The wrapped command — lets the hook detect an already-wrapped command and never double-wrap.</summary>
    public ICommand Inner { get; } = inner;

    public HapticCue Cue { get; } = cue;

    public bool CanExecute(object? parameter) => Inner.CanExecute(parameter);

    public void Execute(object? parameter)
    {
        // Order is the entire point. Cue first, always.
        Feedback.Perform(Cue);

        // ...but ordering ALONE is not enough, and this second line is why. Emitting the cue only hands the
        // impact to the system; it still needs the main thread to turn over before the hardware is actually
        // driven. A navigating command jams that thread immediately — resolving the page from DI, inflating
        // its XAML, running the transition — so a cue emitted one statement earlier is still not FELT until
        // all of that finishes. That is the "massive delay, like it waits for the dive to load" symptom on
        // the logbook, and it is why simply firing early looked correct in code while feeling broken.
        //
        // Posting the command yields exactly one runloop turn: the haptic lands under the finger, then the
        // navigation starts ~16ms later, which no one can perceive. Do not "optimise" this back to a direct
        // call.
        MainThread.BeginInvokeOnMainThread(() => Inner.Execute(parameter));
    }

    public event EventHandler? CanExecuteChanged
    {
        add => Inner.CanExecuteChanged += value;
        remove => Inner.CanExecuteChanged -= value;
    }
}
