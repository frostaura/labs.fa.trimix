namespace Trimix.Core.Abstractions;

/// <summary>
/// Local-first dive sync (architecture overview §6): pushes/pulls when online + authed,
/// last-write-wins on <c>UpdatedAtUtc</c>. Guest mode is local-only; on account creation local dives upload.
/// </summary>
public interface ISyncService
{
    Task SyncAsync(CancellationToken cancellationToken = default);
}
