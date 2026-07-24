using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Trimix.Contracts;
using Trimix.Contracts.Dtos;
using Trimix.Core.Abstractions;
using Trimix.Core.Entitlements;

namespace Trimix.Core.DiveComputer;

/// <summary>
/// Orchestrates one BLE import session (architecture overview §4/§9, PRD Flow B):
/// connect → fetch via the vendor protocol → site auto-match (nearest site within
/// <see cref="SiteAutoMatchRadiusKm"/> of the device via <c>GET /sites</c> geo search) → save
/// locally, truncating at the EntitlementGate free-session cap and invoking the paywall trigger
/// callback when truncation happened. The BLE path emits <c>ble_device_connected</c>, <c>dives_fetched</c>;
/// the generic multi-source path (<see cref="ImportFromConnectorAsync"/>) emits <c>source_connected</c>,
/// <c>dives_synced</c>; both emit <c>dive_logged_import</c> per saved dive. The generic cap emits
/// <c>paywall_shown</c> with <c>trigger=cloud_sync_cap</c> from this service (so the aggregator purchase
/// moment is instrumented regardless of which sync UI drives it — <c>onCapReached</c> is only the UI hook);
/// the BLE-only path keeps <c>trigger=ble_import_cap</c> (emitted by <c>ImportViewModel</c>).
/// </summary>
public sealed class ImportService(
    IEnumerable<IDiveSourceConnector> sourceConnectors,
    IApiClient api,
    ILocationService location,
    IEntitlementService entitlements,
    ILocalStore store,
    IAnalytics analytics,
    IAppInfo appInfo,
    IClock clock,
    ILogger<ImportService> logger) : IImportService
{
    /// <summary>Auto-match radius: nearest known site within 2 km of the device claims the dive.</summary>
    public const double SiteAutoMatchRadiusKm = 2.0;

    /// <summary>The BLE connector owns the interactive scan/select affordances the BLE UI path needs.</summary>
    private BleDeviceSourceConnector Ble =>
        sourceConnectors.OfType<BleDeviceSourceConnector>().FirstOrDefault()
        ?? throw new InvalidOperationException("No BLE source connector registered.");

    public IAsyncEnumerable<DiscoveredComputer> ScanAsync(CancellationToken cancellationToken = default) =>
        Ble.ScanAsync(cancellationToken);

    public async Task<ImportResult> ImportAsync(
        DiscoveredComputer computer,
        Func<Task>? onCapReached = null,
        IProgress<ImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(computer);
        var vendor = VendorName(computer.Vendor);
        Ble.SelectDevice(computer);

        progress?.Report(new ImportProgress("Connecting", 0, 0));
        await Ble.AuthenticateAsync(cancellationToken).ConfigureAwait(false);
        analytics.Track(FunnelEvents.BleDeviceConnected, new Dictionary<string, string>
        {
            [FunnelEvents.Props.Vendor] = vendor,
            [FunnelEvents.Props.Model] = computer.Model,
        });

        progress?.Report(new ImportProgress("Reading dives", 0, 0));
        var refs = await Ble.ListDivesSinceAsync(SyncCursor.None, cancellationToken).ConfigureAwait(false);
        analytics.Track(FunnelEvents.DivesFetched, new Dictionary<string, string>
        {
            [FunnelEvents.Props.Vendor] = vendor,
            [FunnelEvents.Props.Count] = refs.Count.ToString(CultureInfo.InvariantCulture),
        });

        var fetched = new List<(ImportedDive Dive, string Vendor)>(refs.Count);
        foreach (var diveRef in refs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            fetched.Add((await Ble.FetchProfileAsync(diveRef, cancellationToken).ConfigureAwait(false), vendor));
        }

        return await PersistFetchedAsync(fetched, onCapReached, progress, cancellationToken).ConfigureAwait(false);
    }

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
        // designed pre-consent / pre-sign-in state for every gated cloud + HealthKit connector (it carries
        // the disclosure / sign-in prompt for the UI). It, like Denied/Unavailable, must short-circuit to a
        // clean empty result BEFORE any ListDivesSince/FetchProfile network I/O — and before source_connected
        // is emitted, so a not-yet-connected source never appears connected in the frozen aggregator funnel.
        if (auth.Status != ConnectorAuthStatus.Granted)
        {
            return new ImportResult([], 0, false, false);
        }

        // The generic cloud-aggregator spine (PRD Amendments set 2, item 10): the connector id is the
        // free-text 'source' channel (e.g. apple_health, garmin_connect); it is NOT the frozen
        // dive_logged_import 'import' value. The BLE-only path keeps ble_device_connected/dives_fetched.
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
            // File-import carries the real per-file format id (garmin_fit / uddf / dan_dl7 / suunto_json) on
            // DiveRef.Label; use it as the provenance vendor so merge badges show the true vendor and the
            // deterministic id keeps per-format identity (two formats of one dive stay distinct rows). Other
            // connectors' Label is a human dive name, so only the file-import kind overrides the source id.
            var vendor = connector.Descriptor.Kind == ConnectorKind.FileImport && !string.IsNullOrEmpty(diveRef.Label)
                ? diveRef.Label!
                : source;
            fetched.Add((dive, vendor));
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

        var allowance = EntitlementGate.ImportSessionAllowance(entitlements.CurrentTier);
        IReadOnlyList<(ImportedDive Dive, string Vendor)> toImport =
            fetched.Count <= allowance ? fetched : fetched.Take(allowance).ToList();
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

        var capReached = fetched.Count > allowance;
        if (capReached && onCapReached is not null)
        {
            await onCapReached().ConfigureAwait(false);
        }

        return new ImportResult(imported, fetched.Count, capReached, matchedSiteId is not null);
    }

    /// <summary>Nearest site within the radius, via the API's haversine-ranked geo search; null when offline/no fix/no match.</summary>
    private async Task<Guid?> TryAutoMatchSiteAsync(CancellationToken cancellationToken)
    {
        var fix = await location.GetCurrentLocationAsync(cancellationToken).ConfigureAwait(false);
        if (fix is null)
        {
            return null;
        }

        try
        {
            var response = await api.SearchSitesAsync(null, fix.Lat, fix.Lng, 5, cancellationToken).ConfigureAwait(false);
            return response.Sites
                .Where(s => s.DistanceKm is not null && s.DistanceKm <= SiteAutoMatchRadiusKm)
                .OrderBy(s => s.DistanceKm)
                .Select(s => (Guid?)s.Id)
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Site auto-match search failed; importing without a site.");
            return null;
        }
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
        // Carry through any cylinders the source reported (null/empty for today's protocols); typed-condition
        // population is left to the stats branch.
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

    /// <summary>Frozen-contract <c>vendor</c> prop value (lowercase vendor name).</summary>
    public static string VendorName(DiveComputerVendor vendor) => vendor switch
    {
        DiveComputerVendor.Shearwater => "shearwater",
        DiveComputerVendor.Suunto => "suunto",
        DiveComputerVendor.Garmin => "garmin",
        DiveComputerVendor.Mares => "mares",
        DiveComputerVendor.ScubaPro => "scubapro",
        DiveComputerVendor.Aqualung => "aqualung",
        _ => vendor.ToString().ToLowerInvariant(),
    };
}
