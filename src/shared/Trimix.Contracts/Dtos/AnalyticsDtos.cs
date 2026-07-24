namespace Trimix.Contracts.Dtos;

/// <summary>
/// One funnel event in an ingest batch. <paramref name="Name"/> MUST be a <see cref="FunnelEvents"/>
/// constant and <paramref name="Props"/> must contain that event's <see cref="FunnelEvents.RequiredProps"/>
/// (values stringified at the edge; the server stores props append-only as JSON).
/// </summary>
/// <param name="EventId">
/// Optional client-supplied idempotency key enabling server-side dedup of retried batches. Defaults
/// to <see cref="Guid.Empty"/> so existing callers/tests compile unchanged; when empty the server
/// treats the row as non-deduped. Clients SHOULD stamp a stable per-event Guid before first send.
/// </param>
public sealed record AnalyticsEventDto(
    string Name,
    IReadOnlyDictionary<string, string> Props,
    string UserPseudoId,
    DateTime ClientTimestampUtc,
    string Platform,
    string AppVersion,
    Guid EventId = default);

/// <summary><c>POST /analytics/events</c> request (pseudo-id allowed pre-auth).</summary>
public sealed record AnalyticsBatchRequest(IReadOnlyList<AnalyticsEventDto> Events);

/// <summary><c>POST /analytics/events</c> response.</summary>
public sealed record AnalyticsBatchResponse(int Accepted);
