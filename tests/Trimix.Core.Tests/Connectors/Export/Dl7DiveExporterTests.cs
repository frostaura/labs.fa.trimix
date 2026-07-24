using System.Text;
using Trimix.Core.Connectors.Export;
using Xunit;

namespace Trimix.Core.Tests.Connectors.Export;

/// <summary>DAN DL7 export: HL7-style segment envelope (FSH/ZAR/ZDH/ZDP*/ZDT), raw encoding chars, metric.</summary>
public class Dl7DiveExporterTests
{
    private static string Export(Trimix.Contracts.Dtos.DiveDto dive) =>
        Encoding.ASCII.GetString([.. new Dl7DiveExporter().Export(dive).Content]);

    [Fact]
    public void Descriptor_metadata_is_dl7()
    {
        var ex = new Dl7DiveExporter();
        Assert.Equal(DiveExportFormat.Dl7, ex.Format);
        Assert.Equal("dl7", ex.FileExtension);
    }

    [Fact]
    public void Starts_with_fsh_header()
    {
        var text = Export(DiveFixture.Trimix());
        Assert.StartsWith("FSH|", text);
    }

    [Fact]
    public void Fsh_declares_raw_hl7_encoding_characters()
    {
        // The encoding-characters field must be emitted RAW; it must not be delimiter-sanitized.
        var text = Export(DiveFixture.Trimix());
        Assert.StartsWith("FSH|^~\\&|", text);
    }

    [Fact]
    public void Contains_application_and_dive_header_segments()
    {
        var text = Export(DiveFixture.Trimix());
        Assert.Contains("\r\nZAR|", text);
        Assert.Contains("\r\nZDH|", text);
    }

    [Fact]
    public void One_zdp_profile_record_per_sample()
    {
        var dive = DiveFixture.Trimix();
        var text = Export(dive);
        var zdpCount = text.Split("\r\n").Count(line => line.StartsWith("ZDP|", StringComparison.Ordinal));
        Assert.Equal(dive.ProfileSamples.Count, zdpCount);
    }

    [Fact]
    public void Ends_with_zdt_trailer()
    {
        var text = Export(DiveFixture.Trimix());
        Assert.Contains("\r\nZDT|", text);
        Assert.EndsWith("\r\n", text);
    }

    [Fact]
    public void Segments_are_crlf_terminated()
    {
        var text = Export(DiveFixture.Trimix());
        var segments = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.All(segments, s => Assert.Matches("^[A-Z]{3}\\|", s));
    }

    [Fact]
    public void Duration_is_encoded_in_minutes()
    {
        // 3180s / 60 = 53 min, present in both ZDH and ZDT.
        var text = Export(DiveFixture.Trimix());
        Assert.Contains("53", text);
    }

    [Fact]
    public void Free_text_cannot_inject_delimiters()
    {
        // GasMix and other fields are sanitized; a pipe in data never appears as an extra field separator.
        var dive = DiveFixture.Trimix() with { GasMix = "a|b^c~d" };
        var text = Export(dive);
        var zdh = text.Split("\r\n").Single(l => l.StartsWith("ZDH|", StringComparison.Ordinal));
        Assert.DoesNotContain('^', zdh);
        Assert.DoesNotContain('~', zdh);
        Assert.Equal(7, zdh.Split('|').Length); // "ZDH" + 6 fields = 7 pipe-parts, unchanged by injection.
    }
}
