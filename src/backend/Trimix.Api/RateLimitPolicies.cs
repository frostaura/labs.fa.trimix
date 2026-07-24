namespace Trimix.Api;

/// <summary>
/// Named rate-limit policies and their partitioning. Abuse controls for the vertical slice: the
/// unauthenticated analytics ingest is throttled (forge/flood protection for the gate metrics and
/// append-only table), and auth login is rate-limited to blunt online password brute force /
/// credential stuffing. Partitioned by client IP; fixed-window.
/// </summary>
public static class RateLimitPolicies
{
    /// <summary>Policy name applied to <c>POST /auth/login</c>.</summary>
    public const string Login = "login";

    /// <summary>Policy name applied to the analytics ingest group.</summary>
    public const string AnalyticsIngest = "analytics-ingest";

    /// <summary>Login attempts permitted per window per IP before <c>429</c>.</summary>
    public const int LoginPermitLimit = 10;

    /// <summary>Ingest requests permitted per window per IP before <c>429</c>.</summary>
    public const int AnalyticsPermitLimit = 200;

    /// <summary>Fixed window length for both policies.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    /// <summary>Client IP partition key (falls back to a constant when the IP is unavailable).</summary>
    public static string PartitionKey(HttpContext httpContext) =>
        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
