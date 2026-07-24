using Microsoft.Extensions.Logging.Abstractions;
using Trimix.Contracts;
using Trimix.Contracts.Dtos;
using Trimix.Core.Platform;
using Trimix.Core.Storage;
using Trimix.Core.Sync;
using Trimix.Core.Tests.Fakes;
using Xunit;

namespace Trimix.Core.Tests;

/// <summary>Local-first sync: guest stays fully local; sign-in uploads; last-write-wins pulls.</summary>
public class SyncServiceTests
{
    private readonly InMemoryLocalStore _store = new();
    private readonly FakeApiClient _api = new();
    private readonly FakeConnectivity _connectivity = new();
    private readonly FakeAuthSession _session = new();
    private readonly InMemoryPreferences _preferences = new();

    private SyncService CreateService() => new(
        _store,
        _api,
        _connectivity,
        _session,
        _preferences,
        NullLogger<SyncService>.Instance);

    private static DiveDto Dive(Guid? id = null, DateTime? updatedAtUtc = null, bool deleted = false) => new(
        Id: id ?? Guid.NewGuid(),
        StartUtc: new DateTime(2026, 6, 20, 8, 30, 0, DateTimeKind.Utc),
        DurationSec: 2700,
        MaxDepthM: 18.5,
        AvgDepthM: 11.2,
        MinTempC: 24.0,
        GasMix: "Air",
        Source: DiveSources.Manual,
        SourceVendor: null,
        SiteId: null,
        ConditionsJson: null,
        GearCsv: null,
        BuddiesCsv: null,
        Notes: null,
        Rating: 4,
        UpdatedAtUtc: updatedAtUtc ?? new DateTime(2026, 6, 20, 10, 0, 0, DateTimeKind.Utc),
        Deleted: deleted,
        ProfileSamples: []);

    [Fact]
    public async Task Guest_mode_never_touches_the_network()
    {
        await _store.UpsertDiveAsync(Dive());
        _session.IsAuthenticated = false;

        await CreateService().SyncAsync();

        Assert.Empty(_api.SyncRequests);
        Assert.Single(await _store.GetDivesAsync()); // dives stay local
    }

    [Fact]
    public async Task Offline_defers_sync_without_data_loss()
    {
        await _store.UpsertDiveAsync(Dive());
        _session.IsAuthenticated = true;
        _connectivity.IsOnline = false;

        await CreateService().SyncAsync();

        Assert.Empty(_api.SyncRequests);
    }

    [Fact]
    public async Task Guest_dives_upload_after_account_creation()
    {
        // PRD Flow A: dives logged as guest are local-only; creating an account uploads them.
        var guestDive = Dive();
        var tombstone = Dive(deleted: true);
        await _store.UpsertDiveAsync(guestDive);
        await _store.UpsertDiveAsync(tombstone);

        var serverTime = new DateTime(2026, 7, 3, 12, 0, 0, DateTimeKind.Utc);
        _api.SyncHandler = _ => new DiveSyncResponse([], [], serverTime);

        _session.IsAuthenticated = false;
        var sync = CreateService();
        await sync.SyncAsync(); // guest: nothing
        Assert.Empty(_api.SyncRequests);

        _session.IsAuthenticated = true; // account created
        await sync.SyncAsync();

        var request = Assert.Single(_api.SyncRequests);
        Assert.Equal(guestDive.Id, Assert.Single(request.Dives).Id);
        Assert.Equal(tombstone.Id, Assert.Single(request.DeletedIds));
        Assert.Null(request.Since); // first-ever sync
        Assert.NotNull(_preferences.Get(PreferenceKeys.LastSyncUtc));
    }

    [Fact]
    public async Task Second_sync_sends_the_server_watermark()
    {
        _session.IsAuthenticated = true;
        var serverTime = new DateTime(2026, 7, 3, 12, 0, 0, DateTimeKind.Utc);
        _api.SyncHandler = _ => new DiveSyncResponse([], [], serverTime);

        var sync = CreateService();
        await sync.SyncAsync();
        await sync.SyncAsync();

        Assert.Equal(serverTime, _api.SyncRequests[1].Since);
    }

    [Fact]
    public async Task Pull_applies_last_write_wins_on_UpdatedAtUtc()
    {
        _session.IsAuthenticated = true;
        var sharedId = Guid.NewGuid();
        var localNewer = Dive(sharedId, updatedAtUtc: new DateTime(2026, 7, 2, 0, 0, 0, DateTimeKind.Utc));
        await _store.UpsertDiveAsync(localNewer);

        var serverOlder = Dive(sharedId, updatedAtUtc: new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)) with { Notes = "stale server copy" };
        var serverNew = Dive(updatedAtUtc: new DateTime(2026, 7, 3, 0, 0, 0, DateTimeKind.Utc)) with { Notes = "from another device" };
        _api.SyncHandler = _ => new DiveSyncResponse([serverOlder, serverNew], [], DateTime.UtcNow);

        await CreateService().SyncAsync();

        var dives = await _store.GetDivesAsync();
        Assert.Equal(2, dives.Count);
        Assert.Null(dives.Single(d => d.Id == sharedId).Notes); // stale server copy did NOT overwrite
        Assert.Equal("from another device", dives.Single(d => d.Id == serverNew.Id).Notes);
    }

    [Fact]
    public async Task Pull_applies_server_deletions()
    {
        _session.IsAuthenticated = true;
        var dive = Dive();
        await _store.UpsertDiveAsync(dive);
        _api.SyncHandler = _ => new DiveSyncResponse([], [dive.Id], DateTime.UtcNow);

        await CreateService().SyncAsync();

        Assert.True((await _store.GetDivesAsync()).Single(d => d.Id == dive.Id).Deleted);
    }

    [Fact]
    public async Task Sign_out_clears_local_dives_and_the_sync_cursor()
    {
        // Account-bound: signing out must wipe the local store + watermark so the next account on the
        // device never uploads the previous user's dives nor skips its own history.
        _session.IsAuthenticated = true;
        await _store.UpsertDiveAsync(Dive());
        _preferences.Set(PreferenceKeys.LastSyncUtc, "2026-07-01T00:00:00.0000000Z");
        var sync = CreateService(); // subscribes to auth-state changes
        Assert.NotNull(sync);

        await _session.SignOutAsync(); // raises AuthStateChanged(false)

        Assert.Empty(await _store.GetDivesAsync());
        Assert.Null(_preferences.Get(PreferenceKeys.LastSyncUtc));
    }

    [Fact]
    public async Task Network_failure_leaves_local_data_and_watermark_untouched()
    {
        _session.IsAuthenticated = true;
        await _store.UpsertDiveAsync(Dive());
        _api.SyncHandler = _ => throw new HttpRequestException("boom");

        await CreateService().SyncAsync(); // swallowed, local-first unaffected

        Assert.Single(await _store.GetDivesAsync());
        Assert.Null(_preferences.Get(PreferenceKeys.LastSyncUtc));
    }
}
