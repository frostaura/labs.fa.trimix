using Trimix.Contracts;
using Trimix.Contracts.Dtos;
using Trimix.Core.Abstractions;
using Trimix.Core.DiveComputer;
using Trimix.Core.Storage;
using Trimix.Core.Tests.Fakes;
using Xunit;

namespace Trimix.Core.Tests;

/// <summary>
/// The generic, source-agnostic import path (architecture overview §2): any
/// <see cref="IDiveSourceConnector"/> — not just BLE — flows through the same downstream
/// (free-tier truncation → save → <c>dive_logged_import</c>).
/// </summary>
public class ImportFromConnectorTests
{
    private readonly InMemoryLocalStore _store = new();
    private readonly FakeEntitlementService _entitlements = new();
    private readonly RecordingAnalytics _analytics = new();

    private ImportService CreateService(FakeSourceConnector connector)
    {
        _ = connector; // the pipeline takes the connector per-call now, not via DI
        return new(
            _entitlements,
            _store,
            _analytics,
            new FakeAppInfo(),
            new FakeClock());
    }

    [Fact]
    public async Task Generic_connector_persists_all_dives_and_emits_dive_logged_import()
    {
        _entitlements.CurrentTier = EntitlementTier.Pro; // above the free cap → no truncation
        var connector = new FakeSourceConnector();
        var service = CreateService(connector);

        var result = await service.ImportFromConnectorAsync(connector, SyncCursor.None);

        Assert.Equal(2, result.ImportedDives.Count);
        Assert.Equal(2, result.FetchedCount);
        Assert.False(result.CapReached);
        Assert.Equal(2, (await _store.GetDivesAsync()).Count);

        var logged = _analytics.Events.Where(e => e.Name == FunnelEvents.DiveLoggedImport).ToList();
        Assert.Equal(2, logged.Count);
        Assert.All(logged, e =>
        {
            // Every clean import channel is source=import; provenance rides on the free-text vendor prop.
            Assert.Equal(FunnelEvents.Values.SourceImport, e.Props[FunnelEvents.Props.Source]);
            Assert.Equal(connector.Descriptor.ConnectorId, e.Props[FunnelEvents.Props.Vendor]);
        });
    }

    [Fact]
    public async Task Generic_connector_emits_source_connected_and_dives_synced()
    {
        // The cloud-aggregator legs (PRD Amendments set 2, item 10): source_connected/dives_synced are the
        // multi-source analogues of ble_device_connected/dives_fetched. 'source' is the connector id
        // (free-text channel), platform comes from IAppInfo, count is the fetched-ref count.
        _entitlements.CurrentTier = EntitlementTier.Pro;
        var connector = new FakeSourceConnector();
        var service = CreateService(connector);

        await service.ImportFromConnectorAsync(connector, SyncCursor.None);

        var connected = Assert.Single(_analytics.Events, e => e.Name == FunnelEvents.SourceConnected);
        Assert.Equal(connector.Descriptor.ConnectorId, connected.Props[FunnelEvents.Props.Source]);
        Assert.Equal("android", connected.Props[FunnelEvents.Props.Platform]); // FakeAppInfo default

        var synced = Assert.Single(_analytics.Events, e => e.Name == FunnelEvents.DivesSynced);
        Assert.Equal(connector.Descriptor.ConnectorId, synced.Props[FunnelEvents.Props.Source]);
        Assert.Equal("2", synced.Props[FunnelEvents.Props.Count]);

        // Order: source_connected → dives_synced → dive_logged_import.
        var names = _analytics.Events.Select(e => e.Name).ToList();
        Assert.True(
            names.IndexOf(FunnelEvents.SourceConnected) < names.IndexOf(FunnelEvents.DivesSynced),
            "source_connected must precede dives_synced");
        Assert.True(
            names.IndexOf(FunnelEvents.DivesSynced) < names.IndexOf(FunnelEvents.DiveLoggedImport),
            "dives_synced must precede dive_logged_import");
    }

    [Fact]
    public async Task Denied_auth_short_circuits_emits_neither_source_connected_nor_dives_synced()
    {
        var connector = new FakeSourceConnector { AuthStatus = ConnectorAuthStatus.Denied };
        var service = CreateService(connector);

        await service.ImportFromConnectorAsync(connector, SyncCursor.None);

        Assert.DoesNotContain(_analytics.Events, e => e.Name == FunnelEvents.SourceConnected);
        Assert.DoesNotContain(_analytics.Events, e => e.Name == FunnelEvents.DivesSynced);
    }

    [Fact]
    public async Task Denied_auth_short_circuits_with_no_persisted_dives()
    {
        var connector = new FakeSourceConnector { AuthStatus = ConnectorAuthStatus.Denied };
        var service = CreateService(connector);

        var result = await service.ImportFromConnectorAsync(connector, SyncCursor.None);

        Assert.Empty(result.ImportedDives);
        Assert.Equal(0, result.FetchedCount);
        Assert.False(result.CapReached);
        Assert.Empty(await _store.GetDivesAsync());
        Assert.DoesNotContain(_analytics.Events, e => e.Name == FunnelEvents.DiveLoggedImport);
    }

