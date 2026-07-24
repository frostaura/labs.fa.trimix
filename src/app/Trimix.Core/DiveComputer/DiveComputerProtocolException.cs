namespace Trimix.Core.DiveComputer;

/// <summary>A vendor frame stream violated the protocol (truncated, corrupt, or out-of-order frames).</summary>
public sealed class DiveComputerProtocolException(string message) : Exception(message);
