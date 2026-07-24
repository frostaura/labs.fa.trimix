using System.Net.Http.Json;
using Trimix.Contracts;
using Trimix.Contracts.Dtos;
using Xunit;

namespace Trimix.Api.Tests;

/// <summary>
/// POST /dives/sync (architecture overview §6): client-GUID upsert, last-write-wins on
/// UpdatedAtUtc, soft delete, server-changes-since cursor, profile samples included.
/// </summary>
public sealed class DiveSyncTests : IDisposable
{
    private readonly TrimixApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private static DiveDto NewDive(Guid id, DateTime updatedAtUtc, string? notes = null, int samples = 3) => new(
        id,
        StartUtc: new DateTime(2026, 6, 20, 8, 30, 0, DateTimeKind.Utc),
        DurationSec: 2760,
        MaxDepthM: 32.4,
        AvgDepthM: 17.8,
        MinTempC: 24.0,
        GasMix: "EAN32",
        Source: DiveSources.Manual,
        SourceVendor: null,
        SiteId: null,
        ConditionsJson: """{"visibilityM":20}""",
        GearCsv: "BCD,Regulator",
        BuddiesCsv: "Sam",
        Notes: notes,
        Rating: 5,
        UpdatedAtUtc: updatedAtUtc,
        Deleted: false,
        ProfileSamples: Enumerable.Range(0, samples).Select(i => new ProfileSampleDto(i * 30, i * 5.5, 25.0 - i, null)).ToList());

