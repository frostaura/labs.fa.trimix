using System.Net;
using System.Net.Http.Json;
using Trimix.Api.Data;
using Trimix.Contracts;
using Trimix.Contracts.Dtos;
using Xunit;

namespace Trimix.Api.Tests;

/// <summary>
/// Sites/contributions hardening: antimeridian-aware search, the contributor's own-contributions
/// read path, and the server-side payload size cap.
/// </summary>
public sealed class SitesHardeningTests : IDisposable
{
    private readonly TrimixApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private async Task<HttpClient> ProUserAsync(string email, string? pseudoId = null)
    {
        var (client, _) = await _factory.RegisterAsync(email, pseudoId);
        var token = SandboxReceiptCodec.Encode(
            new SandboxReceiptPayload(ReceiptPlatforms.Sandbox, Skus.Pro, $"purchase-{email}", DateTime.UtcNow),
            TrimixApiFactory.SandboxSecret);
        (await client.ValidatePurchaseAsync(ReceiptPlatforms.Sandbox, Skus.Pro, token)).EnsureSuccessStatusCode();
        return client;
    }

    [Fact]
    public async Task Search_across_the_antimeridian_finds_sites_on_both_sides()
    {
        // Fiji straddles 180°. Two sites a few km apart across the line, both stored in [-180, 180].
        var eastId = Guid.NewGuid();
        var westId = Guid.NewGuid();
        _factory.MutateDb(db => db.Sites.AddRange(
            new Site { Id = eastId, Name = "Antimeridian East", Lat = -16.8, Lng = 179.95 },
            new Site { Id = westId, Name = "Antimeridian West", Lat = -16.8, Lng = -179.95 }));

        var client = _factory.CreateClient();
        var response = await client.GetAsync($"{ApiRoutes.Sites.SearchFull}?lat=-16.8&lng=179.9&limit=50");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<SiteSearchResponse>();
        Assert.NotNull(body);

        Assert.Contains(body.Sites, s => s.Name == "Antimeridian East");
        Assert.Contains(body.Sites, s => s.Name == "Antimeridian West"); // regression: excluded by the box.
    }

    [Fact]
    public async Task A_user_can_read_their_own_contributions_and_resolution_note()
    {
        var siteId = SiteSeed.IdFor("Blue Corner");
        var pro = await ProUserAsync("mine@example.com");
        var submit = await pro.PostAsJsonAsync(
            ApiRoutes.Sites.ContributionsFull,
            new SiteContributionRequest(siteId, """{"communityNote":"Strong current on the corner."}"""));
        submit.EnsureSuccessStatusCode();
        var contribution = await submit.Content.ReadFromJsonAsync<SiteContributionResponse>();
        Assert.NotNull(contribution);

        // A moderator rejects it with a note.
        var moderator = await _factory.ModeratorClientAsync();
        (await moderator.PostAsJsonAsync(
            ApiRoutes.Moderation.ResolveFull(contribution.Id),
            new ResolveContributionRequest(false, "Duplicate of an existing note."))).EnsureSuccessStatusCode();

        // The contributor can now see the outcome + note via /sites/contributions/mine.
        var mineResponse = await pro.GetAsync(ApiRoutes.Sites.ContributionsMineFull);
        mineResponse.EnsureSuccessStatusCode();
        var mine = await mineResponse.Content.ReadFromJsonAsync<ModerationQueueResponse>();
        Assert.NotNull(mine);
        var row = Assert.Single(mine.Contributions);
        Assert.Equal(contribution.Id, row.Id);
        Assert.Equal(ContributionStatus.Rejected, row.Status);
        Assert.Equal("Duplicate of an existing note.", row.Note);
    }

    [Fact]
    public async Task Contributions_mine_requires_authentication()
    {
        var anonymous = _factory.CreateClient();
        var response = await anonymous.GetAsync(ApiRoutes.Sites.ContributionsMineFull);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Oversized_contribution_payload_is_rejected()
    {
        var siteId = SiteSeed.IdFor("Blue Corner");
        var pro = await ProUserAsync("huge@example.com");

        var giant = "{\"communityNote\":\"" + new string('x', AppDbContext.ContributionPayloadMaxLength + 100) + "\"}";
        var response = await pro.PostAsJsonAsync(
            ApiRoutes.Sites.ContributionsFull, new SiteContributionRequest(siteId, giant));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, _factory.WithDb(db => db.SiteContributions.Count()));
    }
}
