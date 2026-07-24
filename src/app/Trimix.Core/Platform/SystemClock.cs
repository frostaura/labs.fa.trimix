using Trimix.Core.Abstractions;

namespace Trimix.Core.Platform;

/// <summary>Wall-clock <see cref="IClock"/>.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
