using System.Net;
using System.Net.Http.Json;
using Trimix.Api.Data;
using Trimix.Api.Sites;
using Trimix.Contracts;
using Trimix.Contracts.Dtos;
using Xunit;

namespace Trimix.Api.Tests;

/// <summary>
/// GET /sites (architecture overview §5): name-substring search, bounding-box prefilter +
/// haversine ranking, and the seeded famous-sites dataset.
/// </summary>
public sealed class SiteSearchTests : IDisposable
{
    private readonly TrimixApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private async Task<SiteSearchResponse> SearchAsync(string query)
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync($"{ApiRoutes.Sites.SearchFull}?{query}");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<SiteSearchResponse>();
        Assert.NotNull(body);
        return body;
    }

    [Fact]
    public void Seed_contains_roughly_fifty_famous_sites()
    {
        var count = _factory.WithDb(db => db.Sites.Count(s => s.Source == SiteSources.Seed));
        Assert.InRange(count, 50, 80);
    }

    [Fact]
    public async Task Name_substring_search_is_case_insensitive()
    {
        var result = await SearchAsync("query=thistle");
        var site = Assert.Single(result.Sites);
        Assert.Equal("SS Thistlegorm", site.Name);
        Assert.Null(site.DistanceKm); // No geo in the query — no distance.

        var multi = await SearchAsync("query=Blue Hole");
        Assert.Contains(multi.Sites, s => s.Name == "Blue Hole Dahab");
        Assert.Contains(multi.Sites, s => s.Name == "Blue Hole Gozo");
        Assert.Contains(multi.Sites, s => s.Name == "Great Blue Hole");
    }

    [Fact]
    public async Task Geo_search_ranks_by_haversine_distance_within_the_bounding_box()
    {
        // Synthetic sites at controlled coordinates in the Gulf of Guinea (no seeds nearby).
        _factory.MutateDb(db => db.Sites.AddRange(
            new Site { Id = Guid.NewGuid(), Name = "GeoTest Near", Lat = 10.0, Lng = 10.0 },
            new Site { Id = Guid.NewGuid(), Name = "GeoTest Mid", Lat = 10.5, Lng = 10.0 },
            new Site { Id = Guid.NewGuid(), Name = "GeoTest Far", Lat = 14.0, Lng = 10.0 },
            new Site { Id = Guid.NewGuid(), Name = "GeoTest OutsideBox", Lat = 25.0, Lng = 10.0 }));

        var result = await SearchAsync("lat=10.0&lng=10.0&limit=10");

        Assert.Equal(
            new[] { "GeoTest Near", "GeoTest Mid", "GeoTest Far" },
            result.Sites.Select(s => s.Name).ToArray()); // OutsideBox (15° away) is prefiltered out.

        // Haversine correctness: 0.5° and 4.0° of latitude ≈ 55.6 km and 444.8 km.
        Assert.NotNull(result.Sites[0].DistanceKm);
        Assert.Equal(0.0, result.Sites[0].DistanceKm!.Value, 3);
        Assert.Equal(55.6, result.Sites[1].DistanceKm!.Value, 0);
        Assert.Equal(444.8, result.Sites[2].DistanceKm!.Value, 0);
        Assert.True(result.Sites.Select(s => s.DistanceKm!.Value).SequenceEqual(
            result.Sites.Select(s => s.DistanceKm!.Value).OrderBy(d => d)));
    }

    [Fact]
    public async Task Geo_search_finds_the_expected_famous_site_first()
    {
        // Just off Sharm El Sheikh: Shark and Yolanda Reef must outrank SS Thistlegorm.
        var result = await SearchAsync("lat=27.72&lng=34.25&limit=5");
        Assert.True(result.Sites.Count >= 2);
        Assert.Equal("Shark and Yolanda Reef", result.Sites[0].Name);
        var thistlegorm = result.Sites.Single(s => s.Name == "SS Thistlegorm");
        Assert.True(thistlegorm.DistanceKm > result.Sites[0].DistanceKm);
    }

    [Fact]
    public async Task Name_and_geo_combine_and_limit_is_respected()
    {
        var limited = await SearchAsync("query=a&limit=3");
        Assert.Equal(3, limited.Sites.Count);

        var combined = await SearchAsync("query=blue hole&lat=28.5&lng=34.5&limit=10");
        Assert.Equal("Blue Hole Dahab", combined.Sites[0].Name); // Dahab is inside the box; Gozo/Belize are not.
        Assert.DoesNotContain(combined.Sites, s => s.Name == "Great Blue Hole");
    }

    [Fact]
    public async Task Site_detail_returns_full_factual_fields_and_404_for_unknown()
    {
        var thistlegormId = SiteSeed.IdFor("SS Thistlegorm");
        var client = _factory.CreateClient();

        var response = await client.GetAsync(ApiRoutes.Sites.ByIdFull(thistlegormId));
        response.EnsureSuccessStatusCode();
        var detail = await response.Content.ReadFromJsonAsync<SiteDetailDto>();
        Assert.NotNull(detail);
        Assert.Equal("SS Thistlegorm", detail.Name);
        Assert.Equal("wreck", detail.Type);
        Assert.NotEmpty(detail.Hazards);
        Assert.NotEmpty(detail.MarineLife);
        Assert.False(string.IsNullOrWhiteSpace(detail.AccessNotes));
        Assert.Empty(detail.CommunityNotes);

        var missing = await client.GetAsync(ApiRoutes.Sites.ByIdFull(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }
}
