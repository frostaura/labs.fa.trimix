using System.Buffers.Binary;
using Trimix.Core.Abstractions;
using Trimix.Core.DiveComputer;
using Trimix.Core.DiveComputer.Protocols;
using Trimix.Core.Tests.Fakes;
using Xunit;

namespace Trimix.Core.Tests.Connectors.Ble;

/// <summary>
/// <see cref="AqualungProtocol"/> against committed Oceanic/Pelagic fixed-record fixtures: gas-table
/// header, per-record gas-index resolution, whole-bar tank channel (i770R transmitter), no-transmitter
/// fallback, and the strict error contract.
/// </summary>
public class AqualungProtocolTests
{
    private const byte Magic = 0x41;
    private const byte Header = 0x01;
    private const byte Samples = 0x02;
    private const byte End = 0x03;

    private readonly AqualungProtocol _protocol = new();

    private static readonly DateTime Start = new(2026, 6, 23, 7, 45, 0, DateTimeKind.Utc);

    private static byte[] HeaderPayload(DateTime startUtc, int durationSec, int intervalSec, params (int O2, int He)[] gases)
    {
        var p = new byte[8 + gases.Length * 2];
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(0, 4), BleFrameBuilder.UnixSeconds(startUtc));
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(4, 2), (ushort)durationSec);
        p[6] = (byte)intervalSec;
        p[7] = (byte)gases.Length;
        for (var i = 0; i < gases.Length; i++)
        {
            p[8 + i * 2] = (byte)gases[i].O2;
            p[9 + i * 2] = (byte)gases[i].He;
        }

        return p;
    }

    private static byte[] Record(int depthCm, int tempCenti, int tankBar, int gasIndex)
    {
        var r = new byte[6];
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(0, 2), (ushort)depthCm);
        BinaryPrimitives.WriteInt16LittleEndian(r.AsSpan(2, 2), (short)tempCenti);
        r[4] = (byte)tankBar;
        r[5] = (byte)gasIndex;
        return r;
    }

    private static byte[] Block(params byte[][] records) => records.SelectMany(r => r).ToArray();

    private static DiveComputerFrame HFrame(byte[] payload) => BleFrameBuilder.XorFrame2(Magic, Header, payload);

    private static DiveComputerFrame SFrame(byte[] payload) => BleFrameBuilder.XorFrame2(Magic, Samples, payload);

    private static DiveComputerFrame EFrame() => BleFrameBuilder.XorFrame2(Magic, End, []);

    private static IReadOnlyList<DiveComputerFrame> TwoGasDive() =>
    [
        HFrame(HeaderPayload(Start, durationSec: 30, intervalSec: 10, (21, 0), (50, 0))),
        SFrame(Block(
            Record(0, 2400, 210, 0),
            Record(1500, 2000, 200, 0),
            Record(2500, 1600, 190, 0),
            Record(500, 1900, 185, 1))),
        EFrame(),
    ];

    [Fact]
    public async Task Decodes_records_and_summary()
    {
        var dives = await _protocol.ParseAsync(AsyncStream.From(TwoGasDive()));
        var dive = Assert.Single(dives);

        Assert.Equal(Start, dive.StartUtc);
        Assert.Equal(30, dive.DurationSec);
        Assert.Equal("Air", dive.GasMix);
        Assert.Equal(new[] { 0, 10, 20, 30 }, dive.Samples.Select(s => s.TSeconds));
        Assert.Equal(new[] { 0.0, 15.0, 25.0, 5.0 }, dive.Samples.Select(s => Math.Round(s.DepthM, 2)));
        Assert.Equal(new[] { 210.0, 200.0, 190.0, 185.0 }, dive.Samples.Select(s => s.TankPressureBar!.Value));
        Assert.Equal(25.0, dive.MaxDepthM, 3);
        Assert.Equal(11.25, dive.AvgDepthM!.Value, 3);
        Assert.Equal(16.0, dive.MinTempC!.Value, 3);
    }

    [Fact]
    public async Task Emits_a_cylinder_per_table_gas_with_tank_trace_on_the_first()
    {
        var dives = await _protocol.ParseAsync(AsyncStream.From(TwoGasDive()));
        var cylinders = Assert.Single(dives).Cylinders!;

        Assert.Equal(2, cylinders.Count);
        Assert.Equal(21, cylinders[0].O2!.Value, 3);
        Assert.Equal(210.0, cylinders[0].StartBar!.Value, 3);
        Assert.Equal(185.0, cylinders[0].EndBar!.Value, 3);
        Assert.Equal(50, cylinders[1].O2!.Value, 3);
        Assert.Null(cylinders[1].StartBar);
    }

    [Fact]
    public async Task No_transmitter_record_leaves_tank_null()
    {
        IReadOnlyList<DiveComputerFrame> frames =
        [
            HFrame(HeaderPayload(Start, 20, 10, (21, 0))),
            SFrame(Block(Record(0, 2400, 0xFF, 0), Record(1200, 2000, 0xFF, 0))),
            EFrame(),
        ];

        var dive = Assert.Single(await _protocol.ParseAsync(AsyncStream.From(frames)));
        Assert.All(dive.Samples, s => Assert.Null(s.TankPressureBar));
        var cylinder = Assert.Single(dive.Cylinders!);
        Assert.Null(cylinder.StartBar);
    }

    [Fact]
    public async Task Gas_index_out_of_range_throws()
    {
        IReadOnlyList<DiveComputerFrame> frames =
        [
            HFrame(HeaderPayload(Start, 20, 10, (21, 0))),
            SFrame(Block(Record(0, 2400, 200, 3))), // only 1 gas in table
        ];
        var ex = await Assert.ThrowsAsync<DiveComputerProtocolException>(() => _protocol.ParseAsync(AsyncStream.From(frames)));
        Assert.Contains("outside", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Gas_count_out_of_range_throws()
    {
        // Declared gas count 0 is invalid; build the payload by hand (helper can't express 0 gases meaningfully).
        var payload = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), BleFrameBuilder.UnixSeconds(Start));
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4, 2), 20);
        payload[6] = 10;
        payload[7] = 0; // zero gases
        var frames = new[] { HFrame(payload) };
        var ex = await Assert.ThrowsAsync<DiveComputerProtocolException>(() => _protocol.ParseAsync(AsyncStream.From(frames)));
        Assert.Contains("gases", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Checksum_mismatch_throws()
    {
        var frames = TwoGasDive().ToArray();
        var corrupt = frames[1].Payload.ToArray();
        corrupt[4] ^= 0xFF;
        frames[1] = new DiveComputerFrame(corrupt);
        var ex = await Assert.ThrowsAsync<DiveComputerProtocolException>(() => _protocol.ParseAsync(AsyncStream.From(frames)));
        Assert.Contains("checksum", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unknown_frame_type_throws()
    {
        var frames = new[] { BleFrameBuilder.XorFrame2(Magic, 0x7F, [0x00]) };
        var ex = await Assert.ThrowsAsync<DiveComputerProtocolException>(() => _protocol.ParseAsync(AsyncStream.From(frames)));
        Assert.Contains("Unknown Aqualung frame type", ex.Message);
    }

    [Fact]
    public async Task Stream_ending_mid_dive_throws()
    {
        var frames = TwoGasDive().ToList();
        frames.RemoveAt(frames.Count - 1);
        var ex = await Assert.ThrowsAsync<DiveComputerProtocolException>(() => _protocol.ParseAsync(AsyncStream.From(frames)));
        Assert.Contains("mid-dive", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
