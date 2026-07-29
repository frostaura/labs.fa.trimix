using System.Globalization;
using Microsoft.Extensions.Logging;
using Trimix.Contracts.Dtos;
using Trimix.Core.Abstractions;
using Trimix.Core.Connectors.Infra;

namespace Trimix.Core.DiveComputer;

/// <summary>
/// The production "Sync now" caller of <see cref="IImportService"/> (the pipeline had none after the BLE
/// removal). Enumerates every registered <see cref="IDiveSourceConnector"/>; for each with user consent AND
/// a Granted auth it runs the standard import pipeline incrementally via a persisted per-connector
/// <see cref="SyncCursor"/>, collects the newly-imported dives (deduplicated by their deterministic id),
/// then pushes them to every consented <see cref="IDiveDestinationConnector"/>.
///
/// <para><b>Gating is inherited, not re-invented:</b> an unconsented connector is skipped here with zero
/// I/O, and even if this class mis-skipped, the connectors' own consent gates and
/// <c>ImportService</c>'s Granted-only check still hold. All funnel events (<c>source_connected</c>,
/// <c>dives_synced</c>, <c>dive_logged_import</c>, <c>paywall_shown</c>) are emitted by the import
/// pipeline — this coordinator adds none.</para>
///
/// <para><b>Cursor discipline:</b> the cursor is stored per connector in <see cref="ISecureTokenStore"/>
/// (key <see cref="ConnectorTokenKeys.SyncCursor"/>, cleared on disconnect alongside the token) as the
/// newest imported dive's start instant. It only advances on a fully-imported run — when the free-tier cap
/// truncated the fetch, the cursor stays put so the dives beyond the cap are re-fetched after upgrade
/// (re-imports are harmless: the deterministic dive id upserts onto the same row).</para>
/// </summary>
public sealed class SyncCoordinator(
    IEnumerable<IDiveSourceConnector> sources,
    IEnumerable<IDiveDestinationConnector> destinations,
    IImportService importService,
    IConnectorConsentStore consent,
    ISecureTokenStore tokens,
    ILogger<SyncCoordinator> logger) : ISyncCoordinator
{
    private IReadOnlyList<IDiveSourceConnector> ActiveSources => sources as IReadOnlyList<IDiveSourceConnector> ?? [.. sources];

    private IReadOnlyList<IDiveDestinationConnector> ActiveDestinations => destinations as IReadOnlyList<IDiveDestinationConnector> ?? [.. destinations];

    public bool HasLinkedSources => ActiveSources.Any(s => consent.HasConsent(s.Descriptor.ConnectorId));

    public async Task<SyncReport> SyncNowAsync(
        Func<Task>? onCapReached = null,
        IProgress<SyncProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var linkedSources = ActiveSources.Where(s => consent.HasConsent(s.Descriptor.ConnectorId)).ToList();
        var sourceOutcomes = new List<SourceSyncOutcome>(linkedSources.Count);
        var newDives = new Dictionary<Guid, DiveDto>(); // dedup by the deterministic dive id
        var capReached = false;

        // ── Pull leg: every linked source, incrementally, one at a time (streaming UX; fragile endpoints). ──
        for (var i = 0; i < linkedSources.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var connector = linkedSources[i];
            var id = connector.Descriptor.ConnectorId;
            progress?.Report(new SyncProgress(SyncStage.Importing, id, i, linkedSources.Count, newDives.Count, 0));

            try
            {
                var auth = await connector.AuthenticateAsync(cancellationToken).ConfigureAwait(false);
                if (auth.Status != ConnectorAuthStatus.Granted)
                {
                    // Consented but not signed in (or auth expired) — the relink surface, not an error.
                    sourceOutcomes.Add(new SourceSyncOutcome(id, auth.Status, 0, 0, false));
                    continue;
                }

                var cursor = await LoadCursorAsync(id, cancellationToken).ConfigureAwait(false);
                var result = await importService.ImportFromConnectorAsync(
                    connector, cursor, onCapReached, progress: null, cancellationToken).ConfigureAwait(false);

                foreach (var dive in result.ImportedDives)
                {
                    newDives[dive.Id] = dive;
                }

                capReached |= result.CapReached;
                await AdvanceCursorAsync(id, result, cancellationToken).ConfigureAwait(false);
                sourceOutcomes.Add(new SourceSyncOutcome(
                    id, ConnectorAuthStatus.Granted, result.ImportedDives.Count, result.FetchedCount, result.CapReached));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Every reverse-engineered endpoint is fragile; one broken vendor must never abort the run.
                logger.LogWarning(ex, "Sync source {ConnectorId} failed.", id);
                sourceOutcomes.Add(new SourceSyncOutcome(id, ConnectorAuthStatus.Granted, 0, 0, false, ex.Message));
            }

            progress?.Report(new SyncProgress(SyncStage.Importing, id, i + 1, linkedSources.Count, newDives.Count, 0));
        }

        // ── Push leg: every linked destination gets each newly-imported dive. ──
        var dives = newDives.Values.OrderBy(d => d.StartUtc).ToList();
        var destinationOutcomes = new List<DestinationPushOutcome>();
        var pushedTotal = 0;
        foreach (var destination in ActiveDestinations.Where(d => consent.HasConsent(d.Descriptor.ConnectorId)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = destination.Descriptor.ConnectorId;
            progress?.Report(new SyncProgress(
                SyncStage.Pushing, id, linkedSources.Count, linkedSources.Count, dives.Count, pushedTotal));

            int pushed = 0, skipped = 0, failed = 0;
            string? error = null;
            try
            {
                foreach (var dive in dives)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var push = await destination.PushDiveAsync(dive, cancellationToken).ConfigureAwait(false);
                    switch (push.Status)
                    {
                        case PushStatus.Pushed:
                            pushed++;
                            pushedTotal++;
                            break;
                        case PushStatus.Failed:
                            failed++;
                            break;
                        default: // Skipped / Unsupported — a not-signed-in or unconfigured destination, not an error
                            skipped++;
                            break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Sync push to {ConnectorId} failed.", id);
                error = ex.Message;
            }

            destinationOutcomes.Add(new DestinationPushOutcome(id, pushed, skipped, failed, error));
        }

        progress?.Report(new SyncProgress(
            SyncStage.Done, null, linkedSources.Count, linkedSources.Count, dives.Count, pushedTotal));
        return new SyncReport(sourceOutcomes, dives, destinationOutcomes, capReached);
    }

    private async Task<SyncCursor> LoadCursorAsync(string connectorId, CancellationToken ct)
    {
        var stored = await tokens.GetAsync(ConnectorTokenKeys.SyncCursor(connectorId), ct).ConfigureAwait(false);
        return stored is not null &&
               DateTime.TryParse(stored, CultureInfo.InvariantCulture,
                   DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var since)
            ? new SyncCursor(DateTime.SpecifyKind(since, DateTimeKind.Utc))
            : SyncCursor.None;
    }

    /// <summary>Advances the persisted cursor to the newest imported dive — only when nothing was truncated
    /// (see class remarks on the cap interplay).</summary>
    private async Task AdvanceCursorAsync(string connectorId, ImportResult result, CancellationToken ct)
    {
        if (result.CapReached || result.ImportedDives.Count == 0)
        {
            return;
        }

        var newest = result.ImportedDives.Max(d => d.StartUtc);
        await tokens.SetAsync(
            ConnectorTokenKeys.SyncCursor(connectorId),
            newest.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            ct).ConfigureAwait(false);
    }
}
