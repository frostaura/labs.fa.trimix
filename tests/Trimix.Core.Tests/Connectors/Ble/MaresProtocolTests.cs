using System.Buffers.Binary;
using Trimix.Core.Abstractions;
using Trimix.Core.DiveComputer;
using Trimix.Core.DiveComputer.Protocols;
using Trimix.Core.Tests.Fakes;
using Xunit;

namespace Trimix.Core.Tests.Connectors.Ble;

/// <summary>
/// <see cref="MaresProtocol"/> against committed Icon-HD/Genius strided fixtures: per-sample NDL + tank
/// decode with in-deco sentinel, single-gas cylinder with tank trace, no-transmitter fallback, misaligned
/// block rejection, and the strict error contract.
/// </summary>
public class MaresProtocolTests
{
    private const byte Magic = 0x4D;
    private const byte Header = 0x01;
    private const byte Samples = 0x02;
    private const byte End = 0x03;

    private readonly MaresProtocol _protocol = new();

    private static readonly DateTime Start = new(2026, 6, 24, 6, 30, 0, DateTimeKind.Utc);

    private static byte[] HeaderPayload(DateTime startUtc, int durationSec, int intervalSec, int o2, int he)
    {
        var p = new byte[9];
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(0, 4), BleFrameBuilder.UnixSeconds(startUtc));
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(4, 2), (ushort)durationSec);
        p[6] = (byte)intervalSec;
        p[7] = (byte)o2;
        p[8] = (byte)he;
        return p;
    }

    private static byte[] Record(int depthCm, int tempCenti, int ndlMin, int tankDbar)
    {
        var r = new byte[8];
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(0, 2), (ushort)depthCm);
        BinaryPrimitives.WriteInt16LittleEndian(r.AsSpan(2, 2), (short)tempCenti);
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(4, 2), (ushort)ndlMin);
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(6, 2), (ushort)tankDbar);
        return r;
    }

    private static byte[] Block(params byte[][] records) => records.SelectMany(r => r).ToArray();

    private static DiveComputerFrame HFrame(byte[] payload) => BleFrameBuilder.XorFrame2(Magic, Header, payload);

    private static DiveComputerFrame SFrame(byte[] payload) => BleFrameBuilder.XorFrame2(Magic, Samples, payload);

    private static DiveComputerFrame EFrame() => BleFrameBuilder.XorFrame2(Magic, End, []);

    private static IReadOnlyList<DiveComputerFrame> AirDive() =>
    [
        HFrame(HeaderPayload(Start, durationSec: 30, intervalSec: 10, o2: 21, he: 0)),
        SFrame(Block(
            Record(0, 2300, 60, 2200),
            Record(2000, 1900, 12, 2100),
            Record(3500, 1400, 0xFFFF, 1950),
            Record(500, 1800, 0xFFFF, 1850))),
        EFrame(),
    ];

    [Fact]
    public async Task Decodes_ndl_tank_and_summary()
    {
        var dives = await _protocol.ParseAsync(AsyncStream.From(AirDive()));
        var dive = Assert.Single(dives);

        Assert.Equal(Start, dive.StartUtc);
        Assert.Equal(30, dive.DurationSec);
        Assert.Equal("Air", dive.GasMix);
        Assert.Equal(new[] { 0, 10, 20, 30 }, dive.Samples.Select(s => s.TSeconds));
        Assert.Equal(new[] { 0.0, 20.0, 35.0, 5.0 }, dive.Samples.Select(s => Math.Round(s.DepthM, 2)));
        Assert.Equal(35.0, dive.MaxDepthM, 3);
        Assert.Equal(15.0, dive.AvgDepthM!.Value, 3);
        Assert.Equal(14.0, dive.MinTempC!.Value, 3);

        Assert.Equal(60.0, dive.Samples[0].Ndl!.Value, 3);
        Assert.Equal(12.0, dive.Samples[1].Ndl!.Value, 3);
        Assert.Null(dive.Samples[2].Ndl);   // 0xFFFF → in-deco
        Assert.Null(dive.Samples[3].Ndl);
        Assert.Equal(new[] { 220.0, 210.0, 195.0, 185.0 }, dive.Samples.Select(s => s.TankPressureBar!.Value));
    }

    [Fact]
    public async Task Single_gas_cylinder_carries_tank_trace()
    {
        var dives = await _protocol.ParseAsync(AsyncStream.From(AirDive()));
        var cylinder = Assert.Single(Assert.Single(dives).Cylinders!);

        Assert.Equal(21, cylinder.O2!.Value, 3);
        Assert.Null(cylinder.He);
        Assert.Equal(220.0, cylinder.StartBar!.Value, 3);
        Assert.Equal(185.0, cylinder.EndBar!.Value, 3);
    }

    [Fact]
    public async Task No_transmitter_leaves_tank_null()
    {
        IReadOnlyList<DiveComputerFrame> frames =
        [
            HFrame(HeaderPayload(Start, 20, 10, 32, 0)),
            SFrame(Block(Record(0, 2300, 60, 0xFFFF), Record(1500, 2000, 20, 0xFFFF))),
            EFrame(),
        ];
        var dive = Assert.Single(await _protocol.ParseAsync(AsyncStream.From(frames)));

        Assert.Equal("EAN32", dive.GasMix);
        Assert.All(dive.Samples, s => Assert.Null(s.TankPressureBar));
        Assert.Null(Assert.Single(dive.Cylinders!).StartBar);
    }

    [Fact]
    public async Task Misaligned_sample_block_throws()
    {
        var seven = new byte[7]; // not a multiple of 8
        IReadOnlyList<DiveComputerFrame> frames =
        [
            HFrame(HeaderPayload(Start, 20, 10, 21, 0)),
            SFrame(seven),
        ];
        var ex = await Assert.ThrowsAsync<DiveComputerProtocolException>(() => _protocol.ParseAsync(AsyncStream.From(frames)));
        Assert.Contains("8-byte records", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Checksum_mismatch_throws()
    {
        var frames = AirDive().ToArray();
        var corrupt = frames[1].Payload.ToArray();
        corrupt[6] ^= 0xFF;
        frames[1] = new DiveComputerFrame(corrupt);
        var ex = await Assert.ThrowsAsync<DiveComputerProtocolException>(() => _protocol.ParseAsync(AsyncStream.From(frames)));
        Assert.Contains("checksum", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unknown_frame_type_throws()
    {
        var frames = new[] { BleFrameBuilder.XorFrame2(Magic, 0x7F, [0x00]) };
        var ex = await Assert.ThrowsAsync<DiveComputerProtocolException>(() => _protocol.ParseAsync(AsyncStream.From(frames)));
        Assert.Contains("Unknown Mares frame type", ex.Message);
    }

    [Fact]
    public async Task Samples_before_header_throws()
    {
        var frames = new[] { SFrame(Block(Record(0, 2300, 60, 2200))) };
        var ex = await Assert.ThrowsAsync<DiveComputerProtocolException>(() => _protocol.ParseAsync(AsyncStream.From(frames)));
        Assert.Contains("before any dive header", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Stream_ending_mid_dive_throws()
    {
        var frames = AirDive().ToList();
        frames.RemoveAt(frames.Count - 1);
        var ex = await Assert.ThrowsAsync<DiveComputerProtocolException>(() => _protocol.ParseAsync(AsyncStream.From(frames)));
        Assert.Contains("mid-dive", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
