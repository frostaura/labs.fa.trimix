using System.Net;
using Microsoft.Extensions.Logging;
using Trimix.Contracts;
using Trimix.Contracts.Dtos;
using Trimix.Core.Abstractions;
using Trimix.Core.Api;

namespace Trimix.Core.Analytics;

/// <summary>Thrown (Debug builds) when a tracked event violates the frozen funnel contract.</summary>
public sealed class AnalyticsContractViolationException(string message) : ArgumentException(message);

/// <summary>Behavior knobs for <see cref="AnalyticsService"/>.</summary>
public sealed class AnalyticsOptions
{
    /// <summary>
    /// Contract-violation behavior (architecture overview §7): throw in Debug, drop + log in Release.
    /// The default follows the build configuration of Trimix.Core; hosts may override.
    /// </summary>
    public bool ThrowOnContractViolation { get; set; } =
#if DEBUG
        true;
#else
        false;
#endif

    /// <summary>Max events per <c>POST /analytics/events</c> batch.</summary>
    public int FlushBatchSize { get; set; } = 50;
}

/// <summary>
/// The client analytics pipe (architecture overview §7):
/// <list type="bullet">
/// <item>validates every event against the frozen <see cref="FunnelEvents"/> contract
/// (unknown name / missing required prop → throw in Debug, drop + log in Release),</item>
/// <item>persists the queue through <see cref="ILocalStore"/> (offline buffer — nothing is lost
/// when the device has no connectivity),</item>
/// <item>batch-flushes through <see cref="IApiClient"/>, removing events only after the server
/// acknowledged them,</item>
/// <item>emits <c>install</c> exactly once per install and <c>first_run_completed</c> once after
/// onboarding, guarded by preference flags (wiped on uninstall — the correct per-install scope).</item>
/// </list>
/// </summary>
public sealed class AnalyticsService(
    ILocalStore store,
    IApiClient api,
    IConnectivityService connectivity,
    IPreferences preferences,
    IAppInfo appInfo,
    IClock clock,
    IUserPseudoIdProvider pseudoIds,
    AnalyticsOptions options,
    ILogger<AnalyticsService> logger) : IAnalytics
{
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _flushGate = new(1, 1);
    private Task _enqueueTail = Task.CompletedTask;

    /// <summary>Single source of truth (shared with <see cref="Api.ApiClient"/>) via the provider.</summary>
    public string UserPseudoId => pseudoIds.UserPseudoId;

    public void Track(string name, IReadOnlyDictionary<string, string>? props = null)
    {
        if (!TryValidate(name, props, out var violation))
        {
            if (options.ThrowOnContractViolation)
            {
                throw new AnalyticsContractViolationException(violation!);
            }

            logger.LogWarning("Dropped analytics event violating the frozen funnel contract: {Violation}", violation);
            return;
        }

        var dto = new AnalyticsEventDto(
            name,
            props ?? new Dictionary<string, string>(),
            UserPseudoId,
            clock.UtcNow.UtcDateTime,
            appInfo.Platform,
            appInfo.AppVersion,
            // Client-generated idempotency key so a retried batch (ApiClient's retry, or a lost
            // response on a flaky network) dedups server-side instead of double-appending the row.
            Guid.NewGuid());

        // Serialize writes onto a chain so Track stays synchronous and ordering is preserved.
        lock (_gate)
        {
            _enqueueTail = EnqueueAfterAsync(_enqueueTail, dto);
        }
    }

    private async Task EnqueueAfterAsync(Task tail, AnalyticsEventDto dto)
    {
        await tail.ConfigureAwait(false);
        try
        {
            await store.EnqueueAnalyticsEventAsync(dto).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist analytics event {Event}; the event is lost.", dto.Name);
        }
    }

    public async Task EnsureInstallTrackedAsync(string source, CancellationToken cancellationToken = default)
    {
        if (preferences.Get(PreferenceKeys.InstallTracked) is not null)
        {
            return;
        }

        Track(FunnelEvents.Install, new Dictionary<string, string>
        {
            [FunnelEvents.Props.Platform] = appInfo.Platform,
            [FunnelEvents.Props.Source] = source,
            [FunnelEvents.Props.AppVersion] = appInfo.AppVersion,
        });
        preferences.Set(PreferenceKeys.InstallTracked, "1");
        await DrainEnqueueTailAsync().ConfigureAwait(false);
    }

    public async Task EnsureFirstRunCompletedTrackedAsync(CancellationToken cancellationToken = default)
    {
        if (preferences.Get(PreferenceKeys.FirstRunTracked) is not null)
        {
            return;
        }

        Track(FunnelEvents.FirstRunCompleted, new Dictionary<string, string>
        {
            [FunnelEvents.Props.UserPseudoId] = UserPseudoId,
        });
        preferences.Set(PreferenceKeys.FirstRunTracked, "1");
        await DrainEnqueueTailAsync().ConfigureAwait(false);
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        await DrainEnqueueTailAsync().ConfigureAwait(false);

        if (!connectivity.IsOnline)
        {
            return; // Offline: the queue stays buffered in the local store.
        }

        // Serialize concurrent flushes (startup flush vs. the post-purchase flush) so two callers
        // cannot peek the same oldest rows and POST them twice.
        await _flushGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var batch = await store.PeekAnalyticsEventsAsync(options.FlushBatchSize, cancellationToken).ConfigureAwait(false);
                if (batch.Count == 0)
                {
                    return;
                }

                try
                {
                    await api.SendAnalyticsAsync(new AnalyticsBatchRequest(batch), cancellationToken).ConfigureAwait(false);
                }
                catch (ApiException ex) when ((int)ex.StatusCode is >= 400 and < 500 && ex.StatusCode != HttpStatusCode.RequestTimeout)
                {
                    // A 4xx is a PERMANENT contract rejection, not a transient fault — retrying re-sends the same
                    // rejected batch on every flush forever, poison-pilling the whole queue so no later event for
                    // this install ever reaches the server. Quarantine it: drop the offending batch and continue
                    // draining, so one bad row cannot block the funnel. (Client validation should already keep
                    // drifted events out of the queue; this guards a batch queued by an older/edited build.)
                    logger.LogError(
                        ex, "Analytics batch rejected (HTTP {Status}); dropping {Count} events to unblock the queue.",
                        (int)ex.StatusCode, batch.Count);
                    await store.RemoveAnalyticsEventsAsync(batch, cancellationToken).ConfigureAwait(false);
                    continue;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Best effort: keep the batch buffered and try again on the next flush.
                    logger.LogWarning(ex, "Analytics flush failed; {Count} events stay queued.", batch.Count);
                    return;
                }

                await store.RemoveAnalyticsEventsAsync(batch, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _flushGate.Release();
        }
    }

    private Task DrainEnqueueTailAsync()
    {
        Task tail;
        lock (_gate)
        {
            tail = _enqueueTail;
        }

        return tail;
    }

    private static bool TryValidate(string name, IReadOnlyDictionary<string, string>? props, out string? violation)
    {
        if (!FunnelEvents.RequiredProps.TryGetValue(name, out var required))
        {
            violation = $"'{name}' is not a frozen funnel event (see Trimix.Contracts.FunnelEvents).";
            return false;
        }

        foreach (var key in required)
        {
            if (props is null || !props.TryGetValue(key, out var value) || string.IsNullOrEmpty(value))
            {
                violation = $"Event '{name}' is missing required property '{key}'.";
                return false;
            }
        }

        // Enumerated-value check via the SAME shared contract the server ingest uses — so a drifted trigger /
        // entitlement_state / status / outcome / dive_logged_import.source is caught HERE and never enters the
        // durable queue (where it would be rejected batch-fatally and poison-pill every later event).
        var enumViolation = FunnelValidation.ValidateEnumeratedValues(name, props);
        if (enumViolation is not null)
        {
            violation = enumViolation;
            return false;
        }

        violation = null;
        return true;
    }
}
