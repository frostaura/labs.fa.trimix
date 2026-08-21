using Trimix.Contracts;
using Trimix.Contracts.Dtos;
using Trimix.Core.Storage;
using Xunit;

namespace Trimix.Core.Tests;

/// <summary>sqlite-net-pcl store roundtrip: dives (with profile samples), tombstones, FIFO analytics queue.</summary>
public class SqliteLocalStoreTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(AppContext.BaseDirectory, $"trimix-test-{Guid.NewGuid():N}.db3");
    private readonly SqliteLocalStore _store;

    public SqliteLocalStoreTests()
    {
        _store = new SqliteLocalStore(new SqliteLocalStoreOptions { DatabasePath = _dbPath });
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    private static DiveDto Dive() => new(
        Id: Guid.NewGuid(),
        StartUtc: new DateTime(2026, 6, 20, 8, 30, 0, DateTimeKind.Utc),
        DurationSec: 2700,
        MaxDepthM: 32.4,
        AvgDepthM: 19.7,
        MinTempC: 23.1,
        GasMix: "EAN32",
        Source: DiveSources.Import,
        SourceVendor: "shearwater",
        SiteId: Guid.NewGuid(),
        ConditionsJson: null,
        GearCsv: "BCD,reg",
        BuddiesCsv: "Sam",
        Notes: "Great vis",
        Rating: 5,
        UpdatedAtUtc: new DateTime(2026, 6, 20, 10, 0, 0, DateTimeKind.Utc),
        Deleted: false,
        ProfileSamples: [new ProfileSampleDto(0, 0, 26.0, null), new ProfileSampleDto(10, 5.5, 25.2, null)]);

    private static AnalyticsEventDto Event(string name) => new(
        name,
        new Dictionary<string, string> { [FunnelEvents.Props.SiteId] = "x" },
        "pseudo",
        DateTime.UtcNow,
        "android",
        "0.1.0");

    [Fact]
    public async Task Dive_roundtrips_with_profile_samples()
    {
        await _store.InitializeAsync();
        var dive = Dive();

        await _store.UpsertDiveAsync(dive);
        var loaded = Assert.Single(await _store.GetDivesAsync());

        Assert.Equal(dive.Id, loaded.Id);
        Assert.Equal(dive.MaxDepthM, loaded.MaxDepthM);
        Assert.Equal(dive.GasMix, loaded.GasMix);
        Assert.Equal(2, loaded.ProfileSamples.Count);
        Assert.Equal(5.5, loaded.ProfileSamples[1].DepthM);
    }

    [Fact]
    public async Task Rich_channels_cylinders_and_conditions_survive_the_json_roundtrip()
    {
        await _store.InitializeAsync();
        var dive = Dive() with
        {
            ProfileSamples =
            [
                new ProfileSampleDto(0, 0, 26.0, 0.21, Ndl: 99, CeilingM: 0, AscentRateMPerMin: 0, Cns: 1.0, TankPressureBar: 220, Setpoint: 0.7, HeartRate: 70),
                new ProfileSampleDto(10, 5.5, 25.2, 0.45, Ndl: 40, CeilingM: 1.5, AscentRateMPerMin: 6.0, Cns: 3.3, TankPressureBar: 210, Setpoint: 1.2, HeartRate: 82),
            ],
            Cylinders = [new CylinderDto(O2: 32, He: 0, StartBar: 210, EndBar: 70, VolumeL: 12, WorkingPressureBar: 232)],
            Visibility = "good",
            Current = "none",
            Weather = "sunny",
            EntryType = "shore",
            WaterType = "salt",
            ExposureSuit = "wetsuit",
            WeightKg = 5.0,
        };

        await _store.UpsertDiveAsync(dive);
        var loaded = Assert.Single(await _store.GetDivesAsync());

        var s1 = loaded.ProfileSamples[1];
        Assert.Equal(40, s1.Ndl);
        Assert.Equal(1.5, s1.CeilingM);
        Assert.Equal(210, s1.TankPressureBar);
        Assert.Equal(1.2, s1.Setpoint);
        Assert.Equal(82, s1.HeartRate);

        var cylinder = Assert.Single(loaded.Cylinders!);
        Assert.Equal(32, cylinder.O2);
        Assert.Equal(70, cylinder.EndBar);

        Assert.Equal("good", loaded.Visibility);
        Assert.Equal("wetsuit", loaded.ExposureSuit);
        Assert.Equal(5.0, loaded.WeightKg);
    }

    [Fact]
    public async Task Delete_is_a_tombstone_for_sync()
    {
        var dive = Dive();
        await _store.UpsertDiveAsync(dive);

        await _store.DeleteDiveAsync(dive.Id);

        var loaded = Assert.Single(await _store.GetDivesAsync());
        Assert.True(loaded.Deleted);
    }

    [Fact]
    public async Task DeleteDivesByVendor_hard_removes_only_that_vendor_and_returns_the_count()
    {
        await _store.InitializeAsync();
        var garminA = Dive() with { Id = Guid.NewGuid(), SourceVendor = "garmin_connect" };
        var garminB = Dive() with { Id = Guid.NewGuid(), SourceVendor = "garmin_connect" };
        var scuba = Dive() with { Id = Guid.NewGuid(), SourceVendor = "scubapro" };
        await _store.UpsertDiveAsync(garminA);
        await _store.UpsertDiveAsync(garminB);
        await _store.UpsertDiveAsync(scuba);

        var removed = await _store.DeleteDivesByVendorAsync("garmin_connect");

        Assert.Equal(2, removed);
        // Hard delete — the scubapro dive is the only row left, not a tombstone among ghosts.
        var remaining = Assert.Single(await _store.GetDivesAsync());
        Assert.Equal("scubapro", remaining.SourceVendor);
    }

    [Fact]
    public async Task Analytics_queue_is_fifo_and_removal_is_exact()
    {
        await _store.EnqueueAnalyticsEventAsync(Event(FunnelEvents.SiteViewed));
        await _store.EnqueueAnalyticsEventAsync(Event(FunnelEvents.SiteViewed));
        await _store.EnqueueAnalyticsEventAsync(Event(FunnelEvents.SiteContributionSubmitted));

        var batch = await _store.PeekAnalyticsEventsAsync(2);
        Assert.Equal(2, batch.Count);
        Assert.All(batch, e => Assert.Equal(FunnelEvents.SiteViewed, e.Name));

        await _store.RemoveAnalyticsEventsAsync(batch);

        var remaining = Assert.Single(await _store.PeekAnalyticsEventsAsync(10));
        Assert.Equal(FunnelEvents.SiteContributionSubmitted, remaining.Name);
    }

    [Fact]
    public async Task Upsert_replaces_by_id()
    {
        var dive = Dive();
        await _store.UpsertDiveAsync(dive);
        await _store.UpsertDiveAsync(dive with { Notes = "edited" });

        var loaded = Assert.Single(await _store.GetDivesAsync());
        Assert.Equal("edited", loaded.Notes);
    }
}
