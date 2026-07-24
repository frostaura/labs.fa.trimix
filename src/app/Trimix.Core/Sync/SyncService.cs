using System.Globalization;
using Microsoft.Extensions.Logging;
using Trimix.Contracts.Dtos;
using Trimix.Core.Abstractions;

namespace Trimix.Core.Sync;

/// <summary>
/// Local-first dive sync (architecture overview §6). Dives are ALWAYS saved to the local store
/// first; this service pushes/pulls <c>POST /dives/sync</c> when online + authenticated, with
/// last-write-wins on <c>UpdatedAtUtc</c>. Guest mode is local-only (no network calls at all);
/// on account creation the sign-in flow calls <see cref="SyncAsync"/>, which uploads the dives
/// logged as guest.
/// </summary>
public sealed class SyncService : ISyncService
{
    private readonly ILocalStore _store;
    private readonly IApiClient _api;
    private readonly IConnectivityService _connectivity;
    private readonly IAuthSession _session;
    private readonly IPreferences _preferences;
    private readonly ILogger<SyncService> _logger;

    public SyncService(
        ILocalStore store,
        IApiClient api,
        IConnectivityService connectivity,
        IAuthSession session,
        IPreferences preferences,
        ILogger<SyncService> logger)
    {
        _store = store;
        _api = api;
        _connectivity = connectivity;
        _session = session;
        _preferences = preferences;
        _logger = logger;

        // On sign-out / account switch, wipe the local dive store and the sync watermark so one
        // account's dives never upload into the next account signed in on this device, and so the
        // next account pulls its full history instead of starting from the previous cursor.
        session.AuthStateChanged += (_, isAuthenticated) =>
        {
            if (!isAuthenticated)
            {
                _ = ClearLocalStateAsync();
            }
        };
    }

    private async Task ClearLocalStateAsync()
    {
        try
        {
            await _store.ClearDivesAsync().ConfigureAwait(false);
            _preferences.Remove(PreferenceKeys.LastSyncUtc);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clear local dive state on sign-out.");
        }
    }

    public async Task SyncAsync(CancellationToken cancellationToken = default)
    {
        if (!_session.IsAuthenticated || !_connectivity.IsOnline)
        {
            return; // Guest and/or offline: dives stay local, nothing leaves the device.
        }

        var local = await _store.GetDivesAsync(cancellationToken).ConfigureAwait(false);
        var request = new DiveSyncRequest(
            Dives: [.. local.Where(d => !d.Deleted)],
            DeletedIds: [.. local.Where(d => d.Deleted).Select(d => d.Id)],
            Since: ReadLastSyncUtc());

        DiveSyncResponse response;
        try
        {
            response = await _api.SyncDivesAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Dive sync failed; local-first data is unaffected and the next sync retries.");
            return;
        }

        var localById = local.ToDictionary(d => d.Id);
        foreach (var serverDive in response.Dives)
        {
            // Last-write-wins on UpdatedAtUtc: only apply server rows at least as new as ours.
            if (!localById.TryGetValue(serverDive.Id, out var mine) || serverDive.UpdatedAtUtc >= mine.UpdatedAtUtc)
            {
                await _store.UpsertDiveAsync(serverDive, cancellationToken).ConfigureAwait(false);
            }
        }

        foreach (var deletedId in response.DeletedIds)
        {
            await _store.DeleteDiveAsync(deletedId, cancellationToken).ConfigureAwait(false);
        }

        _preferences.Set(
            PreferenceKeys.LastSyncUtc,
            response.ServerTimeUtc.ToString("O", CultureInfo.InvariantCulture));
    }

    private DateTime? ReadLastSyncUtc()
    {
        var raw = _preferences.Get(PreferenceKeys.LastSyncUtc);
        return raw is not null
               && DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
    }
}
