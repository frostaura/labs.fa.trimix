using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Trimix.Contracts;
using Trimix.Contracts.Dtos;
using Trimix.Core.Abstractions;
using Trimix.Core.Entitlements;
using Trimix.Core.Sites;

namespace Trimix.Core.DiveComputer;

/// <summary>
/// The source-agnostic import pipeline (serverless): authenticate a source connector, list dives since
/// the cursor, fetch each profile, then site auto-match → free-tier truncation → <c>ToDiveDto</c> →
/// local upsert → <c>dive_logged_import</c> per saved dive → paywall trigger on truncation. The generic
/// path emits <c>source_connected</c>, <c>dives_synced</c>, and (on cap) <c>paywall_shown</c> with
/// <c>trigger=cloud_sync_cap</c>. The BLE path and its <c>ble_*</c> events are removed — the constants
/// stay frozen in <see cref="FunnelEvents"/> but are no longer emitted.
/// </summary>
public sealed class ImportService(
    ILocationService location,
    IEntitlementService entitlements,
    ILocalStore store,
    IAnalytics analytics,
    IAppInfo appInfo,
    IClock clock,
    SiteAtlas atlas,
    ILogger<ImportService> logger) : IImportService
{
    /// <summary>Auto-match radius: nearest known site within 2 km of the device claims the dive.</summary>
    public const double SiteAutoMatchRadiusKm = 2.0;

    public async Task<ImportResult> ImportFromConnectorAsync(
        IDiveSourceConnector connector,
        SyncCursor cursor,
        Func<Task>? onCapReached = null,
        IProgress<ImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connector);

        var auth = await connector.AuthenticateAsync(cancellationToken).ConfigureAwait(false);
        // Guardrail (iii) — proceed ONLY when the connector is fully Granted. NeedsInteraction is the
        // designed pre-consent / pre-sign-in state for every gated cloud connector (it carries the
        // disclosure / sign-in prompt for the UI). It, like Denied/Unavailable, must short-circuit to a
        // clean empty result BEFORE any ListDivesSince/FetchProfile network I/O — and before source_connected
        // is emitted, so a not-yet-connected source never appears connected in the frozen aggregator funnel.
        if (auth.Status != ConnectorAuthStatus.Granted)
        {
            return new ImportResult([], 0, false, false);
        }

        // The generic cloud-aggregator spine (PRD Amendments set 2, item 10): the connector id is the
        // free-text 'source' channel (e.g. garmin_connect, suunto_app); it is NOT the frozen
        // dive_logged_import 'import' value.
        var source = connector.Descriptor.ConnectorId;
        analytics.Track(FunnelEvents.SourceConnected, new Dictionary<string, string>
        {
            [FunnelEvents.Props.Source] = source,
            [FunnelEvents.Props.Platform] = appInfo.Platform,
        });

        var refs = await connector.ListDivesSinceAsync(cursor, cancellationToken).ConfigureAwait(false);
        analytics.Track(FunnelEvents.DivesSynced, new Dictionary<string, string>
        {
            [FunnelEvents.Props.Source] = source,
            [FunnelEvents.Props.Count] = refs.Count.ToString(CultureInfo.InvariantCulture),
        });

        var fetched = new List<(ImportedDive Dive, string Vendor)>(refs.Count);
        foreach (var diveRef in refs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dive = await connector.FetchProfileAsync(diveRef, cancellationToken).ConfigureAwait(false);
            fetched.Add((dive, source));
        }

        var result = await PersistFetchedAsync(fetched, onCapReached, progress, cancellationToken).ConfigureAwait(false);

        // The multi-source purchase moment (frozen trigger cloud_sync_cap): the free import cap interrupted
        // "sync all my dives". Emitted from real code here — not delegated to a caller that may forget — so the
        // aggregator install→payer leg is measurable however the sync UI is wired. onCapReached is UI-only.
        if (result.CapReached)
        {
            analytics.Track(FunnelEvents.PaywallShown, new Dictionary<string, string>
            {
                [FunnelEvents.Props.Trigger] = FunnelEvents.Values.TriggerCloudSyncCap,
                [FunnelEvents.Props.EntitlementState] = FunnelEvents.Values.EntitlementStateFree,
            });
        }

        return result;
    }

    /// <summary>
    /// The source-agnostic downstream (architecture overview §1): site auto-match → free-tier truncation
    /// → <c>ToDiveDto</c> → <c>store.UpsertDiveAsync</c> → <c>dive_logged_import</c> per saved dive →
    /// paywall trigger on truncation. Reused verbatim by every source connector.
    /// </summary>
    private async Task<ImportResult> PersistFetchedAsync(
        IReadOnlyList<(ImportedDive Dive, string Vendor)> fetched,
        Func<Task>? onCapReached,
        IProgress<ImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        var matchedSiteId = await TryAutoMatchSiteAsync(cancellationToken).ConfigureAwait(false);

        // "Free keeps your last 5" (prototype monetization moment): the allowance for THIS batch is
        // the free ceiling minus the dives already on device, so the 6th dive is the paywall moment
        // whichever sync it arrives in. Re-imports of dives already stored (deterministic id match)
        // are updates, not new dives — they never consume allowance and never re-trigger the cap
        // (PRD Flow B step 5: unlocking at the cap re-imports onto the same ids).
        var existingIds = (await store.GetDivesAsync(cancellationToken).ConfigureAwait(false))
            .Where(d => !d.Deleted)
            .Select(d => d.Id)
            .ToHashSet();
        var allowance = EntitlementGate.RemainingImports(entitlements.CurrentTier, existingIds.Count);

        var toImport = new List<(ImportedDive Dive, string Vendor)>(fetched.Count);
        var newTotal = 0;
        var newTaken = 0;
        foreach (var (dive, vendor) in fetched)
        {
            if (existingIds.Contains(DeterministicDiveId(vendor, dive.StartUtc, dive.DurationSec)))
            {
                toImport.Add((dive, vendor)); // update of an already-stored dive — always allowed
                continue;
            }

            newTotal++;
            if (newTaken < allowance)
            {
                newTaken++;
                toImport.Add((dive, vendor));
            }
        }

        var imported = new List<DiveDto>(toImport.Count);
        foreach (var (dive, vendor) in toImport)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dto = ToDiveDto(dive, vendor, matchedSiteId);
            await store.UpsertDiveAsync(dto, cancellationToken).ConfigureAwait(false);
            analytics.Track(FunnelEvents.DiveLoggedImport, new Dictionary<string, string>
            {
                [FunnelEvents.Props.DiveId] = dto.Id.ToString("N"),
                [FunnelEvents.Props.Vendor] = vendor,
                [FunnelEvents.Props.Source] = FunnelEvents.Values.SourceImport,
            });
            imported.Add(dto);
            progress?.Report(new ImportProgress("Importing", imported.Count, toImport.Count));
        }

        var capReached = newTotal > allowance;
        if (capReached && onCapReached is not null)
        {
            await onCapReached().ConfigureAwait(false);
        }

        return new ImportResult(imported, fetched.Count, capReached, matchedSiteId is not null);
    }

    /// <summary>
    /// Site auto-match against the bundled on-device atlas (the server geo-search's serverless
    /// replacement): current fix → bounding-box prefilter → haversine → nearest site within
    /// <see cref="SiteAutoMatchRadiusKm"/>. Null when permission is denied, no fix, or nothing near.
    /// </summary>
    private async Task<Guid?> TryAutoMatchSiteAsync(CancellationToken cancellationToken)
    {
        var fix = await location.GetCurrentLocationAsync(cancellationToken).ConfigureAwait(false);
        if (fix is null)
        {
            return null;
        }

        var site = atlas.FindNearest(fix.Lat, fix.Lng, SiteAutoMatchRadiusKm);
        if (site is null)
        {
            logger.LogDebug("Site auto-match: no atlas site within {RadiusKm} km of the fix.", SiteAutoMatchRadiusKm);
            return null;
        }

        logger.LogDebug("Site auto-match: {Site} ({Location}).", site.Name, site.Location);
        return site.Id;
    }

    private DiveDto ToDiveDto(ImportedDive dive, string vendor, Guid? siteId) => new(
        Id: DeterministicDiveId(vendor, dive.StartUtc, dive.DurationSec),
        StartUtc: dive.StartUtc,
        DurationSec: dive.DurationSec,
        MaxDepthM: dive.MaxDepthM,
        AvgDepthM: dive.AvgDepthM,
        MinTempC: dive.MinTempC,
        GasMix: dive.GasMix,
        Source: DiveSources.Import,
        SourceVendor: vendor,
        SiteId: siteId,
        ConditionsJson: null,
        GearCsv: null,
        BuddiesCsv: null,
        Notes: null,
        Rating: null,
        UpdatedAtUtc: clock.UtcNow.UtcDateTime,
        Deleted: false,
        ProfileSamples: dive.Samples,
        // Carry through any cylinders the source reported; typed-condition population is left to the
        // stats branch.
        Cylinders: dive.Cylinders);

    /// <summary>
    /// Deterministic dive identity: a stable GUID derived from (vendor, dive start, duration). Re-importing
    /// the same dives (e.g. after unlocking Pro at the free cap, PRD Flow B step 5) upserts onto the same
    /// ids instead of creating duplicate rows and duplicate <c>dive_logged_import</c> events.
    /// </summary>
    internal static Guid DeterministicDiveId(string vendor, DateTime startUtc, int durationSec)
    {
        var startUnixSec = new DateTimeOffset(DateTime.SpecifyKind(startUtc, DateTimeKind.Utc)).ToUnixTimeSeconds();
        var key = $"{vendor}|{startUnixSec.ToString(CultureInfo.InvariantCulture)}|{durationSec.ToString(CultureInfo.InvariantCulture)}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return new Guid(hash.AsSpan(0, 16));
    }
}
