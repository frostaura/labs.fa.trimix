using Trimix.Core.Stats;
using Xunit;
using static Trimix.Core.Tests.Stats.StatsTestFactory;

namespace Trimix.Core.Tests.Stats;

/// <summary>Cross-source identity + presentation-layer union: tolerances, non-destructive merge, richest-per-field.</summary>
public class DiveMergePolicyTests
{
    private static readonly DateTime T0 = new(2026, 3, 10, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Constants_match_the_decided_tolerances()
    {
        Assert.Equal(90, DiveMergePolicy.StartToleranceSeconds);
        Assert.Equal(90, DiveMergePolicy.DurationToleranceSeconds);
        Assert.Equal(1.5, DiveMergePolicy.MaxDepthToleranceMeters);
    }

    // ---- IsSamePhysicalDive: near-boundary behaviour on each axis ----

    [Theory]
    [InlineData(89, true)]
    [InlineData(90, true)]
    [InlineData(91, false)]
    public void Start_offset_is_bounded_at_ninety_seconds(int offsetSec, bool same)
    {
        var a = Dive(start: T0, durationSec: 3000, maxDepthM: 30);
        var b = Dive(start: T0.AddSeconds(offsetSec), durationSec: 3000, maxDepthM: 30);
        Assert.Equal(same, DiveMergePolicy.IsSamePhysicalDive(a, b));
    }

    [Theory]
    [InlineData(3089, true)]
    [InlineData(3090, true)]
    [InlineData(3091, false)]
    public void Duration_delta_is_bounded_at_ninety_seconds(int otherDuration, bool same)
    {
        var a = Dive(start: T0, durationSec: 3000, maxDepthM: 30);
        var b = Dive(start: T0, durationSec: otherDuration, maxDepthM: 30);
        Assert.Equal(same, DiveMergePolicy.IsSamePhysicalDive(a, b));
    }

    [Theory]
    [InlineData(31.4, true)]
    [InlineData(31.5, true)]
    [InlineData(31.6, false)]
    public void Max_depth_delta_is_bounded_at_one_and_a_half_metres(double otherDepth, bool same)
    {
        var a = Dive(start: T0, durationSec: 3000, maxDepthM: 30);
        var b = Dive(start: T0, durationSec: 3000, maxDepthM: otherDepth);
        Assert.Equal(same, DiveMergePolicy.IsSamePhysicalDive(a, b));
    }

    [Fact]
    public void Match_is_symmetric()
    {
        var a = Dive(start: T0, durationSec: 3000, maxDepthM: 30);
        var b = Dive(start: T0.AddSeconds(85), durationSec: 3040, maxDepthM: 31);
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
        // A–B within tolerance, B–C within tolerance, but A–C is 160 s apart (> 90 s).
        // Matching against the fixed anchor A keeps C out of A's cluster.
        var a = Dive(start: T0, durationSec: 3000, maxDepthM: 30, sourceVendor: "shearwater");
        var b = Dive(start: T0.AddSeconds(80), durationSec: 3000, maxDepthM: 30, sourceVendor: "apple_health");
        var c = Dive(start: T0.AddSeconds(160), durationSec: 3000, maxDepthM: 30, sourceVendor: "garmin_fit");

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
