using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Trimix.Contracts;
using Trimix.Core.Analytics;
using Trimix.Core.Api;
using Trimix.Core.Platform;
using Trimix.Core.Storage;
using Trimix.Core.Tests.Fakes;
using Xunit;

namespace Trimix.Core.Tests;

/// <summary>Frozen-contract validation + persistent queue + batch flush + offline buffering + install-once.</summary>
public class AnalyticsServiceTests
{
    private readonly InMemoryLocalStore _store = new();
    private readonly FakeApiClient _api = new();
    private readonly FakeConnectivity _connectivity = new();
    private readonly InMemoryPreferences _preferences = new();
    private readonly FakeAppInfo _appInfo = new();
    private readonly FakeClock _clock = new();

    private AnalyticsService CreateService(bool throwOnViolation = true, int batchSize = 50) => new(
        _store,
        _api,
        _connectivity,
        _preferences,
        _appInfo,
        _clock,
        new UserPseudoIdProvider(_preferences),
        new AnalyticsOptions { ThrowOnContractViolation = throwOnViolation, FlushBatchSize = batchSize },
        NullLogger<AnalyticsService>.Instance);

    private static Dictionary<string, string> SiteViewedProps() => new()
    {
        [FunnelEvents.Props.SiteId] = Guid.NewGuid().ToString("N"),
    };

    [Fact]
    public void Unknown_event_name_throws_in_debug_mode()
    {
        var analytics = CreateService(throwOnViolation: true);
        var ex = Assert.Throws<AnalyticsContractViolationException>(
            () => analytics.Track("dive_logged", SiteViewedProps()));
        Assert.Contains("not a frozen funnel event", ex.Message);
    }

    [Fact]
    public void Missing_required_prop_throws_in_debug_mode()
    {
        var analytics = CreateService(throwOnViolation: true);
        var ex = Assert.Throws<AnalyticsContractViolationException>(
            () => analytics.Track(FunnelEvents.PaywallShown, new Dictionary<string, string>
            {
                [FunnelEvents.Props.Trigger] = FunnelEvents.Values.TriggerBleImportCap,
                // entitlement_state missing
            }));
        Assert.Contains(FunnelEvents.Props.EntitlementState, ex.Message);
    }

    [Fact]
    public async Task Release_mode_drops_invalid_events_instead_of_throwing()
    {
        var analytics = CreateService(throwOnViolation: false);
        analytics.Track("not_an_event");
        analytics.Track(FunnelEvents.SiteViewed); // missing site_id

        await analytics.FlushAsync();

        Assert.Empty(_api.AnalyticsBatches);
        Assert.Empty(await _store.PeekAnalyticsEventsAsync(10));
    }

    [Fact]
    public void Drifted_enumerated_value_throws_in_debug_mode()
    {
        // The client validator now runs the SAME enumerated-value contract as the server (shared
        // FunnelValidation), so a drifted trigger is caught client-side — not silently queued.
        var analytics = CreateService(throwOnViolation: true);
        var ex = Assert.Throws<AnalyticsContractViolationException>(
            () => analytics.Track(FunnelEvents.PaywallShown, new Dictionary<string, string>
            {
                [FunnelEvents.Props.Trigger] = "settings_upsell", // not in the frozen set
                [FunnelEvents.Props.EntitlementState] = FunnelEvents.Values.EntitlementStateFree,
            }));
        Assert.Contains("frozen set", ex.Message);
    }

    [Fact]
    public async Task Drifted_enumerated_value_is_dropped_client_side_and_never_queued()
    {
        var analytics = CreateService(throwOnViolation: false);
        analytics.Track(FunnelEvents.PaywallShown, new Dictionary<string, string>
        {
            [FunnelEvents.Props.Trigger] = "settings_upsell",
            [FunnelEvents.Props.EntitlementState] = FunnelEvents.Values.EntitlementStateFree,
        });

        await analytics.FlushAsync();

        Assert.Empty(_api.AnalyticsBatches);
        Assert.Empty(await _store.PeekAnalyticsEventsAsync(10)); // never entered the durable queue
    }

    [Fact]
    public async Task Contract_rejected_batch_is_quarantined_not_retried_forever()
    {
        // A 4xx from ingest is a PERMANENT contract rejection: the batch must be dropped, not left to re-send
        // on every flush (which would poison-pill the whole queue and block all later funnel events).
        var analytics = CreateService();
        analytics.Track(FunnelEvents.SiteViewed, SiteViewedProps());
        _api.AnalyticsHandler = _ => throw new ApiException(HttpStatusCode.BadRequest, new ApiError("contract", "rejected"));

        await analytics.FlushAsync();
        Assert.Empty(await _store.PeekAnalyticsEventsAsync(10)); // poison batch quarantined, queue unblocked

        // The queue is no longer stuck: a subsequent valid event flushes through cleanly.
        _api.AnalyticsHandler = null;
        analytics.Track(FunnelEvents.SiteViewed, SiteViewedProps());
        await analytics.FlushAsync();
        Assert.Empty(await _store.PeekAnalyticsEventsAsync(10));
    }

