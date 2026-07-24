using Trimix.Core.Stats;
using Xunit;
using static Trimix.Core.Tests.Stats.StatsTestFactory;

namespace Trimix.Core.Tests.Stats;

/// <summary>Null-tolerant career analytics: values, completeness fractions, and heterogeneous-fidelity edges.</summary>
public class DiveStatsTests
{
    private static DateTime Day(int year, int month, int day) => new(year, month, day, 10, 0, 0, DateTimeKind.Utc);

    // ---- Summarize ----

    [Fact]
    public void Summarize_reports_count_max_depth_and_total_bottom_time()
    {
        var summary = DiveStats.Summarize(new[]
        {
            Dive(durationSec: 1800, maxDepthM: 18),
            Dive(durationSec: 2400, maxDepthM: 42),
            Dive(durationSec: 1200, maxDepthM: 30),
        });

        Assert.Equal(3, summary.TotalDives);
        Assert.Equal(42, summary.MaxDepthM);
        Assert.Equal(5400, summary.TotalBottomTimeSec);
    }

    [Fact]
    public void Summarize_of_empty_is_the_empty_career()
    {
        Assert.Equal(CareerSummary.Empty, DiveStats.Summarize([]));
    }

    // ---- DepthHistogram ----

    [Fact]
    public void Depth_histogram_bins_by_max_depth_and_zero_fills_interior_bands()
    {
        var result = DiveStats.DepthHistogram(new[]
        {
            Dive(maxDepthM: 3),
            Dive(maxDepthM: 8),
            Dive(maxDepthM: 12),
            Dive(maxDepthM: 12),
        }, binM: 5);

        Assert.True(result.HasData);
        Assert.Equal(1.0, result.Completeness.Fraction, 6);
        Assert.Equal(3, result.Value.Count); // [0,5) [5,10) [10,15)
        Assert.Equal(1, result.Value[0].Count);
        Assert.Equal(1, result.Value[1].Count);
        Assert.Equal(2, result.Value[2].Count);
        Assert.Equal(10, result.Value[2].LowerM);
        Assert.Equal(15, result.Value[2].UpperM);
    }

    [Fact]
    public void Depth_histogram_of_empty_has_no_data()
    {
        var result = DiveStats.DepthHistogram([]);
        Assert.False(result.HasData);
        Assert.Empty(result.Value);
    }

    // ---- TimeAtDepth ----

    [Fact]
    public void Time_at_depth_sums_sample_intervals_per_band_and_ignores_diveless_samples()
    {
        var withSamples = Dive(samples: new[]
        {
            Sample(0, 10),
            Sample(60, 10),
            Sample(120, 20),
        });
        var noSamples = Dive(samples: []);

        var result = DiveStats.TimeAtDepth(new[] { withSamples, noSamples }, binM: 5);

        Assert.True(result.HasData);
        Assert.Equal(1, result.Completeness.Available); // only the dive with samples fed it
        Assert.Equal(2, result.Completeness.Total);
        // Interval [0→60] at depth 10 and [60→120] at depth 10 → band [10,15) = 120 s.
        var band = Assert.Single(result.Value, b => b.LowerM == 10);
        Assert.Equal(120, band.Seconds, 6);
    }

    [Fact]
    public void Time_at_depth_with_no_samples_anywhere_has_no_data()
    {
        var result = DiveStats.TimeAtDepth(new[] { Dive(samples: []), Dive(samples: []) });
        Assert.False(result.HasData);
        Assert.Equal(0, result.Completeness.Available);
        Assert.Equal(2, result.Completeness.Total);
    }

    // ---- DivesByMonth / PerYear ----

    [Fact]
    public void Dives_by_month_groups_and_orders_chronologically()
    {
        var months = DiveStats.DivesByMonth(new[]
        {
            Dive(start: Day(2026, 3, 2)),
            Dive(start: Day(2026, 1, 5)),
            Dive(start: Day(2026, 1, 20)),
        });

        Assert.Equal(2, months.Count);
        Assert.Equal(new MonthCount(2026, 1, 2), months[0]);
        Assert.Equal(new MonthCount(2026, 3, 1), months[1]);
    }

    [Fact]
    public void Per_year_rolls_up_count_bottom_time_and_max_depth()
    {
        var years = DiveStats.PerYear(new[]
        {
            Dive(start: Day(2025, 6, 1), durationSec: 1000, maxDepthM: 20),
            Dive(start: Day(2026, 2, 1), durationSec: 2000, maxDepthM: 40),
            Dive(start: Day(2026, 8, 1), durationSec: 1500, maxDepthM: 25),
        });

        Assert.Equal(2, years.Count);
        Assert.Equal(new YearRollup(2025, 1, 1000, 20), years[0]);
        Assert.Equal(new YearRollup(2026, 2, 3500, 40), years[1]);
    }

