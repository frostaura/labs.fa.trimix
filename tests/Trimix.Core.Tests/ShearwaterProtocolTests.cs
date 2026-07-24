using System.Buffers.Binary;
using Trimix.Core.Abstractions;
using Trimix.Core.DiveComputer;
using Trimix.Core.Tests.Fakes;
using Xunit;

namespace Trimix.Core.Tests;

/// <summary>ShearwaterProtocol against the embedded fixture frames: happy path + truncated/corrupt frames.</summary>
public class ShearwaterProtocolTests
{
    private readonly ShearwaterProtocol _protocol = new();

    [Fact]
    public async Task Parses_all_fixture_dives_with_correct_metadata()
    {
        var dives = await _protocol.ParseAsync(AsyncStream.From(ShearwaterFixtures.BuildFrames()));

        Assert.Equal(ShearwaterFixtures.Dives.Count, dives.Count);
        for (var i = 0; i < dives.Count; i++)
        {
            var expected = ShearwaterFixtures.Dives[i];
            Assert.Equal(expected.StartUtc, dives[i].StartUtc);
            Assert.Equal(expected.DurationSec, dives[i].DurationSec);
            Assert.Equal(expected.SampleCount, dives[i].Samples.Count);
            Assert.Equal(expected.MaxDepthM, dives[i].MaxDepthM, 3);
            Assert.NotNull(dives[i].AvgDepthM);
            Assert.True(dives[i].AvgDepthM < dives[i].MaxDepthM);
            // Coldest water is at max depth: 28 °C surface − 0.15 °C/m.
            Assert.Equal(ShearwaterFixtures.TempAt(expected.MaxDepthM), dives[i].MinTempC!.Value, 1);
        }
    }

    [Fact]
    public async Task Formats_gas_mixes_diver_native()
    {
        var dives = await _protocol.ParseAsync(AsyncStream.From(ShearwaterFixtures.BuildFrames()));

        Assert.Equal("Air", dives[0].GasMix);      // 21/0
        Assert.Equal("EAN32", dives[1].GasMix);    // 32/0
        Assert.Equal("21/35", dives[2].GasMix);    // trimix
        Assert.Equal("18/45", dives[3].GasMix);    // hypoxic trimix
        Assert.Equal("EAN28", dives[4].GasMix);
    }

    [Fact]
    public async Task Samples_are_time_ordered_with_interval_spacing()
    {
        var dives = await _protocol.ParseAsync(AsyncStream.From(ShearwaterFixtures.BuildFrames()));
        var samples = dives[0].Samples;

        Assert.Equal(0, samples[0].TSeconds);
        Assert.Equal(ShearwaterFixtures.Dives[0].SampleIntervalSec, samples[1].TSeconds - samples[0].TSeconds);
        Assert.All(samples, s => Assert.True(s.DepthM >= 0));
    }

    [Fact]
    public async Task Truncated_frame_throws()
    {
        var frames = ShearwaterFixtures.BuildFrames().ToList();
        var truncated = frames[0].Payload[..^2]; // chop the last two bytes off the header frame
        frames[0] = new DiveComputerFrame(truncated);

        var ex = await Assert.ThrowsAsync<DiveComputerProtocolException>(
            () => _protocol.ParseAsync(AsyncStream.From(frames)));
        Assert.Contains("Truncated", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Corrupt_frame_fails_checksum()
    {
        var frames = ShearwaterFixtures.BuildFrames().ToList();
        var corrupt = frames[1].Payload.ToArray();
        corrupt[6] ^= 0xFF; // flip depth bits inside the payload without fixing the checksum
        frames[1] = new DiveComputerFrame(corrupt);

        var ex = await Assert.ThrowsAsync<DiveComputerProtocolException>(
            () => _protocol.ParseAsync(AsyncStream.From(frames)));
        Assert.Contains("Checksum", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unknown_frame_type_throws()
    {
        var frames = new[] { ShearwaterFixtures.BuildFrame(0x7F, [0x00]) };

        var ex = await Assert.ThrowsAsync<DiveComputerProtocolException>(
            () => _protocol.ParseAsync(AsyncStream.From(frames)));
        Assert.Contains("Unknown frame type", ex.Message);
    }

    [Fact]
    public async Task Stream_ending_mid_dive_throws()
    {
        // Everything except the very last end frame: dive 4 never completes.
        var frames = ShearwaterFixtures.BuildFrames().ToList();
        frames.RemoveAt(frames.Count - 1);

        var ex = await Assert.ThrowsAsync<DiveComputerProtocolException>(
            () => _protocol.ParseAsync(AsyncStream.From(frames)));
        Assert.Contains("end frame", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Builds a single checksum-valid 14-byte header frame with the given interval/count.</summary>
    private static DiveComputerFrame HeaderFrame(byte sampleIntervalSec, ushort sampleCount)
    {
        var payload = new byte[14];
        payload[0] = 0; // dive index
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(1, 4), 1_700_000_000);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(5, 4), 1200);
        payload[9] = sampleIntervalSec;
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(10, 2), sampleCount);
        payload[12] = 21;
        payload[13] = 0;
        return ShearwaterFixtures.BuildFrame(ShearwaterFixtures.FrameTypeHeader, payload);
    }

    [Fact]
    public async Task Zero_sample_count_header_throws_in_contract_not_InvalidOperation()
    {
        // Degenerate but checksum-valid: SampleCount=0 would make Build()'s samples.Max() throw
        // InvalidOperationException outside the parser's contract; must be a protocol exception.
        var frames = new[] { HeaderFrame(sampleIntervalSec: 10, sampleCount: 0) };

        var ex = await Assert.ThrowsAsync<DiveComputerProtocolException>(
            () => _protocol.ParseAsync(AsyncStream.From(frames)));
        Assert.Contains("zero samples", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Zero_sample_interval_header_throws_in_contract()
    {
        // SampleIntervalSec=0 would stamp every sample at TSeconds=0 and violate the server PK.
        var frames = new[] { HeaderFrame(sampleIntervalSec: 0, sampleCount: 10) };

        var ex = await Assert.ThrowsAsync<DiveComputerProtocolException>(
            () => _protocol.ParseAsync(AsyncStream.From(frames)));
        Assert.Contains("interval", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Sample_block_for_unknown_dive_throws()
    {
        var frames = ShearwaterFixtures.BuildFrames().Skip(1).ToList(); // drop dive 0's header

        var ex = await Assert.ThrowsAsync<DiveComputerProtocolException>(
            () => _protocol.ParseAsync(AsyncStream.From(frames)));
        Assert.Contains("unknown dive", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Garmin_remains_a_notsupported_ble_placeholder()
    {
        // Garmin blocks third-party BLE dive download (FIT/cloud only); Suunto/ScubaPro/Aqualung/Mares now
        // have real clean-room parsers (see Connectors/Ble/*ProtocolTests), so only Garmin still throws.
        var garmin = new GarminProtocol();
        Assert.Equal(DiveComputerVendor.Garmin, garmin.Vendor);
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            garmin.ParseAsync(AsyncStream.From(Array.Empty<DiveComputerFrame>())));
    }
}
