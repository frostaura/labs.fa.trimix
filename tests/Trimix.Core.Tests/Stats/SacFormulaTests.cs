using Trimix.Core.Stats;
using Xunit;
using static Trimix.Core.Tests.Stats.StatsTestFactory;

namespace Trimix.Core.Tests.Stats;

/// <summary>Exact SAC/RMV arithmetic: the spec formula, its null-guards, and multi-cylinder aggregation.</summary>
public class SacFormulaTests
{
    [Theory]
    [InlineData(0, 1.0)]
    [InlineData(10, 2.0)]
    [InlineData(20, 3.0)]
    [InlineData(30, 4.0)]
    public void Average_ata_pressure_is_one_plus_depth_over_ten(double depthM, double expected)
    {
        Assert.Equal(expected, SacFormula.AverageAtaPressure(depthM), 6);
    }

    [Fact]
    public void Sac_matches_the_spec_formula()
    {
        // consumed = 120 bar; Pavg = 1 + 20/10 = 3 ATA; duration = 1200 s = 20 min → 120/(3·20) = 2.0 bar/min.
        var sac = SacFormula.Sac(Cyl(startBar: 220, endBar: 100), avgDepthM: 20, durationSec: 1200);
        Assert.Equal(2.0, sac!.Value, 6);
    }

    [Fact]
    public void Rmv_is_sac_times_cylinder_volume()
    {
        var rmv = SacFormula.Rmv(Cyl(startBar: 220, endBar: 100, volumeL: 10), avgDepthM: 20, durationSec: 1200);
        Assert.Equal(20.0, rmv!.Value, 6); // 2.0 bar/min × 10 L
    }

    [Fact]
    public void Sac_is_null_when_pressures_missing_or_non_decreasing_or_duration_nonpositive()
    {
        Assert.Null(SacFormula.Sac(Cyl(startBar: 200), avgDepthM: 20, durationSec: 1200));            // no end
        Assert.Null(SacFormula.Sac(Cyl(endBar: 100), avgDepthM: 20, durationSec: 1200));              // no start
        Assert.Null(SacFormula.Sac(Cyl(startBar: 100, endBar: 100), avgDepthM: 20, durationSec: 1200)); // no consumption
        Assert.Null(SacFormula.Sac(Cyl(startBar: 100, endBar: 200), avgDepthM: 20, durationSec: 1200)); // gained pressure
        Assert.Null(SacFormula.Sac(Cyl(startBar: 220, endBar: 100), avgDepthM: 20, durationSec: 0));   // no duration
    }

    [Fact]
    public void Rmv_is_null_without_volume_even_when_sac_exists()
    {
        Assert.NotNull(SacFormula.Sac(Cyl(startBar: 220, endBar: 100), 20, 1200));
        Assert.Null(SacFormula.Rmv(Cyl(startBar: 220, endBar: 100), 20, 1200));
        Assert.Null(SacFormula.Rmv(Cyl(startBar: 220, endBar: 100, volumeL: 0), 20, 1200)); // non-positive volume
    }

    [Fact]
    public void ForDive_averages_sac_and_sums_rmv_across_qualifying_cylinders()
    {
        // Cyl A: consumed 120, vol 10 → SAC 2.0, RMV 20.  Cyl B: consumed 60, vol 12 → SAC 1.0, RMV 12.
        var cylinders = new[]
        {
            Cyl(startBar: 220, endBar: 100, volumeL: 10),
            Cyl(startBar: 160, endBar: 100, volumeL: 12),
        };

        var (sac, rmv) = SacFormula.ForDive(cylinders, avgDepthM: 20, durationSec: 1200);

        Assert.Equal(1.5, sac!.Value, 6);   // mean(2.0, 1.0)
        Assert.Equal(32.0, rmv!.Value, 6);  // sum(20, 12)
    }

    [Fact]
    public void ForDive_skips_non_qualifying_cylinders()
    {
        var cylinders = new[]
        {
            Cyl(startBar: 220, endBar: 100, volumeL: 10), // qualifies: SAC 2.0, RMV 20
            Cyl(o2: 21),                                  // no pressures → skipped entirely
        };

        var (sac, rmv) = SacFormula.ForDive(cylinders, avgDepthM: 20, durationSec: 1200);

        Assert.Equal(2.0, sac!.Value, 6);
        Assert.Equal(20.0, rmv!.Value, 6);
    }

    [Fact]
    public void ForDive_returns_nulls_when_no_cylinder_qualifies()
    {
        var (sac, rmv) = SacFormula.ForDive(new[] { Cyl(o2: 32), Cyl() }, avgDepthM: 20, durationSec: 1200);
        Assert.Null(sac);
        Assert.Null(rmv);
    }

    [Fact]
    public void ForDive_returns_sac_but_null_rmv_when_volume_missing()
    {
        var (sac, rmv) = SacFormula.ForDive(new[] { Cyl(startBar: 220, endBar: 100) }, 20, 1200);
        Assert.Equal(2.0, sac!.Value, 6);
        Assert.Null(rmv);
    }
}
