using System.Net;
using System.Net.Http.Json;
using Trimix.Contracts;
using Trimix.Contracts.Dtos;
using Xunit;

namespace Trimix.Api.Tests;

/// <summary>
/// GDPR/CCPA self-service: GET /privacy/export returns the caller's account + dives; POST
/// /privacy/delete erases the caller's dives, analytics rows, contributions, and account server-side.
/// </summary>
public sealed class PrivacyTests : IDisposable
{
    private readonly TrimixApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private static DiveDto ADive() => new(
        Guid.NewGuid(), DateTime.UtcNow.AddHours(-2), 2400, 22.0, 14.0, 21.0, "Air",
        DiveSources.Manual, null, null, null, null, null, "export me", 4, DateTime.UtcNow, false,
        [new ProfileSampleDto(0, 0, 22.0, null)]);

    [Fact]
    public async Task Export_returns_the_callers_account_and_dives()
    {
        var (client, auth) = await _factory.RegisterAsync("export@example.com", "pseudo-export");
        (await client.PostAsJsonAsync(ApiRoutes.Dives.SyncFull, new DiveSyncRequest([ADive()], [], null)))
            .EnsureSuccessStatusCode();
        await client.PostEventsAsync("pseudo-export",
            (FunnelEvents.SiteViewed, new Dictionary<string, string> { [FunnelEvents.Props.SiteId] = Guid.NewGuid().ToString() }));

        var response = await client.GetAsync(ApiRoutes.Privacy.ExportFull);
        response.EnsureSuccessStatusCode();
        var export = await response.Content.ReadFromJsonAsync<PrivacyExportResponse>();
        Assert.NotNull(export);
        Assert.Equal(auth.User.Id, export.Account.Id);
        Assert.Equal("export@example.com", export.Account.Email);
        Assert.Equal(EntitlementTier.Free, export.EntitlementTier);
        Assert.Equal("export me", Assert.Single(export.Dives).Notes);
        Assert.True(export.AnalyticsEventCount >= 1);
    }

    [Fact]
    public async Task Export_requires_authentication()
    {
        var anonymous = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(ApiRoutes.Privacy.ExportFull)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymous.PostAsync(ApiRoutes.Privacy.DeleteFull, null)).StatusCode);
    }

    [Fact]
    public async Task Delete_erases_the_users_dives_analytics_and_account()
    {
        var (client, auth) = await _factory.RegisterAsync("erase@example.com", "pseudo-erase");
        var userId = auth.User.Id;
        (await client.PostAsJsonAsync(ApiRoutes.Dives.SyncFull, new DiveSyncRequest([ADive(), ADive()], [], null)))
            .EnsureSuccessStatusCode();
        await client.PostEventsAsync("pseudo-erase",
            (FunnelEvents.SiteViewed, new Dictionary<string, string> { [FunnelEvents.Props.SiteId] = Guid.NewGuid().ToString() }));

        var delete = await client.PostAsync(ApiRoutes.Privacy.DeleteFull, null);
        delete.EnsureSuccessStatusCode();
        var summary = await delete.Content.ReadFromJsonAsync<PrivacyDeleteResponse>();
        Assert.NotNull(summary);
        Assert.Equal(2, summary.DivesErased);

        // Server-side erasure: nothing for this user survives.
        Assert.Equal(0, _factory.WithDb(db => db.Dives.Count(d => d.UserId == userId)));
        Assert.Equal(0, _factory.WithDb(db => db.AnalyticsEvents.Count(e => e.UserId == userId)));
        Assert.Equal(0, _factory.WithDb(db => db.Users.Count(u => u.Id == userId)));
    }

    [Fact]
    public async Task Delete_also_erases_pre_auth_guest_analytics_keyed_only_by_pseudo_id()
    {
        const string pseudo = "pseudo-guest-erase";

        // 1) A pre-account (guest) funnel event: anonymous client, NO bearer → the row lands with UserId=null
        //    and only the device UserPseudoId. This is the bulk of the pre-activation funnel.
        var guest = _factory.CreateAnonymousClient(pseudo);
        await guest.PostEventsAsync(pseudo,
            (FunnelEvents.FirstRunCompleted, new Dictionary<string, string> { [FunnelEvents.Props.UserPseudoId] = pseudo }));
        Assert.Equal(1, _factory.WithDb(db => db.AnalyticsEvents.Count(e => e.UserPseudoId == pseudo && e.UserId == null)));

        // 2) The same device later creates an account (same pseudo-id) and logs an authenticated event.
        var (client, _) = await _factory.RegisterAsync("guest-erase@example.com", pseudo);
        await client.PostEventsAsync(pseudo,
            (FunnelEvents.SiteViewed, new Dictionary<string, string> { [FunnelEvents.Props.SiteId] = Guid.NewGuid().ToString() }));
        var totalForPseudo = _factory.WithDb(db => db.AnalyticsEvents.Count(e => e.UserPseudoId == pseudo));
        Assert.True(totalForPseudo >= 2);

        // 3) GDPR delete.
        var delete = await client.PostAsync(ApiRoutes.Privacy.DeleteFull, null);
        delete.EnsureSuccessStatusCode();
        var summary = await delete.Content.ReadFromJsonAsync<PrivacyDeleteResponse>();
        Assert.NotNull(summary);

        // Every row under the device pseudo-id is gone — INCLUDING the pre-auth guest row that carried
        // UserId==null (previously left behind), and the reported count is truthful (not just UserId-matched).
        Assert.Equal(0, _factory.WithDb(db => db.AnalyticsEvents.Count(e => e.UserPseudoId == pseudo)));
        Assert.Equal(totalForPseudo, summary.AnalyticsEventsErased);
    }
}
