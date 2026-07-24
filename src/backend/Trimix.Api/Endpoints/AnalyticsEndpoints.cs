using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Trimix.Api.Analytics;
using Trimix.Api.Auth;
using Trimix.Api.Data;
using Trimix.Contracts;
using Trimix.Contracts.Dtos;

namespace Trimix.Api.Endpoints;

/// <summary>
/// POST /analytics/events (architecture overview §7): append-only batch ingest. Works
/// unauthenticated with a client pseudo-id (pre-account funnel); a valid bearer token additionally
/// links rows to the account. Every event is validated against the FROZEN taxonomy — unknown names
/// or missing required props reject the batch.
/// </summary>
public static class AnalyticsEndpoints
{
    private const int MaxBatchSize = 1000;

    public static RouteGroupBuilder MapAnalyticsEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup(ApiRoutes.Analytics.Group);

        group.MapPost(ApiRoutes.Analytics.Events, async (
            AnalyticsBatchRequest request,
            HttpContext httpContext,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var events = request.Events ?? [];
            if (events.Count > MaxBatchSize)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    detail: $"Batch exceeds the maximum of {MaxBatchSize} events.");
            }

            foreach (var e in events)
            {
                // clientSubmitted: reject server-reserved names (account_created / entitlement_*) and
                // drifted enumerated values in addition to unknown names / missing required props.
                var error = FunnelContract.Validate(e.Name, e.Props, clientSubmitted: true);
                if (error is not null)
                {
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest, detail: error);
                }

                if (string.IsNullOrWhiteSpace(e.UserPseudoId))
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        detail: $"Event '{e.Name}' is missing its userPseudoId.");
                }
            }

            // Idempotency: drop rows whose (pseudoId, EventId) is already stored (retried batch) or
            // repeated within this batch. Guid.Empty keys are never deduped (client opted out).
            var keyed = events.Where(e => e.EventId != Guid.Empty).ToList();
            var alreadyStored = new HashSet<(string, Guid)>();
            if (keyed.Count > 0)
            {
                var pseudoIds = keyed.Select(e => e.UserPseudoId).ToHashSet();
                var eventIds = keyed.Select(e => e.EventId).ToHashSet();
                var existing = await db.AnalyticsEvents
                    .Where(e => pseudoIds.Contains(e.UserPseudoId) && eventIds.Contains(e.EventId))
                    .Select(e => new { e.UserPseudoId, e.EventId })
                    .ToListAsync(cancellationToken);
                foreach (var row in existing)
                {
                    alreadyStored.Add((row.UserPseudoId, row.EventId));
                }
            }

            var userId = httpContext.User.UserIdOrNull(); // Optional: pseudo-id-only pre-auth ingest is fine.
            var now = DateTime.UtcNow;
            var accepted = 0;
            var seenInBatch = new HashSet<(string, Guid)>();
            foreach (var e in events)
            {
                if (e.EventId != Guid.Empty)
                {
                    var key = (e.UserPseudoId, e.EventId);
                    if (alreadyStored.Contains(key) || !seenInBatch.Add(key))
                    {
                        continue; // Deduped.
                    }
                }

                db.AnalyticsEvents.Add(new AnalyticsEvent
                {
                    EventId = e.EventId,
                    UserPseudoId = e.UserPseudoId,
                    UserId = userId,
                    Name = e.Name,
                    PropsJson = JsonSerializer.Serialize(e.Props),
                    // Npgsql's timestamptz only accepts Kind=Utc; wire timestamps are UTC by contract.
                    ClientTimestampUtc = e.ClientTimestampUtc.Kind == DateTimeKind.Utc
                        ? e.ClientTimestampUtc
                        : DateTime.SpecifyKind(e.ClientTimestampUtc, DateTimeKind.Utc),
                    ReceivedAtUtc = now,
                    Platform = e.Platform,
                    AppVersion = e.AppVersion,
                });
                accepted++;
            }

            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(new AnalyticsBatchResponse(accepted));
        }).RequireRateLimiting(RateLimitPolicies.AnalyticsIngest);

        return api;
    }
}
