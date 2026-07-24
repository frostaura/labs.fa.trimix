namespace Trimix.Core.Abstractions;

/// <summary>Network reachability, driving sync + analytics flushing.</summary>
public interface IConnectivityService
{
    bool IsOnline { get; }

    /// <summary>Raised when reachability changes; payload is the new <see cref="IsOnline"/>.</summary>
    event EventHandler<bool>? ConnectivityChanged;
}
