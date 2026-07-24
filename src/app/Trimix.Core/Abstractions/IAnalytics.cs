using Trimix.Contracts;

namespace Trimix.Core.Abstractions;

/// <summary>
/// Client analytics pipe (architecture overview §7): validates against the frozen
/// <see cref="FunnelEvents"/> contract (unknown event name or missing required prop throws in Debug,
/// drops + logs in Release), enqueues to local SQLite, batches to <c>POST /api/v1/analytics/events</c>.
/// </summary>
public interface IAnalytics
{
    /// <summary>Stable per-install pseudo id stamped onto every event (created on first use).</summary>
    string UserPseudoId { get; }

    /// <summary>Records a funnel event. <paramref name="name"/> and prop keys MUST be <see cref="FunnelEvents"/> constants.</summary>
    void Track(string name, IReadOnlyDictionary<string, string>? props = null);

    /// <summary>
    /// Emits <see cref="FunnelEvents.Install"/> exactly once per install (guarded by a preference
    /// flag, which uninstall wipes — matching the "per install" semantics). Idempotent.
    /// </summary>
    Task EnsureInstallTrackedAsync(string source, CancellationToken cancellationToken = default);

    /// <summary>
    /// Emits <see cref="FunnelEvents.FirstRunCompleted"/> exactly once, after onboarding finishes. Idempotent.
    /// </summary>
    Task EnsureFirstRunCompletedTrackedAsync(CancellationToken cancellationToken = default);

    /// <summary>Pushes any queued events to the server now (best effort; queue survives offline).</summary>
    Task FlushAsync(CancellationToken cancellationToken = default);
}
