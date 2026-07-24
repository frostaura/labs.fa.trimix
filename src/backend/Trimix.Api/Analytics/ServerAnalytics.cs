using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Trimix.Api.Data;

namespace Trimix.Api.Analytics;

/// <summary>Request headers understood by the Trimix API. Names are owned by the shared contract.</summary>
public static class TrimixHeaders
{
    /// <summary>
    /// The client's analytics pseudo-id. Sent by the app on API calls that cause server-emitted
    /// funnel events (<c>account_created</c>, <c>entitlement_granted</c>, <c>entitlement_restored</c>)
    /// so those rows reconcile into the same per-pseudo-user funnel as client-emitted events.
    /// Delegates to the canonical <see cref="Trimix.Contracts.TrimixHeaders.UserPseudoId"/>.
    /// </summary>
    public const string UserPseudoId = Trimix.Contracts.TrimixHeaders.UserPseudoId;
}

/// <summary>
/// Emits server-side funnel events as append-only <see cref="AnalyticsEvent"/> rows, validated
/// against the frozen contract. Rows are added to the current unit of work — the calling endpoint's
/// SaveChanges commits them atomically with the state change they describe.
/// </summary>
public sealed class ServerAnalytics(AppDbContext db)
{
    /// <summary>Platform recorded for server-emitted rows.</summary>
    public const string ServerPlatform = "server";

    /// <summary>Adds one validated, server-emitted funnel event row (not yet saved).</summary>
    public void Emit(string name, IReadOnlyDictionary<string, string> props, string userPseudoId, Guid? userId)
    {
        var error = FunnelContract.Validate(name, props);
        if (error is not null)
        {
            throw new InvalidOperationException($"Server-emitted analytics event violates the frozen contract: {error}");
        }

        var now = DateTime.UtcNow;
        db.AnalyticsEvents.Add(new AnalyticsEvent
        {
            UserPseudoId = userPseudoId,
            UserId = userId,
            Name = name,
            PropsJson = JsonSerializer.Serialize(props),
            ClientTimestampUtc = now,
            ReceivedAtUtc = now,
            Platform = ServerPlatform,
            AppVersion = ServerPlatform,
        });
    }

    /// <summary>
    /// The pseudo-id to attribute a server-emitted event to: the client-declared header when present,
    /// falling back to the user id (still reconcilable server-side, just not to a device funnel).
    /// </summary>
    public static string ResolvePseudoId(HttpContext httpContext, Guid userId)
    {
        var header = httpContext.Request.Headers[TrimixHeaders.UserPseudoId].ToString();
        return string.IsNullOrWhiteSpace(header) ? userId.ToString() : header;
    }

    /// <summary>
    /// Best-effort pseudo-id for a user who is not the current caller (e.g. the contributor when a
    /// moderator resolves a contribution): their most recent analytics row, else their user id.
    /// </summary>
    public async Task<string> ResolvePseudoIdForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var latest = await db.AnalyticsEvents
            .Where(e => e.UserId == userId)
            .OrderByDescending(e => e.Id)
            .Select(e => e.UserPseudoId)
            .FirstOrDefaultAsync(cancellationToken);
        return latest ?? userId.ToString();
    }
}
