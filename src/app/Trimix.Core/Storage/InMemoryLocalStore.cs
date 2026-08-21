using Trimix.Contracts.Dtos;
using Trimix.Core.Abstractions;

namespace Trimix.Core.Storage;

/// <summary>
/// In-memory <see cref="ILocalStore"/> for tests and dev hosts without a database path.
/// Semantics mirror <see cref="SqliteLocalStore"/>: dive deletion is a tombstone
/// (<see cref="DiveDto.Deleted"/> = true) so SyncService can push deletions.
/// </summary>
public sealed class InMemoryLocalStore : ILocalStore
{
    private readonly Dictionary<Guid, DiveDto> _dives = [];
    private readonly List<AnalyticsEventDto> _analyticsQueue = [];
    private readonly Lock _gate = new();

    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<IReadOnlyList<DiveDto>> GetDivesAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<DiveDto>>([.. _dives.Values]);
        }
    }

    public Task UpsertDiveAsync(DiveDto dive, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dive);
        lock (_gate)
        {
            _dives[dive.Id] = dive;
        }

        return Task.CompletedTask;
    }

    public Task DeleteDiveAsync(Guid diveId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_dives.TryGetValue(diveId, out var dive))
            {
                _dives[diveId] = dive with { Deleted = true };
            }
        }

        return Task.CompletedTask;
    }

    public Task<int> DeleteDivesByVendorAsync(string sourceVendor, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourceVendor);
        lock (_gate)
        {
            // Hard delete (not a tombstone): the provider is being unlinked, and a tombstone would be
            // resurrected by the next sync of a still-consented provider.
            var ids = _dives.Values
                .Where(d => string.Equals(d.SourceVendor, sourceVendor, StringComparison.Ordinal))
                .Select(d => d.Id)
                .ToList();
            foreach (var id in ids)
            {
                _dives.Remove(id);
            }

            return Task.FromResult(ids.Count);
        }
    }

    public Task ClearDivesAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _dives.Clear();
        }

        return Task.CompletedTask;
    }

    public Task EnqueueAnalyticsEventAsync(AnalyticsEventDto analyticsEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(analyticsEvent);
        lock (_gate)
        {
            _analyticsQueue.Add(analyticsEvent);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AnalyticsEventDto>> PeekAnalyticsEventsAsync(int max, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<AnalyticsEventDto>>([.. _analyticsQueue.Take(max)]);
        }
    }

    public Task RemoveAnalyticsEventsAsync(IReadOnlyList<AnalyticsEventDto> analyticsEvents, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(analyticsEvents);
        lock (_gate)
        {
            foreach (var analyticsEvent in analyticsEvents)
            {
                var index = _analyticsQueue.FindIndex(e => ReferenceEquals(e, analyticsEvent));
                if (index < 0)
                {
                    index = _analyticsQueue.FindIndex(e =>
                        e.Name == analyticsEvent.Name
                        && e.ClientTimestampUtc == analyticsEvent.ClientTimestampUtc
                        && e.UserPseudoId == analyticsEvent.UserPseudoId);
                }

                if (index >= 0)
                {
                    _analyticsQueue.RemoveAt(index);
                }
            }
        }

        return Task.CompletedTask;
    }
}
