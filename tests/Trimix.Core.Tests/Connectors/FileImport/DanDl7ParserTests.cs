using System.Text;
using Trimix.Core.Connectors.FileImport;
using Xunit;

namespace Trimix.Core.Tests.Connectors.FileImport;

/// <summary>DanDl7Parser against the committed <c>sample.dl7</c> (ZDH header + tab-delimited ZDP block + ZDT trailer).</summary>
public class DanDl7ParserTests
{
    private readonly DanDl7Parser _parser = new();

    [Fact]
    public void Sniffs_dl7_by_content_and_extension()
    {
        Assert.True(_parser.CanParse(FileImportFixtures.Dl7));
        Assert.True(_parser.CanParse(new PickedFile("dive.zxu", FileImportFixtures.Dl7.Contents)));
        Assert.False(_parser.CanParse(new PickedFile("x.uddf", "<uddf/>"u8.ToArray())));
    }

    [Fact]
    public void Parses_trailer_metadata_and_gas()
    {
        var dive = Assert.Single(_parser.Parse(FileImportFixtures.Dl7));

        Assert.Equal(new DateTime(2026, 6, 3, 12, 0, 0, DateTimeKind.Utc), dive.StartUtc);
        Assert.Equal(120, dive.DurationSec);        // ZDT bottom time
        Assert.Equal(32.0, dive.MaxDepthM, 3);      // ZDT max depth
        Assert.Equal(18.0, dive.MinTempC!.Value, 3); // ZDT min temp
        Assert.Equal("EAN32", dive.GasMix);

        var cylinder = Assert.Single(dive.Cylinders!);
        Assert.Equal(32, cylinder.O2);
        Assert.Equal(0, cylinder.He);
    }

    [Fact]
    public void Reads_tab_delimited_waypoints()
    {
        var dive = Assert.Single(_parser.Parse(FileImportFixtures.Dl7));

        Assert.Equal(5, dive.Samples.Count);
        Assert.Equal([0, 30, 60, 90, 120], dive.Samples.Select(s => s.TSeconds));

        var deepest = dive.Samples[2];
        Assert.Equal(32.0, deepest.DepthM, 3);
        Assert.Equal(18.0, deepest.TempC!.Value, 3);
    }

    [Fact]
    public void Reads_the_zdt_trailer_even_when_the_zdp_end_marker_is_missing()
    {
        // Real-world DL7 variance: some tools omit the '}ZDP' close. The ZDT trailer must still be read
        // (not swallowed by the profile-row parser) so max depth / bottom time / min temp come from ZDT.
        // The sample max (40 m) and last-sample time (60 s) differ from the trailer, so a swallowed trailer
        // would surface those instead — the assertions below fail loudly if the fix regresses.
        var text =
            "ZDH|1|1|I|20260603120000|EAN32\n" +
            "ZDP{\n" +
            "0\t0.0\t24.0\n" +
            "60\t40.0\t14.0\n" +
            "ZDT|32.0|120|18.0\n"; // no '}ZDP' end-marker before the trailer
        var file = new PickedFile("nomarker.dl7", Encoding.UTF8.GetBytes(text));

        var dive = Assert.Single(_parser.Parse(file));

        Assert.Equal(120, dive.DurationSec);         // ZDT bottom time, not the 60 s last sample
        Assert.Equal(32.0, dive.MaxDepthM, 3);       // ZDT max depth, not the 40 m sample max
        Assert.Equal(18.0, dive.MinTempC!.Value, 3); // ZDT min temp
        Assert.Equal(2, dive.Samples.Count);         // the ZDT line is NOT parsed as a 3rd waypoint
    }
}
