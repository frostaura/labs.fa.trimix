using System.Net;
using System.Net.Http.Json;
using Trimix.Api.Data;
using Trimix.Contracts;
using Trimix.Contracts.Dtos;
using Xunit;

namespace Trimix.Api.Tests;

/// <summary>
/// Site contributions and moderation: the server-authoritative Pro/Lifetime gate (§9), the
/// moderator-only queue, approve applying the payload to the site, reject leaving it untouched,
/// and <c>site_contribution_resolved</c> emitted on both outcomes.
/// </summary>
public sealed class ContributionModerationTests : IDisposable
{
    private readonly TrimixApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private async Task<(HttpClient Client, AuthResponse Auth)> ProUserAsync(string email, string? pseudoId = null)
    {
        var (client, auth) = await _factory.RegisterAsync(email, pseudoId);
        var token = SandboxReceiptCodec.Encode(
            new SandboxReceiptPayload(ReceiptPlatforms.Sandbox, Skus.Pro, $"purchase-{email}", DateTime.UtcNow),
            TrimixApiFactory.SandboxSecret);
        (await client.ValidatePurchaseAsync(ReceiptPlatforms.Sandbox, Skus.Pro, token)).EnsureSuccessStatusCode();
        return (client, auth);
    }

    [Fact]
    public async Task Free_users_cannot_contribute_pro_users_can()
    {
        var siteId = SiteSeed.IdFor("Blue Corner");
        var payload = """{"communityNote":"Hook in early — the current builds fast."}""";

        var (freeUser, _) = await _factory.RegisterAsync("free@example.com");
        var denied = await freeUser.PostAsJsonAsync(
            ApiRoutes.Sites.ContributionsFull, new SiteContributionRequest(siteId, payload));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        var anonymous = _factory.CreateClient();
        var unauthorized = await anonymous.PostAsJsonAsync(
            ApiRoutes.Sites.ContributionsFull, new SiteContributionRequest(siteId, payload));
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        var (proUser, _) = await ProUserAsync("pro@example.com");
        var accepted = await proUser.PostAsJsonAsync(
            ApiRoutes.Sites.ContributionsFull, new SiteContributionRequest(siteId, payload));
        accepted.EnsureSuccessStatusCode();
        var contribution = await accepted.Content.ReadFromJsonAsync<SiteContributionResponse>();
        Assert.NotNull(contribution);
        Assert.Equal(ContributionStatus.Pending, contribution.Status);
    }

