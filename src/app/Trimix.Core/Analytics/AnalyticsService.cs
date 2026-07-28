using Microsoft.Extensions.Logging;
using Trimix.Contracts;
using Trimix.Contracts.Dtos;
using Trimix.Core.Abstractions;

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

    /// <summary>Max events drained from the durable queue per flush batch.</summary>
    public int FlushBatchSize { get; set; } = 50;
}

/// <summary>
/// The client analytics pipe — LOCAL SINK ONLY (serverless; the ingest server was removed 2026-07-27):
/// <list type="bullet">
/// <item>validates every event against the frozen <see cref="FunnelEvents"/> contract
/// (unknown name / missing required prop → throw in Debug, drop + log in Release) — the frozen
/// taxonomy invariant is preserved even though nothing aggregates the events any more,</item>
/// <item>persists the queue through <see cref="ILocalStore"/> (durable on-device buffer),</item>
/// <item><see cref="FlushAsync"/> drains the buffer to the debug log and discards — there is no
/// ingest endpoint. M3-style aggregate telemetry is an OPEN ITEM (see the rebuild plan): if the
/// founder later wants a thin analytics beacon, this is the single place to re-point.</item>
/// <item>emits <c>install</c> exactly once per install and <c>first_run_completed</c> once after
/// onboarding, guarded by preference flags (wiped on uninstall — the correct per-install scope).</item>
/// </list>
/// </summary>
public sealed class AnalyticsService(
    ILocalStore store,
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

    /// <summary>Single source of truth for the analytics pseudo-id, via the provider.</summary>
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

    /// <summary>
    /// Local drain: writes each queued event to the debug log and removes it from the durable queue so
    /// the buffer stays bounded. Nothing leaves the device.
    /// </summary>
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        await DrainEnqueueTailAsync().ConfigureAwait(false);

        // Serialize concurrent flushes so two callers cannot drain the same rows twice.
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

                foreach (var dto in batch)
                {
                    logger.LogDebug("funnel {Event} {PseudoId} {Props}", dto.Name, dto.UserPseudoId, dto.Props);
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

        // Enumerated-value check via the shared frozen contract — a drifted trigger / entitlement_state /
        // status / outcome / dive_logged_import.source is caught HERE and never enters the durable queue.
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
