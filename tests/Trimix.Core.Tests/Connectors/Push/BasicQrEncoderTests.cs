using Trimix.Core.Connectors.Push;
using Xunit;

namespace Trimix.Core.Tests.Connectors.Push;

/// <summary>
/// The in-repo byte-mode / EC-M QR encoder (versions 1–10) behind <see cref="IQrEncoder"/>. A full round-trip
/// decode needs a scanner, so these assert the structural invariants every valid QR symbol must satisfy
/// (square size = 17+4·version, the three 7×7 finder patterns, the alternating timing rows, the always-dark
/// module) plus version selection growing with payload size — enough to catch a malformed grid.
/// </summary>
public class BasicQrEncoderTests
{
    private readonly BasicQrEncoder _encoder = new();

    [Fact]
    public void Short_payload_is_version_1_size_21_with_valid_finders_and_timing()
    {
        var qr = _encoder.Encode("HI");

        Assert.Equal(21, qr.Size); // version 1 => 17 + 4·1
        AssertFinders(qr);
        AssertTiming(qr);
        AssertDarkModule(qr);
    }

    [Fact]
    public void Real_ssi_payload_encodes_to_a_valid_multi_block_symbol()
    {
        // The genuine diver-mediated MySSI blob (~66 bytes) lands at version 5 (2 ECC blocks), exercising the
        // data+ECC interleave path, not just a single-block version.
        var payload = new SsiQrPayloadBuilder().Build(DiveFixture.Trimix());

        var qr = _encoder.Encode(payload);

        Assert.True(payload.Length is >= 40 and <= 90, $"payload length was {payload.Length}");
        Assert.Equal(0, (qr.Size - 17) % 4);                 // size is 17+4·version
        var version = (qr.Size - 17) / 4;
        Assert.InRange(version, 1, 10);
        AssertFinders(qr);
        AssertTiming(qr);
        AssertDarkModule(qr);
    }

    [Fact]
    public void Version_grows_with_payload_length()
    {
        var small = _encoder.Encode("HI");
        var large = _encoder.Encode(new string('A', 120)); // 120 bytes forces a higher version than "HI"

        Assert.True(large.Size > small.Size, $"expected {large.Size} > {small.Size}");
    }

    [Fact]
    public void Distinct_payloads_produce_distinct_matrices()
    {
        var a = _encoder.Encode("dive;depth_m:29");
        var b = _encoder.Encode("dive;depth_m:31");

        Assert.Equal(a.Size, b.Size);
        var differs = false;
        for (var y = 0; y < a.Size && !differs; y++)
        {
            for (var x = 0; x < a.Size && !differs; x++)
            {
                differs = a.IsDark(x, y) != b.IsDark(x, y);
            }
        }

        Assert.True(differs, "two different payloads must not render identical grids");
    }

    [Fact]
    public void Oversized_payload_throws_rather_than_emitting_a_broken_symbol()
    {
        Assert.Throws<ArgumentException>(() => _encoder.Encode(new string('X', 300)));
    }

    // ── structural helpers ────────────────────────────────────────────────────────────────────────
    private static void AssertFinders(QrMatrix qr)
    {
        AssertFinder(qr, 0, 0);                 // top-left
        AssertFinder(qr, qr.Size - 7, 0);       // top-right
        AssertFinder(qr, 0, qr.Size - 7);       // bottom-left
    }

    /// <summary>A finder is a 7×7 block: dark at Chebyshev distance {0,1,3} from its centre, light at 2.</summary>
    private static void AssertFinder(QrMatrix qr, int ox, int oy)
    {
        for (var j = 0; j < 7; j++)
        {
            for (var i = 0; i < 7; i++)
            {
                var dist = Math.Max(Math.Abs(i - 3), Math.Abs(j - 3));
                var expectedDark = dist != 2;
                Assert.Equal(expectedDark, qr.IsDark(ox + i, oy + j));
            }
        }
    }

    /// <summary>Row 6 and column 6, in the clean region between finders, alternate dark/light (even = dark).</summary>
    private static void AssertTiming(QrMatrix qr)
    {
        for (var k = 8; k <= qr.Size - 9; k++)
        {
            Assert.Equal(k % 2 == 0, qr.IsDark(k, 6)); // horizontal timing
            Assert.Equal(k % 2 == 0, qr.IsDark(6, k)); // vertical timing
        }
    }

    /// <summary>The single always-dark module sits at (x=8, y=size-8).</summary>
    private static void AssertDarkModule(QrMatrix qr) => Assert.True(qr.IsDark(8, qr.Size - 8));
}
