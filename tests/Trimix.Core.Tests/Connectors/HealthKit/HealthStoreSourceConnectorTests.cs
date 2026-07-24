using Microsoft.Extensions.Logging.Abstractions;
using Trimix.Contracts;
using Trimix.Core.Abstractions;
using Trimix.Core.Connectors.HealthKit;
using Trimix.Core.DiveComputer;
using Trimix.Core.Storage;
using Trimix.Core.Tests.Fakes;
using Xunit;

namespace Trimix.Core.Tests.Connectors.HealthKit;

/// <summary>
/// Unit coverage for <see cref="HealthStoreSourceConnector"/>: descriptor policy, auth delegation, the
/// AWU2UDDF &gt;60 s surface-gap segmentation, depth+temp-only mapping (every rich channel null), the
/// nearest-temperature alignment, sync-cursor forwarding, and the full run through
/// <c>ImportService.ImportFromConnectorAsync</c>. All synthetic — a <see cref="FakeHealthStore"/> stands in
/// for HealthKit, so no iOS binding is touched.
/// </summary>
public class HealthStoreSourceConnectorTests
{
    private static readonly DateTime Base = new(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc);

    private static HealthSeriesPoint P(int seconds, double value) =>
        new(Base.AddSeconds(seconds), value);

    private static HealthSeriesPoint P(int seconds, double value, DateTime origin) =>
        new(origin.AddSeconds(seconds), value);

    [Fact]
    public void Descriptor_is_clean_official_depth_temp_only_ios()
    {
        var connector = new HealthStoreSourceConnector(new FakeHealthStore());

        Assert.Equal("apple_health", connector.Descriptor.ConnectorId);
        Assert.Equal(ConnectorKind.HealthStore, connector.Descriptor.Kind);
        Assert.Equal(LegalPosture.CleanOfficial, connector.Descriptor.Legal);
        Assert.Equal(ProfileFidelity.DepthTempOnly, connector.Descriptor.Fidelity);
        Assert.Equal(AuthModel.OnDeviceConsent, connector.Descriptor.Auth);
        Assert.Equal(Platforms.iOS, connector.Descriptor.Platforms);
        Assert.True(connector.Descriptor.EnabledByDefault);
    }

    [Fact]
    public async Task Authenticate_delegates_to_the_health_store()
    {
        var store = new FakeHealthStore { AuthStatus = ConnectorAuthStatus.Denied };
        var connector = new HealthStoreSourceConnector(store);

        var result = await connector.AuthenticateAsync();

        Assert.Equal(ConnectorAuthStatus.Denied, result.Status);
        Assert.Equal(1, store.AuthorizeCalls);
    }

    [Fact]
    public async Task Single_continuous_workout_maps_to_one_dive_with_depth_and_temp()
    {
        var store = new FakeHealthStore().AddWorkout(
            new HealthDiveWorkout("w1", Base, Base.AddSeconds(8)),
            depth: [P(0, 0), P(2, 5), P(4, 12), P(6, 6), P(8, 0)],
            temperature: [P(0, 20), P(4, 18), P(8, 22)]);
        var connector = new HealthStoreSourceConnector(store);

        var refs = await connector.ListDivesSinceAsync(SyncCursor.None);
        Assert.Single(refs);

        var dive = await connector.FetchProfileAsync(refs[0]);
        Assert.Equal(Base, dive.StartUtc);
        Assert.Equal(DateTimeKind.Utc, dive.StartUtc.Kind);
        Assert.Equal(8, dive.DurationSec);
        Assert.Equal(12, dive.MaxDepthM);
        Assert.Equal(4.6, dive.AvgDepthM!.Value, 5); // (0+5+12+6+0)/5
        Assert.Equal(18, dive.MinTempC!.Value);      // coldest reading in-window
        Assert.Null(dive.GasMix);                    // HealthKit carries no gas
        Assert.Null(dive.Cylinders);                 // no cylinder detail

        Assert.Equal([0, 2, 4, 6, 8], dive.Samples.Select(s => s.TSeconds));
        Assert.Equal(0, dive.Samples[0].DepthM);
        Assert.Equal(20, dive.Samples[0].TempC!.Value); // nearest reading (t=0)
        // Depth+temp only: no rich channels leak in.
        Assert.All(dive.Samples, s =>
        {
            Assert.Null(s.PpO2);
            Assert.Null(s.Ndl);
            Assert.Null(s.CeilingM);
            Assert.Null(s.AscentRateMPerMin);
            Assert.Null(s.Cns);
            Assert.Null(s.TankPressureBar);
            Assert.Null(s.Setpoint);
            Assert.Null(s.HeartRate);
        });
    }

