using Trimix.Core.Connectors.FileImport;
using Xunit;

namespace Trimix.Core.Tests.Connectors.FileImport;

/// <summary>SuuntoJsonParser against the committed <c>suunto-sample.json</c> (community schema: HR + tank pressure + trimix gas).</summary>
public class SuuntoJsonParserTests
{
    private readonly SuuntoJsonParser _parser = new();

    [Fact]
    public void Sniffs_suunto_json_but_not_arbitrary_json()
    {
        Assert.True(_parser.CanParse(FileImportFixtures.SuuntoJson));
        Assert.False(_parser.CanParse(new PickedFile("other.json", "{\"foo\":1}"u8.ToArray())));
        Assert.False(_parser.CanParse(new PickedFile("x.uddf", "<uddf/>"u8.ToArray())));
    }

    [Fact]
    public void Parses_envelope_metadata_and_trimix_gas()
    {
        var dive = Assert.Single(_parser.Parse(FileImportFixtures.SuuntoJson));

        Assert.Equal(new DateTime(2026, 6, 4, 14, 15, 0, DateTimeKind.Utc), dive.StartUtc);
        Assert.Equal(3120, dive.DurationSec);        // diveTime metadata, not the 240 s sample span
        Assert.Equal(41.2, dive.MaxDepthM, 3);
        Assert.Equal(8.0, dive.MinTempC!.Value, 3);
        Assert.Equal("18/45", dive.GasMix);          // hypoxic trimix from fractions 0.18 / 0.45

        var cylinder = Assert.Single(dive.Cylinders!);
        Assert.Equal(18, cylinder.O2);
        Assert.Equal(45, cylinder.He);
        Assert.Equal(230, cylinder.StartBar!.Value, 3);
        Assert.Equal(120, cylinder.EndBar!.Value, 3);
    }

    [Fact]
    public void Reconstructs_samples_with_heart_rate_and_tank_pressure()
    {
        var dive = Assert.Single(_parser.Parse(FileImportFixtures.SuuntoJson));

        Assert.Equal(5, dive.Samples.Count);
        Assert.Equal([0, 60, 120, 180, 240], dive.Samples.Select(s => s.TSeconds));

        var deepest = dive.Samples[2];
        Assert.Equal(41.2, deepest.DepthM, 3);
        Assert.Equal(8.0, deepest.TempC!.Value, 3);
        Assert.Equal(180, deepest.TankPressureBar!.Value, 3);
        Assert.Equal(95, deepest.HeartRate!.Value, 3);
    }
}
