using Trimix.Contracts.Dtos;

namespace Trimix.Core.Abstractions;

/// <summary>Progress of one import session, for the sync UI's progress state.</summary>
public sealed record ImportProgress(string Stage, int Done, int Total);

/// <summary>
/// Outcome of one import session. <paramref name="CapReached"/> means the free-tier per-session
/// cap truncated the fetched dives — the caller shows the purchase moment
/// (<c>paywall_shown</c> with <c>trigger=cloud_sync_cap</c>).
/// </summary>
public sealed record ImportResult(
    IReadOnlyList<DiveDto> ImportedDives,
    int FetchedCount,
    bool CapReached,
    bool SiteMatched);

/// <summary>
/// The generic, source-agnostic import pipeline (the app's single ingestion path post-serverless):
/// authenticate the connector, list dives since the cursor, fetch each profile, then site auto-match →
/// free-tier truncation → save → <c>dive_logged_import</c> → paywall trigger.
/// </summary>
public interface IImportService
{
    /// <summary>
    /// Imports from one <see cref="IDiveSourceConnector"/>. <paramref name="onCapReached"/> is invoked
    /// (after the allowed dives are saved) when the free cap truncated the import — the paywall UI hook.
    /// </summary>
    Task<ImportResult> ImportFromConnectorAsync(
        IDiveSourceConnector connector,
        SyncCursor cursor,
        Func<Task>? onCapReached = null,
        IProgress<ImportProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
