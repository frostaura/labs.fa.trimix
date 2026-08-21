using System.Text.Json;
using SQLite;
using Trimix.Contracts.Dtos;
using Trimix.Core.Abstractions;

namespace Trimix.Core.Storage;

/// <summary>Configuration for <see cref="SqliteLocalStore"/>.</summary>
public sealed class SqliteLocalStoreOptions
{
    /// <summary>Absolute path of the SQLite database file (the App uses its app-data directory).</summary>
    public required string DatabasePath { get; init; }
}

/// <summary>
/// sqlite-net-pcl <see cref="ILocalStore"/> (architecture overview §6/§7): the user's dives —
/// saved locally FIRST, always — and the offline analytics queue. DTOs are stored as JSON
/// documents in narrow tables (slice scale; no local relational queries needed), with the
/// columns SyncService/Logbook actually filter on lifted out.
/// Dive deletion is a tombstone (Deleted = true) so the deletion syncs to the server.
/// </summary>
public sealed class SqliteLocalStore(SqliteLocalStoreOptions options) : ILocalStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SQLiteAsyncConnection _connection = new(options.DatabasePath);
    private bool _initialized;

    /// <summary>One dive, as a JSON document keyed by the client-generated id.</summary>
    private sealed class DiveRow
    {
        [PrimaryKey]
        public string Id { get; set; } = "";

        public string Json { get; set; } = "";

        public bool Deleted { get; set; }

        /// <summary>Sort key so the logbook can page newest-first without deserializing.</summary>
        public long StartUnixMs { get; set; }
    }

    /// <summary>One queued analytics event; Seq preserves enqueue order (oldest first).</summary>
    private sealed class AnalyticsEventRow
    {
        [PrimaryKey, AutoIncrement]
        public int Seq { get; set; }

        public string Json { get; set; } = "";
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        await _connection.CreateTableAsync<DiveRow>().ConfigureAwait(false);
        await _connection.CreateTableAsync<AnalyticsEventRow>().ConfigureAwait(false);
        _initialized = true;
    }

    public async Task<IReadOnlyList<DiveDto>> GetDivesAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        var rows = await _connection.Table<DiveRow>()
            .OrderByDescending(r => r.StartUnixMs)
            .ToListAsync()
            .ConfigureAwait(false);
        return [.. rows.Select(r => JsonSerializer.Deserialize<DiveDto>(r.Json, JsonOptions)!)];
    }

    public async Task UpsertDiveAsync(DiveDto dive, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dive);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        var row = new DiveRow
        {
            Id = dive.Id.ToString("N"),
            Json = JsonSerializer.Serialize(dive, JsonOptions),
            Deleted = dive.Deleted,
            StartUnixMs = new DateTimeOffset(DateTime.SpecifyKind(dive.StartUtc, DateTimeKind.Utc)).ToUnixTimeMilliseconds(),
        };
        await _connection.InsertOrReplaceAsync(row).ConfigureAwait(false);
    }

    public async Task DeleteDiveAsync(Guid diveId, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        var id = diveId.ToString("N");
        var row = await _connection.FindAsync<DiveRow>(id).ConfigureAwait(false);
        if (row is null)
        {
            return;
        }

        var dive = JsonSerializer.Deserialize<DiveDto>(row.Json, JsonOptions)!;
        await UpsertDiveAsync(dive with { Deleted = true }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> DeleteDivesByVendorAsync(string sourceVendor, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourceVendor);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        // SourceVendor lives inside the JSON blob (not a column), so filter by deserializing. Hard-delete by
        // primary key — a tombstone would be resurrected by the next sync of a still-consented provider, and
        // there is no server to sync a deletion to anyway.
        var rows = await _connection.Table<DiveRow>().ToListAsync().ConfigureAwait(false);
        var removed = 0;
        foreach (var row in rows)
        {
            var dive = JsonSerializer.Deserialize<DiveDto>(row.Json, JsonOptions)!;
            if (string.Equals(dive.SourceVendor, sourceVendor, StringComparison.Ordinal))
            {
                await _connection.DeleteAsync<DiveRow>(row.Id).ConfigureAwait(false);
                removed++;
            }
        }

        return removed;
    }

    public async Task ClearDivesAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _connection.DeleteAllAsync<DiveRow>().ConfigureAwait(false);
    }

    public async Task EnqueueAnalyticsEventAsync(AnalyticsEventDto analyticsEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(analyticsEvent);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _connection
            .InsertAsync(new AnalyticsEventRow { Json = JsonSerializer.Serialize(analyticsEvent, JsonOptions) })
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AnalyticsEventDto>> PeekAnalyticsEventsAsync(int max, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        var rows = await _connection.Table<AnalyticsEventRow>()
            .OrderBy(r => r.Seq)
            .Take(max)
            .ToListAsync()
            .ConfigureAwait(false);
        return [.. rows.Select(r => JsonSerializer.Deserialize<AnalyticsEventDto>(r.Json, JsonOptions)!)];
    }

    public async Task RemoveAnalyticsEventsAsync(IReadOnlyList<AnalyticsEventDto> analyticsEvents, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(analyticsEvents);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        foreach (var analyticsEvent in analyticsEvents)
        {
            var json = JsonSerializer.Serialize(analyticsEvent, JsonOptions);
            // Remove exactly one matching row (the oldest) per acknowledged event.
            await _connection.ExecuteAsync(
                    "DELETE FROM AnalyticsEventRow WHERE Seq = (SELECT MIN(Seq) FROM AnalyticsEventRow WHERE Json = ?)",
                    json)
                .ConfigureAwait(false);
        }
    }
}