    [Fact]
    public async Task Valid_event_is_stamped_queued_and_flushed()
    {
        var analytics = CreateService();
        var props = SiteViewedProps();
        analytics.Track(FunnelEvents.SiteViewed, props);

        await analytics.FlushAsync();

        var batch = Assert.Single(_api.AnalyticsBatches);
        var sent = Assert.Single(batch.Events);
        Assert.Equal(FunnelEvents.SiteViewed, sent.Name);
        Assert.Equal(props[FunnelEvents.Props.SiteId], sent.Props[FunnelEvents.Props.SiteId]);
        Assert.Equal(analytics.UserPseudoId, sent.UserPseudoId);
        Assert.Equal(_appInfo.Platform, sent.Platform);
        Assert.Equal(_appInfo.AppVersion, sent.AppVersion);
        Assert.Equal(_clock.UtcNow.UtcDateTime, sent.ClientTimestampUtc);

        // Acknowledged events leave the queue.
        Assert.Empty(await _store.PeekAnalyticsEventsAsync(10));
    }

    [Fact]
    public async Task Offline_events_buffer_and_flush_when_back_online()
    {
        var analytics = CreateService();
        _connectivity.IsOnline = false;
        analytics.Track(FunnelEvents.SiteViewed, SiteViewedProps());

        await analytics.FlushAsync();
        Assert.Empty(_api.AnalyticsBatches);
        Assert.Single(await _store.PeekAnalyticsEventsAsync(10)); // still buffered

        _connectivity.IsOnline = true;
        await analytics.FlushAsync();
        Assert.Single(_api.AnalyticsBatches);
        Assert.Empty(await _store.PeekAnalyticsEventsAsync(10));
    }

    [Fact]
    public async Task Server_failure_keeps_the_queue_for_the_next_flush()
    {
        var analytics = CreateService();
        analytics.Track(FunnelEvents.SiteViewed, SiteViewedProps());
        _api.AnalyticsHandler = _ => throw new HttpRequestException("boom");

        await analytics.FlushAsync();

        Assert.Single(await _store.PeekAnalyticsEventsAsync(10)); // nothing lost

        _api.AnalyticsHandler = r => new Trimix.Contracts.Dtos.AnalyticsBatchResponse(r.Events.Count);
        await analytics.FlushAsync();
        Assert.Empty(await _store.PeekAnalyticsEventsAsync(10));
    }

    [Fact]
    public async Task Flush_batches_by_configured_size()
    {
        var analytics = CreateService(batchSize: 2);
        for (var i = 0; i < 5; i++)
        {
            analytics.Track(FunnelEvents.SiteViewed, SiteViewedProps());
        }

        await analytics.FlushAsync();

        Assert.Equal(new[] { 2, 2, 1 }, _api.AnalyticsBatches.Select(b => b.Events.Count));
    }

    [Fact]
    public async Task Install_fires_exactly_once_per_install()
    {
        var analytics = CreateService();
        await analytics.EnsureInstallTrackedAsync(AnalyticsDefaults.InstallSourceOrganic);
        await analytics.EnsureInstallTrackedAsync(AnalyticsDefaults.InstallSourceOrganic);
        await analytics.FlushAsync();

        var batch = Assert.Single(_api.AnalyticsBatches);
        var install = Assert.Single(batch.Events);
        Assert.Equal(FunnelEvents.Install, install.Name);
        Assert.Equal(_appInfo.Platform, install.Props[FunnelEvents.Props.Platform]);
        Assert.Equal(AnalyticsDefaults.InstallSourceOrganic, install.Props[FunnelEvents.Props.Source]);
        Assert.Equal(_appInfo.AppVersion, install.Props[FunnelEvents.Props.AppVersion]);
    }

    [Fact]
    public async Task First_run_completed_fires_once_with_pseudo_id()
    {
        var analytics = CreateService();
        await analytics.EnsureFirstRunCompletedTrackedAsync();
        await analytics.EnsureFirstRunCompletedTrackedAsync();
        await analytics.FlushAsync();

        var batch = Assert.Single(_api.AnalyticsBatches);
        var evt = Assert.Single(batch.Events);
        Assert.Equal(FunnelEvents.FirstRunCompleted, evt.Name);
        Assert.Equal(analytics.UserPseudoId, evt.Props[FunnelEvents.Props.UserPseudoId]);
    }

    [Fact]
    public void User_pseudo_id_is_stable_across_service_instances()
    {
        var first = CreateService().UserPseudoId;
        var second = CreateService().UserPseudoId; // same preferences → same id
        Assert.Equal(first, second);
    }
}