    // ---- Deepest / Longest ----

    [Fact]
    public void Deepest_and_longest_pick_the_right_dive()
    {
        var deep = Dive(maxDepthM: 55, durationSec: 1000);
        var longd = Dive(maxDepthM: 20, durationSec: 4000);
        var dives = new[] { Dive(maxDepthM: 30, durationSec: 2000), deep, longd };

        Assert.Equal(deep.Id, DiveStats.Deepest(dives).Value!.Id);
        Assert.Equal(longd.Id, DiveStats.Longest(dives).Value!.Id);
    }

    [Fact]
    public void Deepest_of_empty_has_no_data()
    {
        var result = DiveStats.Deepest([]);
        Assert.False(result.HasData);
        Assert.Null(result.Value);
    }

    // ---- Averages ----

    [Fact]
    public void Average_depth_uses_field_or_derives_from_samples_and_reports_completeness()
    {
        var dives = new[]
        {
            Dive(avgDepthM: 20),                                        // field present
            Dive(avgDepthM: null, samples: new[] { Sample(0, 0), Sample(60, 20) }), // derived mean = 10
            Dive(avgDepthM: null, samples: []),                        // no usable depth → excluded
        };

        var result = DiveStats.AverageDepthM(dives);

        Assert.Equal(15.0, result.Value, 6); // mean(20, 10)
        Assert.Equal(2, result.Completeness.Available);
        Assert.Equal(3, result.Completeness.Total);
    }

    [Fact]
    public void Average_duration_is_full_completeness()
    {
        var result = DiveStats.AverageDurationSec(new[] { Dive(durationSec: 1000), Dive(durationSec: 2000) });
        Assert.Equal(1500, result.Value, 6);
        Assert.Equal(1.0, result.Completeness.Fraction, 6);
    }

    // ---- TemperatureRange ----

    [Fact]
    public void Temperature_range_spans_dive_level_and_sample_temps_and_skips_templess_dives()
    {
        var dives = new[]
        {
            Dive(minTempC: 18),
            Dive(samples: new[] { Sample(0, 0, temp: 12), Sample(60, 20, temp: 20) }),
            Dive(minTempC: null, samples: new[] { Sample(0, 0) }), // no temp → excluded
        };

        var result = DiveStats.TemperatureRange(dives);

        Assert.True(result.HasData);
        Assert.Equal(12, result.Value.MinC, 6);
        Assert.Equal(20, result.Value.MaxC, 6);
        Assert.Equal(2, result.Completeness.Available);
        Assert.Equal(3, result.Completeness.Total);
    }

    [Fact]
    public void Temperature_range_with_no_temps_has_no_data()
    {
        Assert.False(DiveStats.TemperatureRange(new[] { Dive(minTempC: null, samples: []) }).HasData);
    }

    // ---- GasMixBreakdown ----

    [Fact]
    public void Gas_mix_breakdown_classifies_cylinders_first_then_string_and_counts_unknowns()
    {
        var dives = new[]
        {
            Dive(cylinders: new[] { Cyl(o2: 21, he: 35) }),   // helium → Trimix
            Dive(cylinders: new[] { Cyl(o2: 32) }),           // enriched → Nitrox
            Dive(cylinders: new[] { Cyl(o2: 21) }),           // ~21% → Air
            Dive(cylinders: null, gasMix: "Air"),             // string → Air
            Dive(cylinders: null, gasMix: "EAN32"),           // string → Nitrox
            Dive(cylinders: null, gasMix: "21/35"),           // O2/He notation → Trimix
            Dive(cylinders: null, gasMix: null),              // nothing → Unknown
        };

        var result = DiveStats.GasMixBreakdown(dives);

        Assert.Equal(2, result.Value.Air);
        Assert.Equal(2, result.Value.Nitrox);
        Assert.Equal(2, result.Value.Trimix);
        Assert.Equal(1, result.Value.Unknown);
        Assert.Equal(6, result.Completeness.Available); // known = 7 − 1 unknown
        Assert.Equal(7, result.Completeness.Total);
    }

    // ---- MostDivedSites ----

    [Fact]
    public void Most_dived_sites_ranks_by_count_and_respects_top_and_completeness()
    {
        var siteA = Guid.NewGuid();
        var siteB = Guid.NewGuid();
        var dives = new[]
        {
            Dive(siteId: siteA), Dive(siteId: siteA), Dive(siteId: siteA),
            Dive(siteId: siteB),
            Dive(siteId: null), Dive(siteId: null),
        };

        var all = DiveStats.MostDivedSites(dives);
        Assert.Equal(2, all.Value.Count);
        Assert.Equal(new SiteFrequency(siteA, 3), all.Value[0]);
        Assert.Equal(new SiteFrequency(siteB, 1), all.Value[1]);
        Assert.Equal(4, all.Completeness.Available);
        Assert.Equal(6, all.Completeness.Total);

        var capped = DiveStats.MostDivedSites(dives, top: 1);
        Assert.Single(capped.Value);
        Assert.Equal(siteA, capped.Value[0].SiteId);
    }

