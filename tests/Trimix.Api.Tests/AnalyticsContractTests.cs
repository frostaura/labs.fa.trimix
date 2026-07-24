using System.Net;
using System.Net.Http.Json;
using Trimix.Contracts;
using Trimix.Contracts.Dtos;
using Xunit;

namespace Trimix.Api.Tests;

/// <summary>
/// POST /analytics/events enforces the FROZEN funnel taxonomy: unknown names and missing required
/// props are rejected; valid batches are stored append-only; pre-auth pseudo-id ingest works.
/// </summary>
public sealed class AnalyticsContractTests : IDisposable
{
    private readonly TrimixApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private static AnalyticsEventDto Event(string name, Dictionary<string, string> props, string pseudo = "pseudo-analytics") =>
        new(name, props, pseudo, DateTime.UtcNow, TestClient.TestPlatform, TestClient.TestAppVersion);

    private async Task<HttpResponseMessage> PostBatchAsync(params AnalyticsEventDto[] events)
    {
        var client = _factory.CreateClient();
        return await client.PostAsJsonAsync(ApiRoutes.Analytics.EventsFull, new AnalyticsBatchRequest(events));
    }

    [Fact]
    public async Task Unknown_event_name_is_rejected()
    {
        var response = await PostBatchAsync(Event("definitely_not_in_the_taxonomy", []));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, _factory.WithDb(db => db.AnalyticsEvents.Count()));
    }

    [Fact]
    public async Task Missing_required_prop_rejects_the_batch_atomically()
    {
        // purchase_completed requires sku_id + gross_price + currency; gross_price/currency missing.
        var invalid = Event(FunnelEvents.PurchaseCompleted, new Dictionary<string, string>
        {
            [FunnelEvents.Props.SkuId] = Skus.Pro,
        });
        var valid = Event(FunnelEvents.FirstRunCompleted, new Dictionary<string, string>
        {
            [FunnelEvents.Props.UserPseudoId] = "pseudo-analytics",
        });

        var response = await PostBatchAsync(valid, invalid);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, _factory.WithDb(db => db.AnalyticsEvents.Count())); // Nothing from the batch landed.
    }

    [Fact]
    public async Task Missing_pseudo_id_is_rejected()
    {
        var response = await PostBatchAsync(Event(
            FunnelEvents.SiteViewed,
            new Dictionary<string, string> { [FunnelEvents.Props.SiteId] = Guid.NewGuid().ToString() },
            pseudo: ""));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Valid_batch_is_accepted_append_only_and_works_unauthenticated()
    {
        var response = await PostBatchAsync(
            Event(FunnelEvents.Install, new Dictionary<string, string>
            {
                [FunnelEvents.Props.Platform] = TestClient.TestPlatform,
                [FunnelEvents.Props.Source] = "organic",
                [FunnelEvents.Props.AppVersion] = TestClient.TestAppVersion,
            }),
            Event(FunnelEvents.PaywallShown, new Dictionary<string, string>
            {
                [FunnelEvents.Props.Trigger] = FunnelEvents.Values.TriggerBleImportCap,
                [FunnelEvents.Props.EntitlementState] = FunnelEvents.Values.EntitlementStateFree,
            }));

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<AnalyticsBatchResponse>();
        Assert.NotNull(body);
        Assert.Equal(2, body.Accepted);

        var rows = _factory.WithDb(db => db.AnalyticsEvents.OrderBy(e => e.Id).ToList());
        Assert.Equal(2, rows.Count);
        Assert.Equal(FunnelEvents.Install, rows[0].Name);
        Assert.Equal(FunnelEvents.PaywallShown, rows[1].Name);
        Assert.All(rows, r =>
        {
            Assert.Null(r.UserId); // Unauthenticated ingest: pseudo-id only.
            Assert.Equal("pseudo-analytics", r.UserPseudoId);
            Assert.Equal(TestClient.TestPlatform, r.Platform);
            Assert.False(string.IsNullOrWhiteSpace(r.PropsJson));
        });
    }

    [Fact]
    public async Task Authenticated_ingest_links_rows_to_the_account()
    {
        var (client, auth) = await _factory.RegisterAsync("tracked@example.com", "pseudo-tracked");
        await client.PostEventsAsync("pseudo-tracked",
            (FunnelEvents.SiteViewed, new Dictionary<string, string>
            {
                [FunnelEvents.Props.SiteId] = Guid.NewGuid().ToString(),
            }));

        var row = _factory.WithDb(db => db.AnalyticsEvents.Single(e => e.Name == FunnelEvents.SiteViewed));
        Assert.Equal(auth.User.Id, row.UserId);
    }
}
