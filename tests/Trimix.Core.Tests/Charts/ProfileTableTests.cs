using System.Globalization;
using Trimix.Contracts.Dtos;
using Trimix.Core.Charts;
using Xunit;

namespace Trimix.Core.Tests.Charts;

/// <summary>
/// The columnar WCAG table alternative: Elapsed + Depth always; every other column only when a
/// sample carries it, so the accessible table grows exactly with the drawn overlays and never shows
/// an empty column for a depth-temp-only dive.
/// </summary>
public class ProfileTableTests
{
    private static ProfileSampleDto Rich(
        int t,
        double depth,
        double? temp = null,
        double? ppO2 = null,
        double? ndl = null,
        double? ceiling = null,
        double? ascent = null,
        double? cns = null,
        double? tank = null,
        double? setpoint = null,
        double? hr = null) =>
        new(t, depth, temp, ppO2, ndl, ceiling, ascent, cns, tank, setpoint, hr);

    [Fact]
    public void Empty_samples_yield_an_empty_table()
    {
        var table = ChartGeometry.BuildProfileTable([]);

        Assert.Empty(table.Headers);
        Assert.Empty(table.Rows);
    }

    [Fact]
    public void Depth_only_source_shows_exactly_elapsed_and_depth()
    {
        var table = ChartGeometry.BuildProfileTable([Rich(0, 0), Rich(60, 20), Rich(120, 5)]);

        Assert.Equal(["Elapsed", "Depth"], table.Headers);
        Assert.Equal(3, table.Rows.Count);
        Assert.All(table.Rows, r => Assert.Equal(2, r.Count));
    }

    [Fact]
    public void Depth_temp_only_healthkit_case_adds_only_a_temp_column()
    {
        var table = ChartGeometry.BuildProfileTable([Rich(0, 0, temp: 21), Rich(60, 20, temp: 14)]);

        Assert.Equal(["Elapsed", "Depth", "Temp"], table.Headers);
    }

    [Fact]
    public void Columns_appear_in_the_fixed_canonical_order()
    {
        var full = new[]
        {
            Rich(0, 0, temp: 20, ppO2: 0.21, ndl: 99, ceiling: 0, ascent: 3, cns: 1, tank: 210, setpoint: 0.7, hr: 70),
            Rich(60, 30, temp: 14, ppO2: 1.2, ndl: 5, ceiling: 3, ascent: 9, cns: 5, tank: 150, setpoint: 1.3, hr: 95),
        };

        var table = ChartGeometry.BuildProfileTable(full);

        Assert.Equal(
            ["Elapsed", "Depth", "Temp", "PpO2", "NDL", "Ceiling", "Ascent", "Tank", "HR", "Setpoint"],
            table.Headers);
    }

    [Fact]
    public void Ascent_rate_gets_its_own_text_column_so_the_ascent_marker_is_not_marker_only()
    {
        // The AscentWarning overlay is drawn from AscentRateMPerMin; its ONLY text equivalent is this column.
        var samples = new[] { Rich(0, 0), Rich(60, 30, ascent: 12), Rich(120, 5) };
        var table = ChartGeometry.BuildProfileTable(samples);

        Assert.Contains("Ascent", table.Headers);
        var col = table.Headers.ToList().IndexOf("Ascent");
        Assert.Equal("12", table.Rows[1][col]);
    }

    [Fact]
    public void A_channel_present_in_only_one_sample_still_gets_a_column_with_blank_cells()
    {
        var samples = new[] { Rich(0, 0), Rich(60, 20, tank: 180), Rich(120, 5) };
        var table = ChartGeometry.BuildProfileTable(samples);

        Assert.Contains("Tank", table.Headers);
        var tankCol = table.Headers.ToList().IndexOf("Tank");
        Assert.Equal(string.Empty, table.Rows[0][tankCol]); // no reading
        Assert.Equal("180", table.Rows[1][tankCol]);        // the one reading
        Assert.Equal(string.Empty, table.Rows[2][tankCol]);
    }

    [Fact]
    public void Cells_are_invariant_numeric_strings_and_rows_follow_time_order()
    {
        // Deliberately out of order to prove sorting; use a decimal to prove invariant formatting.
        var samples = new[] { Rich(60, 20.5, temp: 14.2), Rich(0, 0, temp: 21) };
        var table = ChartGeometry.BuildProfileTable(samples);

        Assert.Equal("0", table.Rows[0][0]);    // elapsed sorted ascending
        Assert.Equal("60", table.Rows[1][0]);
        Assert.Equal("20.5", table.Rows[1][1]); // invariant decimal
        Assert.Equal(20.5.ToString(CultureInfo.InvariantCulture), table.Rows[1][1]);
    }

    [Fact]
    public void Row_width_matches_header_width_for_every_row()
    {
        var samples = new[] { Rich(0, 0, temp: 20, hr: 60), Rich(60, 20, temp: 14, hr: 90) };
        var table = ChartGeometry.BuildProfileTable(samples);

        Assert.All(table.Rows, r => Assert.Equal(table.Headers.Count, r.Count));
    }
}