    [Fact]
    public async Task NeedsInteraction_auth_performs_zero_network_and_emits_no_source_connected()
    {
        // Guardrail (iii): a gated connector that has NOT been consented/signed-in returns NeedsInteraction.
        // The spine must short-circuit BEFORE any ListDivesSince/FetchProfile (live vendor I/O) and must NOT
        // emit source_connected for a source that is not actually connected.
        var connector = new FakeSourceConnector { AuthStatus = ConnectorAuthStatus.NeedsInteraction };
        var service = CreateService(connector);

        var result = await service.ImportFromConnectorAsync(connector, SyncCursor.None);

        Assert.Empty(result.ImportedDives);
        Assert.Equal(0, result.FetchedCount);
        Assert.False(result.CapReached);
        Assert.Empty(await _store.GetDivesAsync());
        Assert.Equal(0, connector.ListDivesCalls); // zero network I/O
        Assert.Equal(0, connector.FetchProfileCalls);
        Assert.DoesNotContain(_analytics.Events, e => e.Name == FunnelEvents.SourceConnected);
        Assert.DoesNotContain(_analytics.Events, e => e.Name == FunnelEvents.DivesSynced);
        Assert.DoesNotContain(_analytics.Events, e => e.Name == FunnelEvents.DiveLoggedImport);
    }

    [Fact]
    public async Task Cloud_sync_cap_emits_paywall_shown_with_frozen_cloud_sync_cap_trigger()
    {
        // The aggregator purchase moment: on the free tier the import-session cap truncates the sync, and the
        // service itself emits paywall_shown{trigger=cloud_sync_cap} — the only production emitter of the
        // frozen trigger, so the reconciliation is proven by real code, not a hand-built event.
        _entitlements.CurrentTier = EntitlementTier.Free;
        var dives = ManyDives(FreeTier.FreeImportDivesPerSession + 2); // exceed the free session cap
        var connector = new FakeSourceConnector("apple_health", dives);
        var service = CreateService(connector);

        var result = await service.ImportFromConnectorAsync(connector, SyncCursor.None);

        Assert.True(result.CapReached);
        Assert.Equal(FreeTier.FreeImportDivesPerSession, result.ImportedDives.Count);

        var paywall = Assert.Single(_analytics.Events, e => e.Name == FunnelEvents.PaywallShown);
        Assert.Equal(FunnelEvents.Values.TriggerCloudSyncCap, paywall.Props[FunnelEvents.Props.Trigger]);
        Assert.Equal(FunnelEvents.Values.EntitlementStateFree, paywall.Props[FunnelEvents.Props.EntitlementState]);
    }

    [Fact]
    public async Task Free_ceiling_counts_dives_already_stored_so_the_sixth_dive_caps_across_sessions()
    {
        // "Free keeps your last 5" is a TOTAL ceiling, not per-batch: a first sync that stored dives
        // consumes the allowance, and the dive that would exceed the ceiling in a LATER sync is the
        // paywall moment too.
        _entitlements.CurrentTier = EntitlementTier.Free;
        var first = new FakeSourceConnector("garmin_connect", ManyDives(FreeTier.FreeImportDivesPerSession - 1));
        await CreateService(first).ImportFromConnectorAsync(first, SyncCursor.None);
        Assert.Equal(FreeTier.FreeImportDivesPerSession - 1, (await _store.GetDivesAsync()).Count);

        // Second session: two NEW dives (different start times) — only one slot remains.
        var later = Enumerable.Range(100, 2).Select(i => new ImportedDive(
            new DateTime(2026, 7, 1, 8, 0, 0, DateTimeKind.Utc).AddDays(i), 2400, 22.0, 12.0, 24.0, "AIR",
            [new ProfileSampleDto(0, 0, null, null), new ProfileSampleDto(60, 22.0, 24.0, null)])).ToList();
        var second = new FakeSourceConnector("garmin_connect", later);
        var result = await CreateService(second).ImportFromConnectorAsync(second, SyncCursor.None);

        Assert.True(result.CapReached);
        Assert.Single(result.ImportedDives);
        Assert.Equal(FreeTier.FreeImportDivesPerSession, (await _store.GetDivesAsync()).Count);
        var paywall = Assert.Single(_analytics.Events, e => e.Name == FunnelEvents.PaywallShown);
        Assert.Equal(FunnelEvents.Values.TriggerCloudSyncCap, paywall.Props[FunnelEvents.Props.Trigger]);
    }

    [Fact]
    public async Task Reimporting_already_stored_dives_never_retriggers_the_cap()
    {
        // Deterministic ids make a re-sync of the same dives an upsert, not new imports — the free
        // ceiling must not fire on it (PRD Flow B step 5: unlock-at-cap re-imports onto the same ids).
        _entitlements.CurrentTier = EntitlementTier.Free;
        var dives = ManyDives(FreeTier.FreeImportDivesPerSession);
        var connector = new FakeSourceConnector("garmin_connect", dives);
        await CreateService(connector).ImportFromConnectorAsync(connector, SyncCursor.None);

        var again = new FakeSourceConnector("garmin_connect", dives);
        var result = await CreateService(again).ImportFromConnectorAsync(again, SyncCursor.None);

        Assert.False(result.CapReached);
        Assert.Equal(FreeTier.FreeImportDivesPerSession, result.ImportedDives.Count); // upserts, same ids
        Assert.Equal(FreeTier.FreeImportDivesPerSession, (await _store.GetDivesAsync()).Count);
        Assert.DoesNotContain(_analytics.Events, e => e.Name == FunnelEvents.PaywallShown);
    }

    private static IReadOnlyList<ImportedDive> ManyDives(int count) =>
        [.. Enumerable.Range(0, count).Select(i => new ImportedDive(
            new DateTime(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc).AddDays(i), 2400, 28.5, 16.0, 21.0, "EAN32",
            [new ProfileSampleDto(0, 0, null, null), new ProfileSampleDto(60, 28.5, 21.0, null)]))];
}
