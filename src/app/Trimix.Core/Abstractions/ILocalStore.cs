using Trimix.Contracts.Dtos;

namespace Trimix.Core.Abstractions;

/// <summary>
/// Local persistence abstraction (SQLite via sqlite-net-pcl in the implementation):
/// the user's dives (saved locally first, always) and the offline analytics queue.
/// </summary>
public interface ILocalStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DiveDto>> GetDivesAsync(CancellationToken cancellationToken = default);

    Task UpsertDiveAsync(DiveDto dive, CancellationToken cancellationToken = default);

    Task DeleteDiveAsync(Guid diveId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Hard-removes every dive from the local store (NOT a tombstone). Used on sign-out / account
    /// switch so one account's dives never leak into the next account signed in on the device.
    /// </summary>
    Task ClearDivesAsync(CancellationToken cancellationToken = default);

    Task EnqueueAnalyticsEventAsync(AnalyticsEventDto analyticsEvent, CancellationToken cancellationToken = default);

    /// <summary>Peeks up to <paramref name="max"/> queued events, oldest first.</summary>
    Task<IReadOnlyList<AnalyticsEventDto>> PeekAnalyticsEventsAsync(int max, CancellationToken cancellationToken = default);

    /// <summary>Removes events acknowledged by the server from the queue.</summary>
    Task RemoveAnalyticsEventsAsync(IReadOnlyList<AnalyticsEventDto> analyticsEvents, CancellationToken cancellationToken = default);
}