    [Fact]
    public async Task Depth_series_with_a_surface_gap_segments_into_two_dives()
    {
        // One workout, two dives: readings up to +8 s, a 120 s surface interval, then a second dive.
        var store = new FakeHealthStore().AddWorkout(
            new HealthDiveWorkout("w1", Base, Base.AddSeconds(134)),
            depth:
            [
                P(0, 0), P(2, 6), P(8, 0),        // dive A: 0–8 s
                P(128, 0), P(130, 4), P(134, 0),  // dive B: 128–134 s (gap 120 s > 60 s)
            ]);
        var connector = new HealthStoreSourceConnector(store);

        var refs = await connector.ListDivesSinceAsync(SyncCursor.None);
        Assert.Equal(2, refs.Count);

        var a = await connector.FetchProfileAsync(refs[0]);
        var b = await connector.FetchProfileAsync(refs[1]);

        Assert.Equal(Base, a.StartUtc);
        Assert.Equal(8, a.DurationSec);
        Assert.Equal(3, a.Samples.Count);
        Assert.Equal(6, a.MaxDepthM);

        Assert.Equal(Base.AddSeconds(128), b.StartUtc);
        Assert.Equal(6, b.DurationSec);              // 134 − 128
        Assert.Equal(3, b.Samples.Count);
        Assert.Equal([0, 2, 6], b.Samples.Select(s => s.TSeconds)); // TSeconds re-based per segment
    }

    [Fact]
    public async Task A_gap_of_exactly_60s_does_not_split_the_dive()
    {
        // Boundary: only gaps strictly greater than 60 s split. A 60 s gap stays one dive.
        var store = new FakeHealthStore().AddWorkout(
            new HealthDiveWorkout("w1", Base, Base.AddSeconds(60)),
            depth: [P(0, 0), P(60, 4)]);
        var connector = new HealthStoreSourceConnector(store);

        var refs = await connector.ListDivesSinceAsync(SyncCursor.None);

        Assert.Single(refs);
        var dive = await connector.FetchProfileAsync(refs[0]);
        Assert.Equal(2, dive.Samples.Count);
        Assert.Equal(60, dive.DurationSec);
    }

    [Fact]
    public async Task Multiple_workouts_each_produce_a_dive()
    {
        var day2 = Base.AddDays(1);
        var store = new FakeHealthStore()
            .AddWorkout(new HealthDiveWorkout("w1", Base, Base.AddSeconds(6)), depth: [P(0, 0), P(6, 10)])
            .AddWorkout(new HealthDiveWorkout("w2", day2, day2.AddSeconds(6)), depth: [P(0, 0, day2), P(6, 8, day2)]);
        var connector = new HealthStoreSourceConnector(store);

        var refs = await connector.ListDivesSinceAsync(SyncCursor.None);

        Assert.Equal(2, refs.Count);
        Assert.Equal(Base, refs[0].StartUtc);
        Assert.Equal(day2, refs[1].StartUtc);
        Assert.Equal("apple_health", refs[0].ConnectorId);
    }

    [Fact]
    public async Task Sync_cursor_since_is_forwarded_to_the_store()
    {
        var day2 = Base.AddDays(1);
        var store = new FakeHealthStore()
            .AddWorkout(new HealthDiveWorkout("w1", Base, Base.AddSeconds(6)), depth: [P(0, 0), P(6, 10)])
            .AddWorkout(new HealthDiveWorkout("w2", day2, day2.AddSeconds(6)), depth: [P(0, 0, day2), P(6, 8, day2)]);
        var connector = new HealthStoreSourceConnector(store);

        var since = Base.AddHours(12);
        var refs = await connector.ListDivesSinceAsync(new SyncCursor(since));

        Assert.True(store.SinceRequested);
        Assert.Equal(since, store.LastSince);
        Assert.Single(refs);                 // only the day-2 workout is newer than the cursor
        Assert.Equal(day2, refs[0].StartUtc);
    }

    [Fact]
    public async Task No_temperature_series_leaves_min_and_per_sample_temp_null()
    {
        var store = new FakeHealthStore().AddWorkout(
            new HealthDiveWorkout("w1", Base, Base.AddSeconds(10)),
            depth: [P(0, 0), P(10, 12)]);
        var connector = new HealthStoreSourceConnector(store);

        var refs = await connector.ListDivesSinceAsync(SyncCursor.None);
        var dive = await connector.FetchProfileAsync(refs[0]);

        Assert.Null(dive.MinTempC);
        Assert.All(dive.Samples, s => Assert.Null(s.TempC));
    }

