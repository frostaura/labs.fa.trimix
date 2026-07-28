using Trimix.Core.Sites;
using Xunit;

namespace Trimix.Core.Tests.Sites;

/// <summary>
/// The bundled read-only site atlas (serverless replacement for the server site DB): bounding-box +
/// haversine matching, stable deterministic ids, and the three-card carousel copy.
/// </summary>
public class SiteAtlasTests
{
    private readonly SiteAtlas _atlas = new();

    [Fact]
    public void Seed_carries_the_fourteen_prototype_sites()
    {
        Assert.Equal(14, _atlas.All.Count);
        Assert.All(_atlas.All, s =>
        {
            Assert.False(string.IsNullOrWhiteSpace(s.Name));
            Assert.False(string.IsNullOrWhiteSpace(s.Location));
            Assert.False(string.IsNullOrWhiteSpace(s.Fact));
        });
        Assert.Equal(_atlas.All.Count, _atlas.All.Select(s => s.Id).Distinct().Count());
    }

    [Fact]
    public void FindNearest_matches_within_the_radius()
    {
        // ~150 m off the Liberty Wreck seed point.
        var site = _atlas.FindNearest(-8.2750, 115.5940, radiusKm: 2.0);
        Assert.NotNull(site);
        Assert.Equal("Liberty Wreck", site!.Name);
    }

    [Fact]
    public void FindNearest_prefers_the_closest_of_two_nearby_sites()
    {
        // Between the two Tulamben sites but nearer Coral Garden.
        var site = _atlas.FindNearest(-8.2773, 115.5895, radiusKm: 2.0);
        Assert.Equal("Coral Garden", site!.Name);
    }

    [Fact]
    public void FindNearest_returns_null_when_nothing_is_in_range()
    {
        Assert.Null(_atlas.FindNearest(0.0, 0.0, radiusKm: 2.0)); // open Atlantic
    }

    [Fact]
    public void Ids_are_deterministic_across_instances()
    {
        var again = new SiteAtlas();
        Assert.Equal(
            _atlas.All.Select(s => s.Id),
            again.All.Select(s => s.Id));
    }

    [Fact]
    public void FindByName_is_case_insensitive()
    {
        Assert.NotNull(_atlas.FindByName("richelieu rock"));
        Assert.Null(_atlas.FindByName("Atlantis"));
    }

    [Fact]
    public void FactsFor_yields_the_three_carousel_cards()
    {
        var site = _atlas.FindByName("Manta Point")!;
        var facts = _atlas.FactsFor(site);
        Assert.Equal(3, facts.Count);
        Assert.Equal(site.Fact, facts[0]);
        Assert.StartsWith("Avg viz", facts[1], StringComparison.Ordinal);
        Assert.StartsWith("Community atlas", facts[2], StringComparison.Ordinal);
    }

    [Fact]
    public void Coordinates_label_uses_the_prototype_format()
    {
        var liberty = _atlas.FindByName("Liberty Wreck")!;
        Assert.Equal("8.274°S 115.593°E", SiteAtlas.CoordinatesLabel(liberty));
    }

    [Fact]
    public void Haversine_is_sane()
    {
        Assert.Equal(0, SiteAtlas.HaversineKm(10, 20, 10, 20), 6);
        // One degree of latitude ≈ 111 km.
        Assert.InRange(SiteAtlas.HaversineKm(0, 0, 1, 0), 110, 112);
    }
}