    [Fact]
    public async Task Moderation_queue_is_moderator_only()
    {
        var (regularUser, _) = await _factory.RegisterAsync("nosy@example.com");
        var forbidden = await regularUser.GetAsync(ApiRoutes.Moderation.ContributionsFull + "?status=pending");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var anonymous = _factory.CreateClient();
        var unauthorized = await anonymous.GetAsync(ApiRoutes.Moderation.ContributionsFull + "?status=pending");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        var moderator = await _factory.ModeratorClientAsync();
        var ok = await moderator.GetAsync(ApiRoutes.Moderation.ContributionsFull + "?status=pending");
        ok.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Approving_applies_the_payload_to_the_site_and_surfaces_the_community_note()
    {
        var siteId = SiteSeed.IdFor("USAT Liberty");
        var payload = """
            {"depthMinM":4.0,"depthMaxM":29.0,"communityNote":"Go at dawn for the bumphead parade."}
            """;

        var (proUser, _) = await ProUserAsync("contributor@example.com", "pseudo-contributor");
        var submit = await proUser.PostAsJsonAsync(
            ApiRoutes.Sites.ContributionsFull, new SiteContributionRequest(siteId, payload));
        submit.EnsureSuccessStatusCode();
        var contribution = await submit.Content.ReadFromJsonAsync<SiteContributionResponse>();
        Assert.NotNull(contribution);

        // Moderator sees it queued.
        var moderator = await _factory.ModeratorClientAsync();
        var queueResponse = await moderator.GetAsync(ApiRoutes.Moderation.ContributionsFull + "?status=pending");
        queueResponse.EnsureSuccessStatusCode();
        var queue = await queueResponse.Content.ReadFromJsonAsync<ModerationQueueResponse>();
        Assert.NotNull(queue);
        Assert.Contains(queue.Contributions, c => c.Id == contribution.Id);

        // Approve.
        var resolve = await moderator.PostAsJsonAsync(
            ApiRoutes.Moderation.ResolveFull(contribution.Id), new ResolveContributionRequest(true, "Verified."));
        resolve.EnsureSuccessStatusCode();
        var resolved = await resolve.Content.ReadFromJsonAsync<ResolveContributionResponse>();
        Assert.NotNull(resolved);
        Assert.Equal(ContributionStatus.Approved, resolved.Status);

        // Payload applied to the site; community note visible on detail.
        var detailResponse = await _factory.CreateClient().GetAsync(ApiRoutes.Sites.ByIdFull(siteId));
        detailResponse.EnsureSuccessStatusCode();
        var detail = await detailResponse.Content.ReadFromJsonAsync<SiteDetailDto>();
        Assert.NotNull(detail);
        Assert.Equal(4.0, detail.DepthMinM);
        Assert.Equal(29.0, detail.DepthMaxM);
        var note = Assert.Single(detail.CommunityNotes);
        Assert.Equal("Go at dawn for the bumphead parade.", note.Note);

        // Both outcomes emit site_contribution_resolved — approved here.
        var names = _factory.EventNamesFor("pseudo-contributor");
        Assert.Contains(FunnelEvents.SiteContributionResolved, names);

        // Re-resolving is a conflict (already resolved).
        var again = await moderator.PostAsJsonAsync(
            ApiRoutes.Moderation.ResolveFull(contribution.Id), new ResolveContributionRequest(false, null));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Rejecting_leaves_the_site_untouched_and_still_emits_resolution()
    {
        var siteId = SiteSeed.IdFor("SS Yongala");
        var originalName = "SS Yongala";
        var payload = """{"name":"Renamed By Troll","communityNote":"spam"}""";

        var (proUser, _) = await ProUserAsync("troll@example.com", "pseudo-troll");
        var submit = await proUser.PostAsJsonAsync(
            ApiRoutes.Sites.ContributionsFull, new SiteContributionRequest(siteId, payload));
        submit.EnsureSuccessStatusCode();
        var contribution = await submit.Content.ReadFromJsonAsync<SiteContributionResponse>();
        Assert.NotNull(contribution);

        var moderator = await _factory.ModeratorClientAsync();
        var resolve = await moderator.PostAsJsonAsync(
            ApiRoutes.Moderation.ResolveFull(contribution.Id), new ResolveContributionRequest(false, "Not factual."));
        resolve.EnsureSuccessStatusCode();
        var resolved = await resolve.Content.ReadFromJsonAsync<ResolveContributionResponse>();
        Assert.NotNull(resolved);
        Assert.Equal(ContributionStatus.Rejected, resolved.Status);

        var name = _factory.WithDb(db => db.Sites.Single(s => s.Id == siteId).Name);
        Assert.Equal(originalName, name);

        var detail = await (await _factory.CreateClient().GetAsync(ApiRoutes.Sites.ByIdFull(siteId)))
            .Content.ReadFromJsonAsync<SiteDetailDto>();
        Assert.NotNull(detail);
        Assert.Empty(detail.CommunityNotes); // Rejected notes never surface.

        Assert.Contains(FunnelEvents.SiteContributionResolved, _factory.EventNamesFor("pseudo-troll"));
    }

    [Fact]
    public async Task Approving_a_new_site_proposal_creates_the_site()
    {
        var payload = """
            {"name":"GeoTest Proposed Pinnacle","lat":-9.4,"lng":46.2,"depthMinM":12.0,"depthMaxM":35.0,
             "type":"pinnacle","communityNote":"Local secret."}
            """;

        var (proUser, _) = await ProUserAsync("pioneer@example.com");
        var submit = await proUser.PostAsJsonAsync(
            ApiRoutes.Sites.ContributionsFull, new SiteContributionRequest(null, payload));
        submit.EnsureSuccessStatusCode();
        var contribution = await submit.Content.ReadFromJsonAsync<SiteContributionResponse>();
        Assert.NotNull(contribution);

        var moderator = await _factory.ModeratorClientAsync();
        var resolve = await moderator.PostAsJsonAsync(
            ApiRoutes.Moderation.ResolveFull(contribution.Id), new ResolveContributionRequest(true, null));
        resolve.EnsureSuccessStatusCode();

        var site = _factory.WithDb(db => db.Sites.Single(s => s.Name == "GeoTest Proposed Pinnacle"));
        Assert.Equal(SiteSources.Contributed, site.Source);
        Assert.Equal(SiteStatuses.Live, site.Status);
        Assert.Equal(-9.4, site.Lat);
        Assert.Equal(46.2, site.Lng);
    }
}