    [Fact]
    public async Task Temperature_reading_outside_the_tolerance_window_is_not_attached()
    {
        // Only reading is 30 s past the dive end — outside both the per-sample 15 s tolerance and the
        // segment window — so it neither colours a sample nor sets MinTempC.
        var store = new FakeHealthStore().AddWorkout(
            new HealthDiveWorkout("w1", Base, Base.AddSeconds(10)),
            depth: [P(0, 0), P(10, 12)],
            temperature: [P(40, 17)]);
        var connector = new HealthStoreSourceConnector(store);

        var refs = await connector.ListDivesSinceAsync(SyncCursor.None);
        var dive = await connector.FetchProfileAsync(refs[0]);

        Assert.Null(dive.MinTempC);
        Assert.All(dive.Samples, s => Assert.Null(s.TempC));
    }

    [Fact]
    public async Task Workout_with_no_depth_series_yields_no_dive()
    {
        var store = new FakeHealthStore()
            .AddWorkout(new HealthDiveWorkout("empty", Base, Base.AddSeconds(6)), depth: [])
            .AddWorkout(new HealthDiveWorkout("w2", Base.AddDays(1), Base.AddDays(1).AddSeconds(6)),
                depth: [P(0, 0, Base.AddDays(1)), P(6, 9, Base.AddDays(1))]);
        var connector = new HealthStoreSourceConnector(store);

        var refs = await connector.ListDivesSinceAsync(SyncCursor.None);

        Assert.Single(refs); // the empty workout is skipped, the real one imports
    }

    [Fact]
    public async Task Dive_refs_index_the_buffer_in_order()
    {
        var store = new FakeHealthStore().AddWorkout(
            new HealthDiveWorkout("w1", Base, Base.AddSeconds(134)),
            depth: [P(0, 0), P(8, 6), P(128, 0), P(134, 2)]);
        var connector = new HealthStoreSourceConnector(store);

        var refs = await connector.ListDivesSinceAsync(SyncCursor.None);

        Assert.Equal("0", refs[0].SourceId);
        Assert.Equal("1", refs[1].SourceId);
        var second = await connector.FetchProfileAsync(refs[1]);
        Assert.Equal(refs[1].StartUtc, second.StartUtc);
        Assert.Equal(refs[1].DurationSec, second.DurationSec);
    }

    [Fact]
    public async Task Flows_through_import_service_as_a_clean_import_channel()
    {
        // The whole point of the seam: a non-BLE connector runs the same downstream. source=import (frozen),
        // vendor=apple_health (free-text provenance), plus the source_connected/dives_synced cloud legs.
        var store = new FakeHealthStore()
            .AddWorkout(new HealthDiveWorkout("w1", Base, Base.AddSeconds(8)),
                depth: [P(0, 0), P(4, 12), P(8, 0)], temperature: [P(4, 19)]);
        var connector = new HealthStoreSourceConnector(store);

        var localStore = new InMemoryLocalStore();
        var analytics = new RecordingAnalytics();
        var service = new ImportService(
            [connector],
            new FakeApiClient(),
            new FakeLocation(),
            new FakeEntitlementService { CurrentTier = EntitlementTier.Pro },
            localStore,
            analytics,
            new FakeAppInfo { Platform = "ios" },
            new FakeClock(),
            NullLogger<ImportService>.Instance);

        var result = await service.ImportFromConnectorAsync(connector, SyncCursor.None);

        Assert.Equal(1, result.FetchedCount);
        Assert.Single(result.ImportedDives);
        Assert.False(result.CapReached);
        Assert.Single(await localStore.GetDivesAsync());

        var connected = Assert.Single(analytics.Events, e => e.Name == FunnelEvents.SourceConnected);
        Assert.Equal("apple_health", connected.Props[FunnelEvents.Props.Source]);
        Assert.Equal("ios", connected.Props[FunnelEvents.Props.Platform]);

        var synced = Assert.Single(analytics.Events, e => e.Name == FunnelEvents.DivesSynced);
        Assert.Equal("1", synced.Props[FunnelEvents.Props.Count]);

        var logged = Assert.Single(analytics.Events, e => e.Name == FunnelEvents.DiveLoggedImport);
        Assert.Equal(FunnelEvents.Values.SourceImport, logged.Props[FunnelEvents.Props.Source]);
        Assert.Equal("apple_health", logged.Props[FunnelEvents.Props.Vendor]);
    }
}
