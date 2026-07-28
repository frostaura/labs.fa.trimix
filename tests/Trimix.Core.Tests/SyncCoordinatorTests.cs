using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using Trimix.Contracts;
using Trimix.Contracts.Dtos;
using Trimix.Core.Abstractions;
using Trimix.Core.Connectors.Infra;
using Trimix.Core.DiveComputer;
using Trimix.Core.Storage;
using Trimix.Core.Tests.Connectors.Cloud;
using Trimix.Core.Tests.Fakes;
using Xunit;

namespace Trimix.Core.Tests;

/// <summary>
/// The generic "Sync now" coordinator — the production caller <c>ImportService</c> was missing: consented
/// sources → import pipeline (incremental via a persisted per-connector cursor) → dedup by deterministic
/// dive id → consented destinations' <c>PushDiveAsync</c>. Unconsented connectors are skipped with zero I/O.
/// </summary>
public class SyncCoordinatorTests
{
    private readonly InMemoryLocalStore _store = new();
    private readonly FakeEntitlementService _entitlements = new() { CurrentTier = EntitlementTier.Pro };
    private readonly RecordingAnalytics _analytics = new();
    private readonly InMemoryConnectorConsentStore _consent = new();
    private readonly InMemorySecureTokenStore _tokens = new();

    private static readonly DateTime DiveOneStart = new(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime DiveTwoStart = new(2026, 6, 2, 9, 30, 0, DateTimeKind.Utc);

    private SyncCoordinator Create(
        IEnumerable<IDiveSourceConnector> sources,
        IEnumerable<IDiveDestinationConnector> destinations)
    {
        var importService = new ImportService(
            new FakeLocation(),
            _entitlements,
            _store,
            _analytics,
            new FakeAppInfo(),
            new FakeClock(),
            new Trimix.Core.Sites.SiteAtlas(),
            NullLogger<ImportService>.Instance);
        return new SyncCoordinator(
            sources, destinations, importService, _consent, _tokens, NullLogger<SyncCoordinator>.Instance);
    }

    private static ImportedDive Dive(DateTime startUtc, int durationSec, double maxDepthM) => new(
        startUtc, durationSec, maxDepthM, maxDepthM / 2, 21.0, "EAN32",
        [new ProfileSampleDto(0, 0, null, null), new ProfileSampleDto(60, maxDepthM, 21.0, null)]);

    [Fact]
    public async Task Sync_imports_from_consented_sources_and_pushes_new_dives_to_the_consented_destination()
    {
        var sourceA = new CursorAwareSource("source_a", [Dive(DiveOneStart, 2400, 28.5)]);
        var sourceB = new CursorAwareSource("source_b", [Dive(DiveTwoStart, 1800, 18.0)]);
        var destination = new FakeDestinationConnector("dest_x");
        await _consent.GrantAsync("source_a");
        await _consent.GrantAsync("source_b");
        await _consent.GrantAsync("dest_x");
        var coordinator = Create([sourceA, sourceB], [destination]);

        var report = await coordinator.SyncNowAsync();

        Assert.Equal(2, report.NewDives.Count);
        Assert.False(report.CapReached);
        Assert.Equal(2, report.Sources.Count);
        Assert.All(report.Sources, s => Assert.Equal(ConnectorAuthStatus.Granted, s.Auth));
        Assert.Equal(2, (await _store.GetDivesAsync()).Count);

        // Push leg: both newly-imported dives reached the destination, oldest first.
        var outcome = Assert.Single(report.Destinations);
        Assert.Equal("dest_x", outcome.ConnectorId);
        Assert.Equal(2, outcome.Pushed);
        Assert.Equal([DiveOneStart, DiveTwoStart], destination.PushedDives.Select(d => d.StartUtc).ToArray());
    }

    [Fact]
    public async Task Unconsented_connectors_are_skipped_with_zero_io()
    {
        var linked = new CursorAwareSource("source_a", [Dive(DiveOneStart, 2400, 28.5)]);
        var unlinked = new CursorAwareSource("source_dark", [Dive(DiveTwoStart, 1800, 18.0)]);
        var unlinkedDest = new FakeDestinationConnector("dest_dark");
        await _consent.GrantAsync("source_a");
        var coordinator = Create([linked, unlinked], [unlinkedDest]);

        var report = await coordinator.SyncNowAsync();

        Assert.Single(report.NewDives);
        Assert.Single(report.Sources); // the unconsented source does not even appear in the report
        Assert.Equal(0, unlinked.ListCalls);
        Assert.Equal(0, unlinked.FetchCalls);
        Assert.Empty(report.Destinations);
        Assert.Empty(unlinkedDest.PushedDives);
    }

    [Fact]
    public async Task Cursor_advances_and_persists_so_the_second_sync_imports_nothing_new()
    {
        var source = new CursorAwareSource("source_a", [Dive(DiveOneStart, 2400, 28.5), Dive(DiveTwoStart, 1800, 18.0)]);
        var destination = new FakeDestinationConnector("dest_x");
        await _consent.GrantAsync("source_a");
        await _consent.GrantAsync("dest_x");
        var coordinator = Create([source], [destination]);

        var first = await coordinator.SyncNowAsync();
        Assert.Equal(2, first.NewDives.Count);
        Assert.Null(source.LastCursor!.Since); // first run: full sync from SyncCursor.None

        // The cursor persisted to the newest imported dive's start instant, under the connector's key.
        var stored = _tokens.Values[ConnectorTokenKeys.SyncCursor("source_a")];
        Assert.Equal(DiveTwoStart, DateTime.Parse(stored, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal));

        var second = await coordinator.SyncNowAsync();

        Assert.Equal(DiveTwoStart, source.LastCursor!.Since); // second run resumed from the cursor
        Assert.Empty(second.NewDives); // nothing new ⇒ nothing re-imported…
        Assert.Equal(2, destination.PushedDives.Count); // …and nothing re-pushed (still just the first run's two)
        Assert.Equal(2, (await _store.GetDivesAsync()).Count);
    }

    [Fact]
    public async Task Duplicate_dives_from_a_source_are_deduplicated_by_deterministic_id_before_pushing()
    {
        // The same dive listed twice (vendor glitch): the deterministic id makes both upsert onto one row,
        // and the coordinator must push it once, not twice.
        var dive = Dive(DiveOneStart, 2400, 28.5);
        var source = new CursorAwareSource("source_a", [dive, dive]);
        var destination = new FakeDestinationConnector("dest_x");
        await _consent.GrantAsync("source_a");
        await _consent.GrantAsync("dest_x");
        var coordinator = Create([source], [destination]);

        var report = await coordinator.SyncNowAsync();

        Assert.Single(report.NewDives);
        Assert.Single(await _store.GetDivesAsync());
        Assert.Single(destination.PushedDives);
    }

    [Fact]
    public async Task A_source_that_is_not_signed_in_is_reported_not_imported()
    {
        var source = new CursorAwareSource("source_a", [Dive(DiveOneStart, 2400, 28.5)])
        {
            AuthStatus = ConnectorAuthStatus.NeedsInteraction,
        };
        await _consent.GrantAsync("source_a");
        var coordinator = Create([source], []);

        var report = await coordinator.SyncNowAsync();

        var outcome = Assert.Single(report.Sources);
        Assert.Equal(ConnectorAuthStatus.NeedsInteraction, outcome.Auth); // the relink surface for the UI
        Assert.Equal(0, outcome.Imported);
        Assert.Equal(0, source.ListCalls); // Granted-only: no dive I/O without a live sign-in
        Assert.Empty(report.NewDives);
    }

    [Fact]
    public async Task A_throwing_source_never_aborts_the_run_and_later_sources_still_sync()
    {
        var broken = new CursorAwareSource("source_broken", [Dive(DiveOneStart, 2400, 28.5)]) { ThrowOnList = true };
        var healthy = new CursorAwareSource("source_b", [Dive(DiveTwoStart, 1800, 18.0)]);
        await _consent.GrantAsync("source_broken");
        await _consent.GrantAsync("source_b");
        var coordinator = Create([broken, healthy], []);

        var report = await coordinator.SyncNowAsync();

        Assert.Equal(2, report.Sources.Count);
        Assert.NotNull(report.Sources[0].Error);
        Assert.Null(report.Sources[1].Error);
        Assert.Single(report.NewDives); // the healthy source's dive landed
        Assert.False(_tokens.Values.ContainsKey(ConnectorTokenKeys.SyncCursor("source_broken"))); // no false advance
    }

    [Fact]
    public async Task Cap_reached_leaves_the_cursor_unadvanced_so_upgrade_can_resync_the_truncated_dives()
    {
        _entitlements.CurrentTier = EntitlementTier.Free; // free-tier session allowance truncates the fetch
        var dives = Enumerable.Range(0, 10)
            .Select(i => Dive(DiveOneStart.AddDays(i), 2400, 20 + i))
            .ToArray();
        var source = new CursorAwareSource("source_a", dives);
        await _consent.GrantAsync("source_a");
        var coordinator = Create([source], []);

        var report = await coordinator.SyncNowAsync();

        Assert.True(report.CapReached);
        var outcome = Assert.Single(report.Sources);
        Assert.True(outcome.CapReached);
        Assert.True(outcome.Imported < outcome.Fetched);
        // Cursor must NOT advance past a truncated run — post-upgrade re-sync must see the cut dives again.
        Assert.False(_tokens.Values.ContainsKey(ConnectorTokenKeys.SyncCursor("source_a")));
    }

    [Fact]
    public async Task HasLinkedSources_tracks_consent()
    {
        var source = new CursorAwareSource("source_a", [Dive(DiveOneStart, 2400, 28.5)]);
        var coordinator = Create([source], []);

        Assert.False(coordinator.HasLinkedSources); // radar button disabled until a provider is linked
        await _consent.GrantAsync("source_a");
        Assert.True(coordinator.HasLinkedSources);
    }

    [Fact]
    public async Task Progress_streams_importing_then_pushing_then_done()
    {
        var source = new CursorAwareSource("source_a", [Dive(DiveOneStart, 2400, 28.5)]);
        var destination = new FakeDestinationConnector("dest_x");
        await _consent.GrantAsync("source_a");
        await _consent.GrantAsync("dest_x");
        var coordinator = Create([source], [destination]);
        var ticks = new List<SyncProgress>();
        var progress = new SynchronousProgress(ticks.Add);

        await coordinator.SyncNowAsync(progress: progress);

        Assert.Contains(ticks, t => t.Stage == SyncStage.Importing && t.ConnectorId == "source_a");
        Assert.Contains(ticks, t => t.Stage == SyncStage.Pushing && t.ConnectorId == "dest_x");
        var done = ticks.Last();
        Assert.Equal(SyncStage.Done, done.Stage);
        Assert.Equal(1, done.ImportedSoFar);
        Assert.Equal(1, done.PushedSoFar);
    }

    /// <summary>Inline IProgress that reports synchronously (no SynchronizationContext trampoline in tests).</summary>
    private sealed class SynchronousProgress(Action<SyncProgress> handler) : IProgress<SyncProgress>
    {
        public void Report(SyncProgress value) => handler(value);
    }

    /// <summary>
    /// Cursor-honouring fake source: lists only dives strictly newer than the cursor (so a persisted cursor
    /// provably prevents re-import) and records the last cursor it was handed.
    /// </summary>
    private sealed class CursorAwareSource(string connectorId, IReadOnlyList<ImportedDive> dives) : IDiveSourceConnector
    {
        public ConnectorDescriptor Descriptor { get; } = new(
            connectorId, ConnectorKind.CloudApi, LegalPosture.ToSRiskReverseEng, ProfileFidelity.FullProfile,
            AuthModel.UserCredential, Platforms.Both, EnabledByDefault: false);

        public ConnectorAuthStatus AuthStatus { get; set; } = ConnectorAuthStatus.Granted;

        public bool ThrowOnList { get; set; }

        public SyncCursor? LastCursor { get; private set; }

        public int ListCalls { get; private set; }

        public int FetchCalls { get; private set; }

        public Task<ConnectorAuthResult> AuthenticateAsync(CancellationToken ct = default) =>
            Task.FromResult(new ConnectorAuthResult(AuthStatus));

        public Task<IReadOnlyList<DiveRef>> ListDivesSinceAsync(SyncCursor cursor, CancellationToken ct = default)
        {
            ListCalls++;
            LastCursor = cursor;
            if (ThrowOnList)
            {
                throw new InvalidOperationException("vendor endpoint moved");
            }

            IReadOnlyList<DiveRef> refs =
            [
                .. dives
                    .Select((d, i) => (Dive: d, Index: i))
                    .Where(x => cursor.Since is not { } since || x.Dive.StartUtc > since)
                    .Select(x => new DiveRef(
                        Descriptor.ConnectorId,
                        x.Index.ToString(CultureInfo.InvariantCulture),
                        x.Dive.StartUtc,
                        x.Dive.DurationSec)),
            ];
            return Task.FromResult(refs);
        }

        public Task<ImportedDive> FetchProfileAsync(DiveRef diveRef, CancellationToken ct = default)
        {
            FetchCalls++;
            return Task.FromResult(dives[int.Parse(diveRef.SourceId, CultureInfo.InvariantCulture)]);
        }
    }

    /// <summary>Recording fake destination: accepts every push and remembers the dives it received.</summary>
    private sealed class FakeDestinationConnector(string connectorId) : IDiveDestinationConnector
    {
        public ConnectorDescriptor Descriptor { get; } = new(
            connectorId, ConnectorKind.CloudApi, LegalPosture.ToSRiskReverseEng, ProfileFidelity.SummaryOnly,
            AuthModel.UserCredential, Platforms.Both, EnabledByDefault: false);

        public List<DiveDto> PushedDives { get; } = [];

        public Task<ConnectorAuthResult> AuthenticateAsync(CancellationToken ct = default) =>
            Task.FromResult(new ConnectorAuthResult(ConnectorAuthStatus.Granted));

        public Task<PushResult> PushDiveAsync(DiveDto dive, CancellationToken ct = default)
        {
            PushedDives.Add(dive);
            return Task.FromResult(new PushResult(PushStatus.Pushed, Descriptor.ConnectorId));
        }
    }
}
