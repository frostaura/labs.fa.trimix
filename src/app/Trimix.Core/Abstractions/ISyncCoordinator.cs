using Trimix.Contracts.Dtos;

namespace Trimix.Core.Abstractions;

/// <summary>Which leg of the "Sync now" run a <see cref="SyncProgress"/> report describes.</summary>
public enum SyncStage { Importing, Pushing, Done }

/// <summary>
/// One streaming progress tick of a "Sync now" run — the model the sync bottom sheet renders
/// ("N imported · checking &lt;source&gt; · PADI &amp; SSI kept in sync"). <paramref name="ConnectorId"/> is
/// the connector currently being worked (null once <see cref="SyncStage.Done"/>).
/// </summary>
public sealed record SyncProgress(
    SyncStage Stage,
    string? ConnectorId,
    int SourcesDone,
    int SourcesTotal,
    int ImportedSoFar,
    int PushedSoFar);

/// <summary>Per-source outcome of one sync run. <paramref name="Error"/> is non-null when the source threw
/// (a broken vendor endpoint never aborts the whole run).</summary>
public sealed record SourceSyncOutcome(
    string ConnectorId,
    ConnectorAuthStatus Auth,
    int Imported,
    int Fetched,
    bool CapReached,
    string? Error = null);

/// <summary>Per-destination outcome of the push leg (counts over the run's newly-imported dives).</summary>
public sealed record DestinationPushOutcome(
    string ConnectorId,
    int Pushed,
    int Skipped,
    int Failed,
    string? Error = null);

/// <summary>
/// Everything one "Sync now" run did: every consented source's outcome, the deduplicated newly-imported
/// dives, every consented destination's push counts, and whether any source hit the free-tier cap
/// (the paywall moment already emitted by <c>ImportService</c>).
/// </summary>
public sealed record SyncReport(
    IReadOnlyList<SourceSyncOutcome> Sources,
    IReadOnlyList<DiveDto> NewDives,
    IReadOnlyList<DestinationPushOutcome> Destinations,
    bool CapReached);

/// <summary>
/// The generic "Sync now" entry point — the production caller of <c>ImportService</c>. One call pulls from
/// every LINKED (consented + authenticated) source connector incrementally via a persisted per-connector
/// <see cref="SyncCursor"/>, then pushes the newly-imported dives to every LINKED destination connector.
/// Unlinked connectors are skipped with zero I/O; per-user consent + the connectors' own gating remain the
/// only opt-in surfaces.
/// </summary>
public interface ISyncCoordinator
{
    /// <summary>True when at least one source connector has user consent — drives the radar button's
    /// enabled state ("Sync now" is disabled until a provider is linked).</summary>
    bool HasLinkedSources { get; }

    /// <summary>
    /// Runs one full sync: consented sources → <c>ImportService.ImportFromConnectorAsync</c> (incremental,
    /// cursor persisted per connector) → deduplicated new dives → consented destinations'
    /// <c>PushDiveAsync</c>. <paramref name="onCapReached"/> is the paywall UI hook (forwarded to the
    /// import pipeline); <paramref name="progress"/> streams <see cref="SyncProgress"/> ticks for the sheet.
    /// </summary>
    Task<SyncReport> SyncNowAsync(
        Func<Task>? onCapReached = null,
        IProgress<SyncProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
