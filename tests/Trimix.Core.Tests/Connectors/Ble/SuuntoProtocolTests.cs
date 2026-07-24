using System.Buffers.Binary;
using Trimix.Core.Abstractions;
using Trimix.Core.DiveComputer;
using Trimix.Core.DiveComputer.Protocols;
using Trimix.Core.Tests.Fakes;
using Xunit;

namespace Trimix.Core.Tests.Connectors.Ble;

/// <summary>
/// <see cref="SuuntoProtocol"/> against committed TLV fixture records: full rich-channel decode
/// (depth/temp/NDL/ceiling/CNS/tank/setpoint/gas table), multi-dive streams, and the strict error contract.
/// </summary>
public class SuuntoProtocolTests
{
    private const byte Magic = 0x53;
    private const byte Header = 0x10;
    private const byte Sample = 0x20;
    private const byte Gas = 0x30;
    private const byte Footer = 0x40;

    private readonly SuuntoProtocol _protocol = new();

    private static byte[] HeaderPayload(DateTime startUtc, int durationSec, int intervalSec)
    {
        var p = new byte[14];
        BinaryPrimitives.WriteInt64LittleEndian(p.AsSpan(0, 8), BleFrameBuilder.UnixMillis(startUtc));
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(8, 4), (uint)durationSec);
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(12, 2), (ushort)intervalSec);
        return p;
    }

    private static byte[] GasPayload(byte index, byte o2, byte he) => [index, o2, he];

    private static byte[] SamplePayload(
        int t, int depthCm, int tempCenti, int ndlMin, int ceilingCm, int tankDbar, int cns, int gasIndex, int setpointMbar)
    {
        var p = new byte[18];
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(0, 4), (uint)t);
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(4, 2), (ushort)depthCm);
        BinaryPrimitives.WriteInt16LittleEndian(p.AsSpan(6, 2), (short)tempCenti);
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(8, 2), (ushort)ndlMin);
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(10, 2), (ushort)ceilingCm);
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(12, 2), (ushort)tankDbar);
        p[14] = (byte)cns;
        p[15] = (byte)gasIndex;
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(16, 2), (ushort)setpointMbar);
        return p;
    }

    private static DiveComputerFrame Frame(byte type, byte[] payload) => BleFrameBuilder.XorFrame2(Magic, type, payload);

    /// <summary>One deco dive: Air + EAN50, four samples exercising every rich channel.</summary>
    private static readonly DateTime Start = new(2026, 6, 20, 8, 30, 0, DateTimeKind.Utc);

    private static IReadOnlyList<DiveComputerFrame> RichDive() =>
    [
        Frame(Header, HeaderPayload(Start, durationSec: 40, intervalSec: 10)),
        Frame(Gas, GasPayload(0, 21, 0)),
        Frame(Gas, GasPayload(1, 50, 0)),
        //             t   depthCm tempCenti ndl   ceilCm tankDbar cns gas sp
        Frame(Sample, SamplePayload(0, 0, 2500, 99, 0, 2000, 1, 0, 0)),
        Frame(Sample, SamplePayload(10, 1800, 2200, 15, 0, 1950, 3, 0, 0)),
        Frame(Sample, SamplePayload(20, 3000, 1800, 0xFFFF, 300, 1800, 8, 0, 0)),
        Frame(Sample, SamplePayload(30, 600, 2000, 0xFFFF, 300, 1700, 9, 1, 1300)),
        Frame(Footer, []),
    ];

    [Fact]
    public async Task Decodes_summary_and_every_rich_channel()
    {
        var dives = await _protocol.ParseAsync(AsyncStream.From(RichDive()));

        var dive = Assert.Single(dives);
        Assert.Equal(Start, dive.StartUtc);
        Assert.Equal(40, dive.DurationSec);
        Assert.Equal(4, dive.Samples.Count);
        Assert.Equal(30.0, dive.MaxDepthM, 3);
        Assert.Equal(13.5, dive.AvgDepthM!.Value, 3);
        Assert.Equal(18.0, dive.MinTempC!.Value, 3);
        Assert.Equal("Air", dive.GasMix);

        var a = dive.Samples[0];
        Assert.Equal(0, a.TSeconds);
        Assert.Equal(0.0, a.DepthM, 3);
        Assert.Equal(25.0, a.TempC!.Value, 3);
        Assert.Equal(99.0, a.Ndl!.Value, 3);
        Assert.Null(a.CeilingM);
        Assert.Equal(200.0, a.TankPressureBar!.Value, 3);
        Assert.Equal(1.0, a.Cns!.Value, 3);
        Assert.Null(a.Setpoint);

        var c = dive.Samples[2];
        Assert.Equal(20, c.TSeconds);
        Assert.Null(c.Ndl);                       // 0xFFFF → in-deco/n-a
        Assert.Equal(3.0, c.CeilingM!.Value, 3);  // 300 cm
        Assert.Equal(180.0, c.TankPressureBar!.Value, 3);

        var d = dive.Samples[3];
        Assert.Equal(1.3, d.Setpoint!.Value, 3);  // 1300 mbar ppO2
        Assert.Equal(170.0, d.TankPressureBar!.Value, 3);
    }

    [Fact]
    public async Task Emits_a_cylinder_per_gas_with_tank_trace_on_the_primary()
    {
        var dives = await _protocol.ParseAsync(AsyncStream.From(RichDive()));
        var cylinders = Assert.Single(dives).Cylinders!;

        Assert.Equal(2, cylinders.Count);
        Assert.Equal(21, cylinders[0].O2!.Value, 3);
        Assert.Null(cylinders[0].He);
        Assert.Equal(200.0, cylinders[0].StartBar!.Value, 3);   // first tank reading
        Assert.Equal(170.0, cylinders[0].EndBar!.Value, 3);     // last tank reading
        Assert.Equal(50, cylinders[1].O2!.Value, 3);
        Assert.Null(cylinders[1].StartBar);                     // trace attaches to primary only
    }

    [Fact]
    public async Task Two_dives_parse_independently_including_trimix_naming()
    {
        List<DiveComputerFrame> frames = [.. RichDive()];
        var second = new DateTime(2026, 6, 21, 9, 0, 0, DateTimeKind.Utc);
        frames.AddRange(
        [
            Frame(Header, HeaderPayload(second, 30, 10)),
            Frame(Gas, GasPayload(0, 18, 45)), // hypoxic trimix
            Frame(Sample, SamplePayload(0, 0, 2400, 0xFFFF, 0, 2200, 0, 0, 0)),
            Frame(Sample, SamplePayload(10, 4100, 1000, 0xFFFF, 500, 2000, 5, 0, 0)),
            Frame(Footer, []),
        ]);

        var dives = await _protocol.ParseAsync(AsyncStream.From(frames));

        Assert.Equal(2, dives.Count);
        Assert.Equal("Air", dives[0].GasMix);
        Assert.Equal("18/45", dives[1].GasMix);
        Assert.Equal(second, dives[1].StartUtc);
        Assert.Equal(41.0, dives[1].MaxDepthM, 3);
    }

    [Fact]
    public async Task Checksum_mismatch_throws()
    {
        var frames = RichDive().ToArray();
        var corrupt = frames[3].Payload.ToArray();
        corrupt[5] ^= 0xFF; // flip a depth byte, leave checksum stale
        frames[3] = new DiveComputerFrame(corrupt);

        var ex = await Assert.ThrowsAsync<DiveComputerProtocolException>(() => _protocol.ParseAsync(AsyncStream.From(frames)));
        Assert.Contains("checksum", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unknown_record_type_throws()
    {
        var frames = new[] { Frame(0x7F, [0x00]) };
        var ex = await Assert.ThrowsAsync<DiveComputerProtocolException>(() => _protocol.ParseAsync(AsyncStream.From(frames)));
        Assert.Contains("Unknown Suunto record type", ex.Message);
    }

    [Fact]
    public async Task Sample_before_header_throws()
    {
        var frames = new[] { Frame(Sample, SamplePayload(0, 0, 2500, 99, 0, 2000, 1, 0, 0)) };
        var ex = await Assert.ThrowsAsync<DiveComputerProtocolException>(() => _protocol.ParseAsync(AsyncStream.From(frames)));
        Assert.Contains("before any dive header", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unknown_gas_index_throws()
    {
        var frames = new[]
        {
            Frame(Header, HeaderPayload(Start, 20, 10)),
            Frame(Gas, GasPayload(0, 21, 0)),
            Frame(Sample, SamplePayload(0, 0, 2500, 99, 0, 2000, 1, 7, 0)), // gas 7 undefined
        };
        var ex = await Assert.ThrowsAsync<DiveComputerProtocolException>(() => _protocol.ParseAsync(AsyncStream.From(frames)));
        Assert.Contains("unknown gas index", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Stream_ending_mid_dive_throws()
    {
        var frames = RichDive().ToList();
        frames.RemoveAt(frames.Count - 1); // drop the footer
        var ex = await Assert.ThrowsAsync<DiveComputerProtocolException>(() => _protocol.ParseAsync(AsyncStream.From(frames)));
        Assert.Contains("mid-dive", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Non_increasing_sample_time_throws()
    {
        var frames = new[]
        {
            Frame(Header, HeaderPayload(Start, 20, 10)),
            Frame(Gas, GasPayload(0, 21, 0)),
            Frame(Sample, SamplePayload(10, 0, 2500, 99, 0, 2000, 1, 0, 0)),
            Frame(Sample, SamplePayload(10, 100, 2500, 99, 0, 2000, 1, 0, 0)), // not strictly increasing
        };
        var ex = await Assert.ThrowsAsync<DiveComputerProtocolException>(() => _protocol.ParseAsync(AsyncStream.From(frames)));
        Assert.Contains("increase in time", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
