using Trimix.Contracts.Dtos;

namespace Trimix.Core.Abstractions;

/// <summary>Progress of one import session, for the Import screen's progress state.</summary>
public sealed record ImportProgress(string Stage, int Done, int Total);

/// <summary>
/// Outcome of one import session. <paramref name="CapReached"/> means the free-tier per-session
/// cap truncated the fetched dives — the caller shows the purchase moment
/// (<c>paywall_shown</c> with <c>trigger=ble_import_cap</c>).
/// </summary>
public sealed record ImportResult(
    IReadOnlyList<DiveDto> ImportedDives,
    int FetchedCount,
    bool CapReached,
    bool SiteMatched);

/// <summary>
/// Orchestrates the BLE import flow (architecture overview §4/§9):
/// scan → connect → fetch via the vendor protocol → site auto-match (nearest site within
/// <see cref="ImportService.SiteAutoMatchRadiusKm"/> via <c>GET /sites</c> geo search) →
/// save locally, respecting the EntitlementGate import-session cap.
/// </summary>
public interface IImportService
{
    /// <summary>Scans for nearby dive computers until cancelled.</summary>
    IAsyncEnumerable<DiscoveredComputer> ScanAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Connects to <paramref name="computer"/>, fetches and saves its dives.
    /// <paramref name="onCapReached"/> is invoked (after the allowed dives are saved) when the
    /// free cap truncated the import — the paywall trigger callback.
    /// </summary>
    Task<ImportResult> ImportAsync(
        DiscoveredComputer computer,
        Func<Task>? onCapReached = null,
        IProgress<ImportProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The generic, source-agnostic import path (architecture overview §2): authenticate the
    /// <paramref name="connector"/>, list dives since <paramref name="cursor"/>, fetch each profile, then run
    /// the same downstream (site auto-match → free-tier truncation → save → <c>dive_logged_import</c> →
    /// paywall trigger) as the BLE path. This is the path HealthKit / file / cloud sources use later.
    /// </summary>
    Task<ImportResult> ImportFromConnectorAsync(
        IDiveSourceConnector connector,
        SyncCursor cursor,
        Func<Task>? onCapReached = null,
        IProgress<ImportProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
