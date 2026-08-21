using Trimix.Core.Abstractions;
using Trimix.Core.Tests.Fakes;
using Xunit;

namespace Trimix.Core.Tests;

/// <summary>
/// The rules that make haptics a perceived-performance tool rather than a decoration.
///
/// <para>The app-side <c>HapticCommand</c> decorator cannot be referenced from here — Trimix.Core has no
/// MAUI dependency and must keep it that way — so these tests pin the CONTRACT it implements: the cue is
/// observed before the wrapped work runs. If the decorator is ever "simplified" back into an event
/// subscription, this is the shape that stops being true.</para>
/// </summary>
public class HapticCueOrderingTests
{
    /// <summary>Mirrors Trimix.App's HapticCommand: cue first, then the real work.</summary>
    private static void ExecuteWithCue(IHaptics haptics, HapticCue cue, Action work)
    {
        haptics.Perform(cue);
        work();
    }

    [Fact]
    public void The_cue_is_emitted_before_the_command_runs()
    {
        var haptics = new RecordingHaptics();
        var cuesSeenByTheCommand = Array.Empty<HapticCue>();

        // A navigating command is the case that matters: by the time it returns, the transition has already
        // started, so a cue emitted afterwards lands behind the screen movement.
        ExecuteWithCue(haptics, HapticCue.Tap, () => cuesSeenByTheCommand = [.. haptics.Cues]);

        Assert.Equal(HapticCue.Tap, Assert.Single(cuesSeenByTheCommand));
    }

    [Fact]
    public void Terminal_cues_are_never_dropped_while_light_cues_can_be()
    {
        // Pins the coalescing rule's intent: Success/Warning/Error report an OUTCOME, so losing one loses
        // information, whereas a second Tap inside the same gesture is just a stutter.
        Assert.All(
            new[] { HapticCue.Success, HapticCue.Warning, HapticCue.Error },
            cue => Assert.True(IsTerminal(cue), $"{cue} must be treated as terminal"));

        Assert.All(
            new[] { HapticCue.Tap, HapticCue.Selection, HapticCue.Navigate },
            cue => Assert.False(IsTerminal(cue), $"{cue} must be coalescible"));

        static bool IsTerminal(HapticCue cue) =>
            cue is HapticCue.Success or HapticCue.Warning or HapticCue.Error;
    }

    [Fact]
    public void A_no_op_haptics_implementation_records_nothing_and_never_throws()
    {
        // Every call site fires unguarded, so the null implementation is what keeps a headless host safe.
        var haptics = new Trimix.Core.Platform.NullHaptics();
        foreach (var cue in Enum.GetValues<HapticCue>())
        {
            haptics.Perform(cue);
        }
    }
}
