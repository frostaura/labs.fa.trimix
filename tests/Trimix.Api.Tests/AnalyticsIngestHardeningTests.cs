using System.Net;
using System.Net.Http.Json;
using Trimix.Contracts;
using Trimix.Contracts.Dtos;
using Xunit;

namespace Trimix.Api.Tests;

/// <summary>
/// Ingest hardening: the unauthenticated endpoint must not accept server-reserved event names,
/// drifted enumerated values, and must dedup retried batches by (pseudoId, eventId) so the M3/M4
/// gate metrics can't be forged or double-counted.
/// </summary>
public sealed class AnalyticsIngestHardeningTests : IDisposable
{
    private readonly TrimixApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private static AnalyticsEventDto Event(
        string name, Dictionary<string, string> props, string pseudo = "pseudo-harden", Guid eventId = default) =>
        new(name, props, pseudo, DateTime.UtcNow, TestClient.TestPlatform, TestClient.TestAppVersion, eventId);

    private async Task<HttpResponseMessage> PostBatchAsync(params AnalyticsEventDto[] events)
    {
        var client = _factory.CreateClient();
        return await client.PostAsJsonAsync(ApiRoutes.Analytics.EventsFull, new AnalyticsBatchRequest(events));
    }

    [Theory]
    [InlineData(FunnelEvents.EntitlementGranted)]
    [InlineData(FunnelEvents.EntitlementRestored)]
    [InlineData(FunnelEvents.AccountCreated)]
    public async Task Server_reserved_event_names_are_rejected_from_clients(string reservedName)
    {
        // A well-formed payload for each reserved event — rejection is because of the NAME, not props.
        var props = reservedName switch
        {
            FunnelEvents.AccountCreated => new Dictionary<string, string> { [FunnelEvents.Props.AuthProvider] = "email" },
            FunnelEvents.EntitlementRestored => new Dictionary<string, string> { [FunnelEvents.Props.Tier] = "lifetime" },
            _ => new Dictionary<string, string>
            {
                [FunnelEvents.Props.Tier] = "lifetime",
                [FunnelEvents.Props.SourceReceiptId] = "forged",
            },
        };

        var response = await PostBatchAsync(Event(reservedName, props));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, _factory.WithDb(db => db.AnalyticsEvents.Count()));
    }

    [Fact]
    public async Task Drifted_enumerated_values_are_rejected()
    {
        var badTrigger = await PostBatchAsync(Event(FunnelEvents.PaywallShown, new Dictionary<string, string>
        {
            [FunnelEvents.Props.Trigger] = "settings_upsell", // not in the frozen set
            [FunnelEvents.Props.EntitlementState] = FunnelEvents.Values.EntitlementStateFree,
        }));
        Assert.Equal(HttpStatusCode.BadRequest, badTrigger.StatusCode);

        var badImportSource = await PostBatchAsync(Event(FunnelEvents.DiveLoggedImport, new Dictionary<string, string>
        {
            [FunnelEvents.Props.DiveId] = Guid.NewGuid().ToString(),
            [FunnelEvents.Props.Vendor] = "Shearwater",
            [FunnelEvents.Props.Source] = "bluetooth", // dive_logged_import.source is frozen to 'import'
        }));
        Assert.Equal(HttpStatusCode.BadRequest, badImportSource.StatusCode);

        // outcome is frozen to {granted,denied} on the TERMINAL ble-permission events (previously declared but
        // never enforced — a vacuous contract). A drifted outcome must now be rejected.
        var badOutcome = await PostBatchAsync(Event(FunnelEvents.BlePermissionGranted, new Dictionary<string, string>
        {
            [FunnelEvents.Props.Outcome] = "maybe", // not in {granted,denied}
        }));
        Assert.Equal(HttpStatusCode.BadRequest, badOutcome.StatusCode);

        Assert.Equal(0, _factory.WithDb(db => db.AnalyticsEvents.Count()));
    }

    [Fact]
    public async Task Ble_permission_requested_keeps_its_requested_lifecycle_outcome()
    {
        // outcome enforcement must NOT break ble_permission_requested's documented 'requested' lifecycle value.
        var response = await PostBatchAsync(Event(FunnelEvents.BlePermissionRequested, new Dictionary<string, string>
        {
            [FunnelEvents.Props.Outcome] = "requested",
        }));
        response.EnsureSuccessStatusCode();
        Assert.Equal(1, _factory.WithDb(db => db.AnalyticsEvents.Count()));
    }

    [Fact]
    public async Task Install_acquisition_source_is_free_text_and_still_accepted()
    {
        // 'source' is overloaded: install.source is a channel, NOT the frozen 'import' value.
        var response = await PostBatchAsync(Event(FunnelEvents.Install, new Dictionary<string, string>
        {
            [FunnelEvents.Props.Platform] = TestClient.TestPlatform,
            [FunnelEvents.Props.Source] = "organic",
            [FunnelEvents.Props.AppVersion] = TestClient.TestAppVersion,
        }));
        response.EnsureSuccessStatusCode();
        Assert.Equal(1, _factory.WithDb(db => db.AnalyticsEvents.Count()));
    }

    [Fact]
    public async Task Cloud_aggregator_events_and_trigger_validate_and_are_accepted()
    {
        // The additive cloud-aggregator taxonomy (PRD Amendments set 2, item 10): paywall_shown with the
        // new cloud_sync_cap trigger, plus source_connected/dives_synced whose 'source' is a free-text
        // connector id (NOT the frozen dive_logged_import 'import' value) — all must pass ingest.
        var response = await PostBatchAsync(
            Event(FunnelEvents.PaywallShown, new Dictionary<string, string>
            {
                [FunnelEvents.Props.Trigger] = FunnelEvents.Values.TriggerCloudSyncCap,
                [FunnelEvents.Props.EntitlementState] = FunnelEvents.Values.EntitlementStateFree,
            }, eventId: Guid.NewGuid()),
            Event(FunnelEvents.SourceConnected, new Dictionary<string, string>
            {
                [FunnelEvents.Props.Source] = "apple_health", // free-text connector id
                [FunnelEvents.Props.Platform] = TestClient.TestPlatform,
            }, eventId: Guid.NewGuid()),
            Event(FunnelEvents.DivesSynced, new Dictionary<string, string>
            {
                [FunnelEvents.Props.Source] = "garmin_cloud", // free-text connector id
                [FunnelEvents.Props.Count] = "7",
            }, eventId: Guid.NewGuid()));

        response.EnsureSuccessStatusCode();
        Assert.Equal(3, _factory.WithDb(db => db.AnalyticsEvents.Count()));
    }

    [Fact]
    public async Task Retried_batch_is_deduped_by_pseudo_and_event_id()
    {
        var eventId = Guid.NewGuid();
        AnalyticsEventDto MakeSiteView() => Event(
            FunnelEvents.SiteViewed,
            new Dictionary<string, string> { [FunnelEvents.Props.SiteId] = Guid.NewGuid().ToString() },
            eventId: eventId);

        var first = await PostBatchAsync(MakeSiteView());
        first.EnsureSuccessStatusCode();
        var firstBody = await first.Content.ReadFromJsonAsync<AnalyticsBatchResponse>();
        Assert.Equal(1, firstBody!.Accepted);

        // Same idempotency key resent (client retry) — stored once, reported as 0 newly accepted.
        var second = await PostBatchAsync(MakeSiteView());
        second.EnsureSuccessStatusCode();
        var secondBody = await second.Content.ReadFromJsonAsync<AnalyticsBatchResponse>();
        Assert.Equal(0, secondBody!.Accepted);

        Assert.Equal(1, _factory.WithDb(db => db.AnalyticsEvents.Count(e => e.EventId == eventId)));

        // Duplicate within a single batch is also collapsed.
        var withinBatch = await PostBatchAsync(MakeSiteView(), MakeSiteView());
        withinBatch.EnsureSuccessStatusCode();
        var withinBody = await withinBatch.Content.ReadFromJsonAsync<AnalyticsBatchResponse>();
        Assert.Equal(0, withinBody!.Accepted);
        Assert.Equal(1, _factory.WithDb(db => db.AnalyticsEvents.Count(e => e.EventId == eventId)));
    }
}