    private static async Task<DiveSyncResponse> SyncAsync(HttpClient client, DiveSyncRequest request)
    {
        var response = await client.PostAsJsonAsync(ApiRoutes.Dives.SyncFull, request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<DiveSyncResponse>();
        Assert.NotNull(body);
        return body;
    }

    [Fact]
    public async Task Upsert_persists_dives_with_profile_samples_and_pull_returns_them()
    {
        var (deviceA, _) = await _factory.RegisterAsync("sync@example.com");
        var t0 = DateTime.UtcNow;
        var diveId = Guid.NewGuid();

        var push = await SyncAsync(deviceA, new DiveSyncRequest([NewDive(diveId, t0, "first push", samples: 4)], [], null));
        Assert.Empty(push.Dives); // Client won: nothing to send back.
        Assert.Empty(push.DeletedIds);

        // Second device, same account: full pull.
        var deviceB = _factory.CreateClient();
        await deviceB.LoginAsync("sync@example.com");
        var pull = await SyncAsync(deviceB, new DiveSyncRequest([], [], null));

        var dive = Assert.Single(pull.Dives);
        Assert.Equal(diveId, dive.Id);
        Assert.Equal("first push", dive.Notes);
        Assert.Equal(4, dive.ProfileSamples.Count);
        Assert.Equal(new[] { 0, 30, 60, 90 }, dive.ProfileSamples.Select(s => s.TSeconds).ToArray());
    }

    [Fact]
    public async Task Upsert_persists_rich_channels_cylinders_and_conditions()
    {
        var (deviceA, _) = await _factory.RegisterAsync("rich@example.com");
        var diveId = Guid.NewGuid();
        var t0 = DateTime.UtcNow;

        var rich = new DiveDto(
            diveId,
            StartUtc: new DateTime(2026, 6, 21, 9, 0, 0, DateTimeKind.Utc),
            DurationSec: 3300,
            MaxDepthM: 41.2,
            AvgDepthM: 22.6,
            MinTempC: 12.5,
            GasMix: "21/35",
            Source: DiveSources.Import,
            SourceVendor: "shearwater",
            SiteId: null,
            ConditionsJson: null,
            GearCsv: null,
            BuddiesCsv: null,
            Notes: "trimix wall dive",
            Rating: 5,
            UpdatedAtUtc: t0,
            Deleted: false,
            ProfileSamples:
            [
                new ProfileSampleDto(0, 0.0, 21.0, 0.21, Ndl: 99, CeilingM: 0, AscentRateMPerMin: 0, Cns: 1.2, TankPressureBar: 230, Setpoint: 1.3, HeartRate: 72),
                new ProfileSampleDto(60, 18.5, 14.0, 1.05, Ndl: 12, CeilingM: 3.0, AscentRateMPerMin: 9.0, Cns: 8.4, TankPressureBar: 205, Setpoint: 1.3, HeartRate: 88),
            ],
            Cylinders: [new CylinderDto(O2: 21, He: 35, StartBar: 232, EndBar: 90, VolumeL: 24, WorkingPressureBar: 232)],
            Visibility: "excellent",
            Current: "mild",
            Weather: "overcast",
            EntryType: "boat",
            WaterType: "salt",
            ExposureSuit: "drysuit",
            WeightKg: 6.5);

        await SyncAsync(deviceA, new DiveSyncRequest([rich], [], null));

        // Second device pulls: every rich channel, the cylinder, and the typed conditions must round-trip.
        var deviceB = _factory.CreateClient();
        await deviceB.LoginAsync("rich@example.com");
        var pull = await SyncAsync(deviceB, new DiveSyncRequest([], [], null));

        var dive = Assert.Single(pull.Dives);
        Assert.Equal(diveId, dive.Id);

        Assert.Equal(2, dive.ProfileSamples.Count);
        var s1 = dive.ProfileSamples[1];
        Assert.Equal(12, s1.Ndl);
        Assert.Equal(3.0, s1.CeilingM);
        Assert.Equal(9.0, s1.AscentRateMPerMin);
        Assert.Equal(8.4, s1.Cns);
        Assert.Equal(205, s1.TankPressureBar);
        Assert.Equal(1.3, s1.Setpoint);
        Assert.Equal(88, s1.HeartRate);

        var cylinder = Assert.Single(dive.Cylinders!);
        Assert.Equal(21, cylinder.O2);
        Assert.Equal(35, cylinder.He);
        Assert.Equal(232, cylinder.StartBar);
        Assert.Equal(90, cylinder.EndBar);
        Assert.Equal(24, cylinder.VolumeL);
        Assert.Equal(232, cylinder.WorkingPressureBar);

        Assert.Equal("excellent", dive.Visibility);
        Assert.Equal("mild", dive.Current);
        Assert.Equal("overcast", dive.Weather);
        Assert.Equal("boat", dive.EntryType);
        Assert.Equal("salt", dive.WaterType);
        Assert.Equal("drysuit", dive.ExposureSuit);
        Assert.Equal(6.5, dive.WeightKg);
    }

    [Fact]
    public async Task Last_write_wins_on_UpdatedAtUtc()
    {
        var (client, _) = await _factory.RegisterAsync("lww@example.com");
        var diveId = Guid.NewGuid();
        var t0 = DateTime.UtcNow;

        await SyncAsync(client, new DiveSyncRequest([NewDive(diveId, t0, "v1")], [], null));

        // Newer client write wins.
        await SyncAsync(client, new DiveSyncRequest([NewDive(diveId, t0.AddMinutes(5), "v2-newer")], [], null));
        var afterNewer = await SyncAsync(client, new DiveSyncRequest([], [], null));
        Assert.Equal("v2-newer", Assert.Single(afterNewer.Dives).Notes);

        // Stale write loses — and the server returns its (winning) version so the client converges.
        var stale = await SyncAsync(client, new DiveSyncRequest([NewDive(diveId, t0.AddMinutes(1), "v3-stale")], [], t0.AddMinutes(-1)));
        Assert.Equal("v2-newer", Assert.Single(stale.Dives).Notes);

        var final = await SyncAsync(client, new DiveSyncRequest([], [], null));
        Assert.Equal("v2-newer", Assert.Single(final.Dives).Notes);
    }

    [Fact]
    public async Task Soft_delete_propagates_through_the_cursor()
    {
        var (client, _) = await _factory.RegisterAsync("delete@example.com");
        var keepId = Guid.NewGuid();
        var dropId = Guid.NewGuid();
        var t0 = DateTime.UtcNow;

        await SyncAsync(client, new DiveSyncRequest([NewDive(keepId, t0), NewDive(dropId, t0)], [], null));
        var cursor = (await SyncAsync(client, new DiveSyncRequest([], [], null))).ServerTimeUtc;

        await SyncAsync(client, new DiveSyncRequest([], [dropId], null));

        // Another device syncing from the cursor sees the deletion (and not the untouched dive).
        var deviceB = _factory.CreateClient();
        await deviceB.LoginAsync("delete@example.com");
        var pull = await SyncAsync(deviceB, new DiveSyncRequest([], [], cursor));
        Assert.Equal(dropId, Assert.Single(pull.DeletedIds));
        Assert.Empty(pull.Dives);

        // A full pull only returns the surviving dive.
        var full = await SyncAsync(deviceB, new DiveSyncRequest([], [], null));
        Assert.Equal(keepId, Assert.Single(full.Dives).Id);
        Assert.Equal(dropId, Assert.Single(full.DeletedIds));
    }

    [Fact]
    public async Task Users_cannot_touch_each_others_dives()
    {
        var diveId = Guid.NewGuid();
        var t0 = DateTime.UtcNow;

        var (alice, _) = await _factory.RegisterAsync("alice@example.com");
        await SyncAsync(alice, new DiveSyncRequest([NewDive(diveId, t0, "alice's dive")], [], null));

        // Bob pushes a dive with the same GUID and tries to delete it: both must be ignored.
        var (bob, _) = await _factory.RegisterAsync("bob@example.com");
        await SyncAsync(bob, new DiveSyncRequest([NewDive(diveId, t0.AddHours(1), "bob's takeover")], [diveId], null));

        var alicePull = await SyncAsync(alice, new DiveSyncRequest([], [], null));
        Assert.Equal("alice's dive", Assert.Single(alicePull.Dives).Notes);
        Assert.Empty(alicePull.DeletedIds);

        var bobPull = await SyncAsync(bob, new DiveSyncRequest([], [], null));
        Assert.Empty(bobPull.Dives);
    }

    [Fact]
    public async Task Lagging_client_clock_does_not_hide_a_dive_from_other_devices()
    {
        // Device A establishes a cursor from a point in time BEFORE device B writes anything.
        var (deviceA, _) = await _factory.RegisterAsync("skew@example.com");
        var deviceB = _factory.CreateClient();
        await deviceB.LoginAsync("skew@example.com");

        var cursor = (await SyncAsync(deviceA, new DiveSyncRequest([], [], null))).ServerTimeUtc;

        // Device B's clock lags 30 minutes: its dive's client UpdatedAtUtc is BEFORE A's cursor, but
        // the server stamps its own receive time on the row.
        var laggedId = Guid.NewGuid();
        await SyncAsync(deviceB, new DiveSyncRequest(
            [NewDive(laggedId, DateTime.UtcNow.AddMinutes(-30), "logged on a slow phone")], [], null));

        // A's incremental pull from its earlier cursor must still see B's dive (regression: filtering
        // on the client clock silently skipped it).
        var pull = await SyncAsync(deviceA, new DiveSyncRequest([], [], cursor));
        Assert.Equal(laggedId, Assert.Single(pull.Dives).Id);
    }

    [Fact]
    public async Task Oversized_dive_notes_are_rejected()
    {
        var (client, _) = await _factory.RegisterAsync("bigtext@example.com");
        var huge = new string('x', 5000); // > DiveNotesMaxLength (4096)
        var response = await client.PostAsJsonAsync(
            ApiRoutes.Dives.SyncFull,
            new DiveSyncRequest([NewDive(Guid.NewGuid(), DateTime.UtcNow, huge)], [], null));
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Sync_requires_authentication()
    {
        var anonymous = _factory.CreateClient();
        var response = await anonymous.PostAsJsonAsync(ApiRoutes.Dives.SyncFull, new DiveSyncRequest([], [], null));
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
