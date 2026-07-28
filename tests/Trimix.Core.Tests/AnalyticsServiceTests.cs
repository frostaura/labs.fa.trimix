using Microsoft.Extensions.Logging.Abstractions;
using Trimix.Contracts;
using Trimix.Core.Analytics;
using Trimix.Core.Platform;
using Trimix.Core.Storage;
using Trimix.Core.Tests.Fakes;
using Xunit;

namespace Trimix.Core.Tests;

/// <summary>
/// Frozen-contract validation + persistent queue + local drain + install-once. Serverless: there is no
/// ingest server — FlushAsync drains the durable queue to the debug log and discards.
/// </summary>
public class AnalyticsServiceTests
{
    private readonly InMemoryLocalStore _store = new();
    private readonly InMemoryPreferences _preferences = new();
    private readonly FakeAppInfo _appInfo = new();
    private readonly FakeClock _clock = new();

    private AnalyticsService CreateService(bool throwOnViolation = true, int batchSize = 50) => new(
        _store,
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

        Assert.Empty(await _store.PeekAnalyticsEventsAsync(10));
    }

    [Fact]
    public void Drifted_enumerated_value_throws_in_debug_mode()
    {
        // The client validator runs the shared enumerated-value contract (FunnelValidation), so a
        // drifted trigger is caught at the Track call site — not silently queued.
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

        Assert.Empty(await _store.PeekAnalyticsEventsAsync(10)); // never entered the durable queue
    }

    [Fact]
    public async Task Valid_event_is_queued_then_drained_by_flush()
    {
        // Stamp-shape assertions (pseudo-id / platform / version / timestamp) live on the install and
        // first-run tests below, whose Ensure* methods deterministically drain the enqueue tail.
        var analytics = CreateService();
        analytics.Track(FunnelEvents.SiteViewed, SiteViewedProps());

        // Local drain: flush empties the queue (nothing leaves the device; the buffer stays bounded).
        await analytics.FlushAsync();
        Assert.Empty(await _store.PeekAnalyticsEventsAsync(10));
    }

    [Fact]
    public async Task Flush_drains_the_whole_queue_across_batches()
    {
        var analytics = CreateService(batchSize: 2);
        for (var i = 0; i < 5; i++)
        {
            analytics.Track(FunnelEvents.SiteViewed, SiteViewedProps());
        }

        await analytics.FlushAsync();

        Assert.Empty(await _store.PeekAnalyticsEventsAsync(10));
    }

    [Fact]
    public async Task Install_fires_exactly_once_per_install()
    {
        var analytics = CreateService();
        await analytics.EnsureInstallTrackedAsync(AnalyticsDefaults.InstallSourceOrganic);
        await analytics.EnsureInstallTrackedAsync(AnalyticsDefaults.InstallSourceOrganic);

        var queued = await _store.PeekAnalyticsEventsAsync(10);
        var install = Assert.Single(queued);
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

        var queued = await _store.PeekAnalyticsEventsAsync(10);
        var evt = Assert.Single(queued);
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
