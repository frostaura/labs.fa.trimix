namespace Trimix.Core.Abstractions;

/// <summary>Testable time source.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
