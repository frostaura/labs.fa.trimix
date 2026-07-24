using System.Text;
using System.Xml.Linq;
using Trimix.Core.Connectors.Export;
using Xunit;

namespace Trimix.Core.Tests.Connectors.Export;

/// <summary>UDDF export: well-formed XML, key values round-trip, metric+Kelvin conventions, gas fractions.</summary>
public class UddfDiveExporterTests
{
    private static readonly XNamespace N = UddfDiveExporter.Ns;

    private static XDocument ExportAndParse(Trimix.Contracts.Dtos.DiveDto dive)
    {
        var art = new UddfDiveExporter().Export(dive);
        var xml = Encoding.UTF8.GetString([.. art.Content]);
        return XDocument.Parse(xml); // throws if not well-formed
    }

    [Fact]
    public void Descriptor_metadata_is_uddf()
    {
        var ex = new UddfDiveExporter();
        Assert.Equal(DiveExportFormat.Uddf, ex.Format);
        Assert.Equal("uddf", ex.FileExtension);
        Assert.Equal("application/x-uddf+xml", ex.ContentType);
    }

    [Fact]
    public void Export_produces_wellformed_uddf_root()
    {
        var doc = ExportAndParse(DiveFixture.Trimix());
        Assert.Equal(N + "uddf", doc.Root!.Name);
        Assert.Equal("3.2.0", doc.Root.Attribute("version")!.Value);
    }

    [Fact]
    public void Export_roundtrips_summary_values()
    {
        var doc = ExportAndParse(DiveFixture.Trimix());
        var after = doc.Descendants(N + "informationafterdive").Single();
        Assert.Equal(28.5, DiveFixture.D(after.Element(N + "greatestdepth")!.Value), 6);
        Assert.Equal(3180, DiveFixture.I(after.Element(N + "diveduration")!.Value));
        Assert.Equal(16.2, DiveFixture.D(after.Element(N + "averagedepth")!.Value), 6);
    }

    [Fact]
    public void Temperatures_are_in_kelvin()
    {
        var doc = ExportAndParse(DiveFixture.Trimix());
        var lowest = doc.Descendants(N + "lowesttemperature").Single().Value;
        Assert.Equal(12.0 + 273.15, DiveFixture.D(lowest), 6);

        var firstWaypointTemp = doc.Descendants(N + "waypoint").First().Element(N + "temperature")!.Value;
        Assert.Equal(21.0 + 273.15, DiveFixture.D(firstWaypointTemp), 6);
    }

    [Fact]
    public void Waypoint_count_matches_sample_count()
    {
        var dive = DiveFixture.Trimix();
        var doc = ExportAndParse(dive);
        Assert.Equal(dive.ProfileSamples.Count, doc.Descendants(N + "waypoint").Count());
    }

    [Fact]
    public void Tank_pressure_is_converted_bar_to_pascal()
    {
        var doc = ExportAndParse(DiveFixture.Trimix());
        var tankPressures = doc.Descendants(N + "tankpressure").Select(e => DiveFixture.D(e.Value)).ToList();
        Assert.Contains(200 * 100000.0, tankPressures);
        Assert.Contains(150 * 100000.0, tankPressures);
    }

    [Fact]
    public void Gas_mix_fractions_come_from_cylinder()
    {
        var doc = ExportAndParse(DiveFixture.Trimix());
        var mix = doc.Descendants(N + "mix").Single();
        Assert.Equal(0.21, DiveFixture.D(mix.Element(N + "o2")!.Value), 4);
        Assert.Equal(0.35, DiveFixture.D(mix.Element(N + "he")!.Value), 4);
        Assert.Equal(0.44, DiveFixture.D(mix.Element(N + "n2")!.Value), 4);
    }

    [Fact]
    public void Air_dive_without_cylinders_falls_back_to_air_mix()
    {
        var doc = ExportAndParse(DiveFixture.AirNoCylinders());
        var mix = doc.Descendants(N + "mix").Single();
        Assert.Equal(0.21, DiveFixture.D(mix.Element(N + "o2")!.Value), 4);
        Assert.Equal(0.0, DiveFixture.D(mix.Element(N + "he")!.Value), 4);
    }

    [Fact]
    public void Depth_temp_only_dive_omits_optional_after_dive_fields()
    {
        var doc = ExportAndParse(DiveFixture.AirNoCylinders());
        var after = doc.Descendants(N + "informationafterdive").Single();
        Assert.Null(after.Element(N + "averagedepth"));
        Assert.Null(after.Element(N + "lowesttemperature"));
        // Samples with null temp emit no temperature element.
        Assert.Empty(doc.Descendants(N + "waypoint").Descendants(N + "temperature"));
    }
}
