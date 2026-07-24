using System.Buffers.Binary;
using Trimix.Core.Abstractions;
using Trimix.Core.DiveComputer;
using Trimix.Core.DiveComputer.Protocols;
using Trimix.Core.Tests.Fakes;
using Xunit;

namespace Trimix.Core.Tests.Connectors.Ble;

/// <summary>
/// <see cref="ScubaProProtocol"/> against committed Uwatec-"Smart" delta fixtures: base + accumulated
/// signed deltas for depth/temp/tank reconstructed correctly, single-gas cylinder with tank trace,
/// no-transmitter fallback, and the strict error contract.
/// </summary>
public class ScubaProProtocolTests
{
    private const byte Magic = 0x55;
    private const byte Header = 0x01;
    private const byte Samples = 0x02;
    private const byte End = 0x03;

    private readonly ScubaProProtocol _protocol = new();

    private static readonly DateTime Start = new(2026, 6, 22, 10, 0, 0, DateTimeKind.Utc);

    private static byte[] HeaderPayload(
        DateTime startUtc, int durationSec, int intervalSec, int o2, int he, int baseDepthCm, int baseTempCenti, int baseTankDbar)
    {
        var p = new byte[15];
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(0, 4), BleFrameBuilder.UnixSeconds(startUtc));
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(4, 2), (ushort)durationSec);
        p[6] = (byte)intervalSec;
        p[7] = (byte)o2;
        p[8] = (byte)he;
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(9, 2), (ushort)baseDepthCm);
        BinaryPrimitives.WriteInt16LittleEndian(p.AsSpan(11, 2), (short)baseTempCenti);
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(13, 2), (ushort)baseTankDbar);
        return p;
    }

    private static byte[] Delta(int depthDeltaCm, int tempDeltaDeci, int tankDeltaDeci)
    {
        var r = new byte[4];
        BinaryPrimitives.WriteInt16LittleEndian(r.AsSpan(0, 2), (short)depthDeltaCm);
        r[2] = (byte)(sbyte)tempDeltaDeci;
        r[3] = (byte)(sbyte)tankDeltaDeci;
        return r;
    }

    private static byte[] Batch(params byte[][] deltas) => deltas.SelectMany(d => d).ToArray();

    private static DiveComputerFrame HFrame(byte[] payload) => BleFrameBuilder.AdditiveFrame1(Magic, Header, payload);

    private static DiveComputerFrame SFrame(byte[] payload) => BleFrameBuilder.AdditiveFrame1(Magic, Samples, payload);

    private static DiveComputerFrame EFrame() => BleFrameBuilder.AdditiveFrame1(Magic, End, []);

    /// <summary>Base 0 m / 26 °C / 200 bar, then three deltas across two batches.</summary>
    private static IReadOnlyList<DiveComputerFrame> DeltaDive(int baseTankDbar = 2000) =>
    [
        HFrame(HeaderPayload(Start, durationSec: 40, intervalSec: 10, o2: 32, he: 0,
            baseDepthCm: 0, baseTempCenti: 2600, baseTankDbar: baseTankDbar)),
        SFrame(Batch(Delta(+1000, -20, -50), Delta(+800, -30, -60))),
        SFrame(Batch(Delta(-1500, +10, -40))),
        EFrame(),
    ];

    [Fact]
    public async Task Reconstructs_absolute_channels_from_deltas()
    {
        var dives = await _protocol.ParseAsync(AsyncStream.From(DeltaDive()));
        var dive = Assert.Single(dives);

        Assert.Equal(Start, dive.StartUtc);
        Assert.Equal(40, dive.DurationSec);
        Assert.Equal("EAN32", dive.GasMix);
        Assert.Equal(4, dive.Samples.Count);

        Assert.Equal(new[] { 0, 10, 20, 30 }, dive.Samples.Select(s => s.TSeconds));
        Assert.Equal(new[] { 0.0, 10.0, 18.0, 3.0 }, dive.Samples.Select(s => Math.Round(s.DepthM, 2)));
        Assert.Equal(new[] { 26.0, 24.0, 21.0, 22.0 }, dive.Samples.Select(s => Math.Round(s.TempC!.Value, 2)));
        Assert.Equal(new[] { 200.0, 195.0, 189.0, 185.0 }, dive.Samples.Select(s => Math.Round(s.TankPressureBar!.Value, 2)));

        Assert.Equal(18.0, dive.MaxDepthM, 3);
        Assert.Equal(7.75, dive.AvgDepthM!.Value, 3);
        Assert.Equal(21.0, dive.MinTempC!.Value, 3);
    }

    [Fact]
    public async Task Single_gas_cylinder_carries_tank_trace()
    {
        var dives = await _protocol.ParseAsync(AsyncStream.From(DeltaDive()));
        var cylinder = Assert.Single(Assert.Single(dives).Cylinders!);

        Assert.Equal(32, cylinder.O2!.Value, 3);
        Assert.Null(cylinder.He);
        Assert.Equal(200.0, cylinder.StartBar!.Value, 3);
        Assert.Equal(185.0, cylinder.EndBar!.Value, 3);
    }

    [Fact]
    public async Task No_transmitter_leaves_tank_null_and_cylinder_pressureless()
    {
        var dives = await _protocol.ParseAsync(AsyncStream.From(DeltaDive(baseTankDbar: 0xFFFF)));
        var dive = Assert.Single(dives);

        Assert.All(dive.Samples, s => Assert.Null(s.TankPressureBar));
        var cylinder = Assert.Single(dive.Cylinders!);
        Assert.Equal(32, cylinder.O2!.Value, 3);
        Assert.Null(cylinder.StartBar);
        Assert.Null(cylinder.EndBar);
    }

    [Fact]
    public async Task Checksum_mismatch_throws()
    {
        var frames = DeltaDive().ToArray();
        var corrupt = frames[1].Payload.ToArray();
        corrupt[4] ^= 0xFF; // flip a delta byte
        frames[1] = new DiveComputerFrame(corrupt);

        var ex = await Assert.ThrowsAsync<DiveComputerProtocolException>(() => _protocol.ParseAsync(AsyncStream.From(frames)));
        Assert.Contains("checksum", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unknown_frame_type_throws()
    {
        var frames = new[] { BleFrameBuilder.AdditiveFrame1(Magic, 0x7F, [0x00]) };
        var ex = await Assert.ThrowsAsync<DiveComputerProtocolException>(() => _protocol.ParseAsync(AsyncStream.From(frames)));
        Assert.Contains("Unknown ScubaPro frame type", ex.Message);
    }

    [Fact]
    public async Task Batch_before_header_throws()
    {
        var frames = new[] { SFrame(Batch(Delta(0, 0, 0))) };
        var ex = await Assert.ThrowsAsync<DiveComputerProtocolException>(() => _protocol.ParseAsync(AsyncStream.From(frames)));
        Assert.Contains("before any dive header", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Negative_reconstructed_depth_throws()
    {
        var frames = new[]
        {
            HFrame(HeaderPayload(Start, 20, 10, 21, 0, baseDepthCm: 0, baseTempCenti: 2600, baseTankDbar: 2000)),
            SFrame(Batch(Delta(-100, 0, 0))), // 0 - 1.00 m = negative
        };
        var ex = await Assert.ThrowsAsync<DiveComputerProtocolException>(() => _protocol.ParseAsync(AsyncStream.From(frames)));
        Assert.Contains("negative depth", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Stream_ending_mid_dive_throws()
    {
        var frames = DeltaDive().ToList();
        frames.RemoveAt(frames.Count - 1); // drop the end frame
        var ex = await Assert.ThrowsAsync<DiveComputerProtocolException>(() => _protocol.ParseAsync(AsyncStream.From(frames)));
        Assert.Contains("mid-dive", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
