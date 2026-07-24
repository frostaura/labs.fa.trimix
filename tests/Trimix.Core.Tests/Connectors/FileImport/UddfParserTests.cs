using System.Text;
using Trimix.Core.Connectors.FileImport;
using Xunit;

namespace Trimix.Core.Tests.Connectors.FileImport;

/// <summary>UddfParser against the committed <c>sample.uddf</c> (SI units: Kelvin temps, Pascal pressures).</summary>
public class UddfParserTests
{
    private readonly UddfParser _parser = new();

    [Fact]
    public void Sniffs_uddf_by_content_and_extension()
    {
        Assert.True(_parser.CanParse(FileImportFixtures.Uddf));
        Assert.True(_parser.CanParse(new PickedFile("x.txt", "<uddf version=\"3.2.0\"/>"u8.ToArray())));
        Assert.False(_parser.CanParse(new PickedFile("x.json", "{}"u8.ToArray())));
    }

    [Fact]
    public void Parses_metadata_with_si_unit_conversions()
    {
        var dive = Assert.Single(_parser.Parse(FileImportFixtures.Uddf));

        Assert.Equal(new DateTime(2026, 6, 2, 9, 30, 0, DateTimeKind.Utc), dive.StartUtc);
        Assert.Equal(240, dive.DurationSec);
        Assert.Equal(18.0, dive.MaxDepthM, 3);
        Assert.Equal(18.0, dive.MinTempC!.Value, 2);  // 291.15 K → 18.00 °C
        Assert.Equal("EAN32", dive.GasMix);

        var cylinder = Assert.Single(dive.Cylinders!);
        Assert.Equal(32, cylinder.O2);                // fraction 0.32 → 32 %
        Assert.Equal(0, cylinder.He);
        Assert.Equal(210, cylinder.StartBar!.Value, 3); // 21_000_000 Pa → 210 bar
        Assert.Equal(110, cylinder.EndBar!.Value, 3);
    }

    [Fact]
    public void Reconstructs_samples_with_temperature_and_tank_pressure()
    {
        var dive = Assert.Single(_parser.Parse(FileImportFixtures.Uddf));

        Assert.Equal(5, dive.Samples.Count);
        Assert.Equal([0, 60, 120, 180, 240], dive.Samples.Select(s => s.TSeconds));

        var deepest = dive.Samples[2];
        Assert.Equal(18.0, deepest.DepthM, 3);
        Assert.Equal(18.0, deepest.TempC!.Value, 2);      // 291.15 K
        Assert.Equal(150, deepest.TankPressureBar!.Value, 3); // 15_000_000 Pa → 150 bar
    }

    [Fact]
    public void A_mix_stamped_on_every_waypoint_yields_one_cylinder_not_one_per_waypoint()
    {
        // Some exporters stamp <switchmix> on EVERY waypoint even without a real gas change. The parser must
        // collapse to the DISTINCT gases (one cylinder here), not emit one duplicate CylinderDto per stamp.
        const string xml =
            "<uddf version=\"3.2.0\">" +
            "<gasdefinitions><mix id=\"ean32\"><o2>0.32</o2><he>0.0</he></mix></gasdefinitions>" +
            "<profiledata><repetitiongroup><dive>" +
            "<informationbeforedive><datetime>2026-06-02T09:30:00</datetime></informationbeforedive>" +
            "<samples>" +
            "<waypoint><depth>0</depth><divetime>0</divetime><switchmix ref=\"ean32\"/></waypoint>" +
            "<waypoint><depth>10</depth><divetime>60</divetime><switchmix ref=\"ean32\"/></waypoint>" +
            "<waypoint><depth>18</depth><divetime>120</divetime><switchmix ref=\"ean32\"/></waypoint>" +
            "<waypoint><depth>0</depth><divetime>180</divetime><switchmix ref=\"ean32\"/></waypoint>" +
            "</samples></dive></repetitiongroup></profiledata></uddf>";

        var dive = Assert.Single(_parser.Parse(new PickedFile("many.uddf", Encoding.UTF8.GetBytes(xml))));

        var cylinder = Assert.Single(dive.Cylinders!);
        Assert.Equal(32, cylinder.O2);
    }
}
