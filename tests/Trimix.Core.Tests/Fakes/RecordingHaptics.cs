using Trimix.Core.Abstractions;

namespace Trimix.Core.Tests.Fakes;

/// <summary>
/// Records every <see cref="HapticCue"/> a viewmodel fires, in order. Haptics are the only feedback the
/// app gives for several outcomes — the design removed toasts, so a failed sync reports itself inline in a
/// sheet that then closes — which makes the cue load-bearing rather than decorative, and worth pinning.
/// </summary>
public sealed class RecordingHaptics : IHaptics
{
    private readonly List<HapticCue> _cues = [];

    public IReadOnlyList<HapticCue> Cues => _cues;

    public void Perform(HapticCue cue) => _cues.Add(cue);
}
