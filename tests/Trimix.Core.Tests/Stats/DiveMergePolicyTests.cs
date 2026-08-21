using Trimix.Core.Stats;
using Xunit;
using static Trimix.Core.Tests.Stats.StatsTestFactory;

namespace Trimix.Core.Tests.Stats;

/// <summary>Cross-source identity + presentation-layer union: tolerances, non-destructive merge, richest-per-field.</summary>
public class DiveMergePolicyTests
{
    private static readonly DateTime T0 = new(2026, 3, 10, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Overlap_padding_constants_match_the_decided_values()
    {
        Assert.Equal(0.30, DiveMergePolicy.OverlapPadFraction);
        Assert.Equal(120, DiveMergePolicy.OverlapPadFloorSeconds);
        Assert.Equal(600, DiveMergePolicy.OverlapPadCeilingSeconds);
    }

    // ---- IsSamePhysicalDive: time-interval overlap with duration-scaled padding ----

    [Fact]
    public void Heavily_overlapping_windows_are_the_same_dive()
    {
        // Two computers on one ~50 min dive, one started five minutes after the other — windows overlap hugely.
        var a = Dive(start: T0, durationSec: 3000, maxDepthM: 30);
        var b = Dive(start: T0.AddMinutes(5), durationSec: 3000, maxDepthM: 18); // depth differs — irrelevant now
        Assert.True(DiveMergePolicy.IsSamePhysicalDive(a, b));
    }

    [Fact]
    public void Non_overlapping_windows_beyond_the_pad_are_distinct()
    {
        // Two 50 min dives (pad = clamp(0.30·3000, 120, 600) = 600 s). A ends at +3000; B starts 700 s later,
        // beyond the pad, so the windows don't bridge — a separate (repetitive) dive.
        var a = Dive(start: T0, durationSec: 3000, maxDepthM: 30);
        var b = Dive(start: T0.AddSeconds(3000 + 700), durationSec: 3000, maxDepthM: 30);
        Assert.False(DiveMergePolicy.IsSamePhysicalDive(a, b));
    }

    [Fact]
    public void A_surface_interval_inside_the_pad_still_bridges()
    {
        // Same 50 min dives, but B starts only 500 s after A ends — inside the 600 s pad, so they merge.
        var a = Dive(start: T0, durationSec: 3000, maxDepthM: 30);
        var b = Dive(start: T0.AddSeconds(3000 + 500), durationSec: 3000, maxDepthM: 30);
        Assert.True(DiveMergePolicy.IsSamePhysicalDive(a, b));
    }

    [Fact]
    public void Pad_scales_down_for_short_dives_via_the_geometric_mean()
    {
        // Two 100 s bounce dives → pad = clamp(0.30·√(100·100)=30, floor 120, …) = 120 s (the floor).
        var a = Dive(start: T0, durationSec: 100, maxDepthM: 6);
        var bridged = Dive(start: T0.AddSeconds(100 + 100), durationSec: 100, maxDepthM: 6); // gap 100 < 120
        var separate = Dive(start: T0.AddSeconds(100 + 150), durationSec: 100, maxDepthM: 6); // gap 150 > 120
        Assert.True(DiveMergePolicy.IsSamePhysicalDive(a, bridged));
        Assert.False(DiveMergePolicy.IsSamePhysicalDive(a, separate));
    }

    [Fact]
    public void Pad_is_capped_so_very_long_dives_do_not_bridge_a_long_surface_interval()
    {
        // Very long dives would scale past the ceiling; pad is clamped to 600 s, so a 700 s gap stays distinct.
        var a = Dive(start: T0, durationSec: 10000, maxDepthM: 40);
        var b = Dive(start: T0.AddSeconds(10000 + 700), durationSec: 10000, maxDepthM: 40);
        Assert.False(DiveMergePolicy.IsSamePhysicalDive(a, b));
    }

    [Fact]
    public void Match_is_symmetric()
    {
        var a = Dive(start: T0, durationSec: 3000, maxDepthM: 30);
        var b = Dive(start: T0.AddSeconds(3000 + 500), durationSec: 3200, maxDepthM: 31);
        Assert.Equal(DiveMergePolicy.IsSamePhysicalDive(a, b), DiveMergePolicy.IsSamePhysicalDive(b, a));
    }

    // ---- FidelityScore ----

    [Fact]
    public void Full_profile_dive_outranks_depth_temp_only_dive()
    {
        var full = Dive(gasMix: "air", cylinders: new[] { Cyl(o2: 21, startBar: 200, endBar: 80) }, samples: new[]
        {
            Sample(0, 0, temp: 20, ndl: 99, tank: 200, ceiling: 0),
            Sample(60, 30, temp: 18, ndl: 5, tank: 150, ceiling: 3),
        });
        var depthTemp = Dive(cylinders: null, samples: new[]
        {
            Sample(0, 0, temp: 20),
            Sample(60, 30, temp: 18),
        });

        Assert.True(DiveMergePolicy.FidelityScore(full) > DiveMergePolicy.FidelityScore(depthTemp));
    }

    // ---- Merge: clustering + union ----

    [Fact]
    public void Two_sources_of_one_physical_dive_collapse_to_a_single_badged_entry()
    {
        var shearId = Guid.NewGuid();
        var shear = Dive(
            id: shearId, start: T0, durationSec: 3000, maxDepthM: 30.0,
            source: "import", sourceVendor: "shearwater", gasMix: "air",
            cylinders: new[] { Cyl(o2: 21, startBar: 200, endBar: 90, volumeL: 12) },
            samples: new[] { Sample(0, 0, temp: 20, ndl: 99, tank: 200), Sample(60, 30, temp: 18, ndl: 5, tank: 150) });
        var watch = Dive(
            id: Guid.NewGuid(), start: T0.AddSeconds(30), durationSec: 3010, maxDepthM: 30.4,
            source: "import", sourceVendor: "apple_health", notes: "from watch",
            samples: new[] { Sample(0, 0, temp: 21), Sample(60, 30, temp: 19) });

        var merged = DiveMergePolicy.Merge(new[] { shear, watch });

        var one = Assert.Single(merged);
        Assert.Equal(shearId, one.Primary.Id);                  // richest source's real id (detail nav resolves)
        Assert.Equal(2, one.Sources.Count);                     // underlying rows retained, non-destructive
        Assert.Equal(shear.ProfileSamples.Count, one.Primary.ProfileSamples.Count); // samples wholesale from top
        Assert.NotNull(one.Primary.Cylinders);
        Assert.Equal("air", one.Primary.GasMix);                // top's non-null scalar wins
        Assert.Equal("from watch", one.Primary.Notes);          // filled from the lower-fidelity source (non-null beats null)
        Assert.Equal(new[] { "Shearwater", "Apple Watch" }, one.Badges.Select(b => b.Label));
    }

    [Fact]
    public void Distinct_physical_dives_stay_separate()
    {
        var a = Dive(start: T0, durationSec: 3000, maxDepthM: 30);
        var b = Dive(start: T0.AddHours(2), durationSec: 2400, maxDepthM: 18);
        var merged = DiveMergePolicy.Merge(new[] { a, b });
        Assert.Equal(2, merged.Count);
        Assert.All(merged, m => Assert.Single(m.Sources));
    }

    [Fact]
    public void Clustering_is_independent_of_input_order()
    {
        var shear = Dive(start: T0, sourceVendor: "shearwater", maxDepthM: 30, durationSec: 3000);
        var watch = Dive(start: T0.AddSeconds(20), sourceVendor: "apple_health", maxDepthM: 30.3, durationSec: 3020);
        var other = Dive(start: T0.AddHours(5), sourceVendor: "garmin_fit", maxDepthM: 12, durationSec: 1800);

        var forward = DiveMergePolicy.Merge(new[] { shear, watch, other });
        var reversed = DiveMergePolicy.Merge(new[] { other, watch, shear });

        Assert.Equal(2, forward.Count);
        Assert.Equal(forward.Count, reversed.Count);
        Assert.Equal(2, forward[0].Sources.Count); // the shear+watch cluster
        Assert.Single(forward[1].Sources);
    }

    [Fact]
    public void Anchor_matching_does_not_transitively_chain()
    {
        // Short 100 s dives → pad = 120 s (the floor). A=[0,100], B=[150,250], C=[230,330]:
        // A–B overlap (padded), B–C overlap (padded), but A–C do NOT (C starts 230 s after A's start, and
        // 230 > endA(100)+120). Matching against the fixed anchor A keeps C out of A's cluster.
        var a = Dive(start: T0, durationSec: 100, maxDepthM: 30, sourceVendor: "shearwater");
        var b = Dive(start: T0.AddSeconds(150), durationSec: 100, maxDepthM: 30, sourceVendor: "apple_health");
        var c = Dive(start: T0.AddSeconds(230), durationSec: 100, maxDepthM: 30, sourceVendor: "garmin_fit");

        var merged = DiveMergePolicy.Merge(new[] { a, b, c });

        Assert.Equal(2, merged.Count);
        Assert.Equal(2, merged[0].Sources.Count); // {A, B}
        Assert.Single(merged[1].Sources);         // {C}
    }

    [Fact]
    public void Custom_fidelity_delegate_overrides_the_primary_and_samples_source()
    {
        var shear = Dive(id: Guid.NewGuid(), start: T0, sourceVendor: "shearwater", maxDepthM: 30,
            samples: new[] { Sample(0, 0, temp: 20, ndl: 99), Sample(60, 30, temp: 18, ndl: 5) });
        var watchId = Guid.NewGuid();
        var watch = Dive(id: watchId, start: T0.AddSeconds(10), sourceVendor: "apple_health", maxDepthM: 30.2,
            samples: new[] { Sample(0, 0, temp: 21) });

        // Force apple_health to rank highest.
        var merged = DiveMergePolicy.Merge(
            new[] { shear, watch },
            fidelity: d => d.SourceVendor == "apple_health" ? 100 : 0);

        var one = Assert.Single(merged);
        Assert.Equal(watchId, one.Primary.Id);
        Assert.Equal(watch.ProfileSamples.Count, one.Primary.ProfileSamples.Count);
    }

    [Fact]
    public void Duplicate_source_and_vendor_produce_only_one_badge()
    {
        var a = Dive(start: T0, sourceVendor: "shearwater", maxDepthM: 30, durationSec: 3000);
        var b = Dive(start: T0.AddSeconds(10), sourceVendor: "shearwater", maxDepthM: 30, durationSec: 3005);
        var merged = Assert.Single(DiveMergePolicy.Merge(new[] { a, b }));
        Assert.Equal(2, merged.Sources.Count);
        Assert.Single(merged.Badges);
    }

    // ---- Badge labels ----

    [Theory]
    [InlineData("shearwater", "import", "Shearwater")]
    [InlineData("apple_health", "import", "Apple Watch")]
    [InlineData("garmin_fit", "import", "Garmin")]
    [InlineData("suunto_json", "import", "Suunto")]
    [InlineData("beta_vendor", "import", "Beta Vendor")] // unknown vendor → title-cased
    public void Badge_label_maps_known_vendors_and_titlecases_unknowns(string vendor, string source, string expected)
    {
        Assert.Equal(expected, DiveMergePolicy.BadgeLabel(Dive(sourceVendor: vendor, source: source)));
    }

    [Fact]
    public void Badge_label_falls_back_to_source_when_vendor_is_null()
    {
        Assert.Equal("Manual", DiveMergePolicy.BadgeLabel(Dive(sourceVendor: null, source: "manual")));
        Assert.Equal("Import", DiveMergePolicy.BadgeLabel(Dive(sourceVendor: null, source: "import")));
    }

    [Fact]
    public void Empty_input_merges_to_nothing()
    {
        Assert.Empty(DiveMergePolicy.Merge([]));
    }
}