    // ---- SAC ----

    [Fact]
    public void Sac_over_time_emits_only_computable_dives_in_chronological_order()
    {
        var early = Dive(start: Day(2026, 1, 2), avgDepthM: 20, durationSec: 1200,
            cylinders: new[] { Cyl(startBar: 220, endBar: 100, volumeL: 10) }); // SAC 2.0, RMV 20
        var later = Dive(start: Day(2026, 1, 10), avgDepthM: 20, durationSec: 1200,
            cylinders: new[] { Cyl(startBar: 160, endBar: 100, volumeL: 10) }); // SAC 1.0, RMV 10
        var noGas = Dive(start: Day(2026, 1, 5), cylinders: null);              // excluded

        var result = DiveStats.SacOverTime(new[] { later, noGas, early });

        Assert.Equal(2, result.Value.Count);
        Assert.Equal(early.Id, result.Value[0].DiveId);  // sorted by StartUtc
        Assert.Equal(later.Id, result.Value[1].DiveId);
        Assert.Equal(2.0, result.Value[0].SacBarPerMin!.Value, 6);
        Assert.Equal(20.0, result.Value[0].RmvLPerMin!.Value, 6);
        Assert.Equal(2, result.Completeness.Available);
        Assert.Equal(3, result.Completeness.Total);
    }

    [Fact]
    public void Sac_summary_means_over_qualifying_dives_only()
    {
        var dives = new[]
        {
            Dive(avgDepthM: 20, durationSec: 1200, cylinders: new[] { Cyl(startBar: 220, endBar: 100, volumeL: 10) }), // SAC 2, RMV 20
            Dive(avgDepthM: 20, durationSec: 1200, cylinders: new[] { Cyl(startBar: 160, endBar: 100) }),             // SAC 1, no RMV (no vol)
            Dive(cylinders: null), // excluded
        };

        var result = DiveStats.SacSummary(dives);

        Assert.Equal(1.5, result.Value.MeanSacBarPerMin, 6); // mean(2, 1)
        Assert.Equal(20.0, result.Value.MeanRmvLPerMin!.Value, 6); // only one dive had volume
        Assert.Equal(2, result.Value.SacDiveCount);
        Assert.Equal(1, result.Value.RmvDiveCount);
        Assert.Equal(2, result.Completeness.Available);
        Assert.Equal(3, result.Completeness.Total);
    }

    [Fact]
    public void Sac_ignores_depth_temp_only_dives_without_pressure()
    {
        // A HealthKit-style dive: depth+temp samples, no cylinders → contributes nothing, doesn't skew.
        var healthKit = Dive(samples: new[] { Sample(0, 0, temp: 20), Sample(60, 20, temp: 18) }, cylinders: null);
        var result = DiveStats.SacSummary(new[] { healthKit });
        Assert.False(result.HasData);
        Assert.Equal(0, result.Value.SacDiveCount);
    }

    // ---- Streaks ----

    [Fact]
    public void Streaks_measure_current_and_longest_consecutive_dive_days()
    {
        var dives = new[]
        {
            Dive(start: Day(2026, 1, 1)),
            Dive(start: Day(2026, 1, 2)),
            Dive(start: Day(2026, 1, 2)), // same day counts once
            Dive(start: Day(2026, 1, 3)), // run of 3
            Dive(start: Day(2026, 1, 5)),
            Dive(start: Day(2026, 1, 6)), // current run of 2 (ends at the latest day)
        };

        var streaks = DiveStats.Streaks(dives);

        Assert.Equal(3, streaks.LongestDays);
        Assert.Equal(2, streaks.CurrentDays);
    }

    [Fact]
    public void Streaks_of_empty_are_zero()
    {
        Assert.Equal(StreakStats.None, DiveStats.Streaks([]));
    }

    // ---- CalendarHeatmap ----

    [Fact]
    public void Calendar_heatmap_counts_per_day_within_the_year_only()
    {
        var dives = new[]
        {
            Dive(start: Day(2026, 4, 10)),
            Dive(start: Day(2026, 4, 10)), // same day → count 2
            Dive(start: Day(2026, 7, 1)),
            Dive(start: Day(2025, 12, 31)), // different year → excluded
        };

        var heatmap = DiveStats.CalendarHeatmap(dives, 2026);

        Assert.Equal(2, heatmap.Count);
        Assert.Equal(new HeatmapDay(new DateOnly(2026, 4, 10), 2), heatmap[0]);
        Assert.Equal(new HeatmapDay(new DateOnly(2026, 7, 1), 1), heatmap[1]);
    }
}
