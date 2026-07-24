using System.Buffers.Binary;
using Trimix.Core.Abstractions;

namespace Trimix.Core.DiveComputer;

/// <summary>
/// Deterministic fixture frames for a simulated Shearwater Perdix 2, embedded in Core
/// (architecture overview §4). SimulatorTransport streams these; ShearwaterProtocol parses them;
/// the unit tests assert against the <see cref="Dives"/> metadata below.
///
/// ── Fixture frame format (documented contract between SimulatorTransport and ShearwaterProtocol) ──
///
/// Every frame is one <see cref="DiveComputerFrame"/> payload with this layout (little-endian):
///
///   offset  size  field
///   0       1     magic 'S' (0x53)
///   1       1     magic 'W' (0x57)
///   2       1     frame type: 0x01 dive header · 0x02 sample block · 0x03 dive end
///   3       1     N = payload length in bytes
///   4       N     payload (see below)
///   4+N     1     checksum: XOR of all preceding bytes [0 .. 4+N)
///
/// Dive header payload (type 0x01, N = 14):
///   0       1     dive index
///   1       4     dive start, unix seconds UTC (u32)
///   5       4     duration in seconds (u32)
///   9       1     sample interval in seconds
///   10      2     sample count (u16)
///   12      1     O2 fraction of the programmed gas, percent
///   13      1     He fraction of the programmed gas, percent
///
/// Sample block payload (type 0x02, N = 4 + 4·count):
///   0       1     dive index
///   1       2     index of the first sample in this block (u16)
///   3       1     count of samples in this block (≤ 60)
///   4+4i    2     sample depth in centimeters (u16)
///   6+4i    2     sample water temperature in deci-°C (i16)
///
/// Dive end payload (type 0x03, N = 1):
///   0       1     dive index
///
/// ⚠ M3 RISK (stated honestly, per the architecture doc): this format is OUR simulator fixture
/// contract, NOT the real Shearwater BLE protocol. It exercises the parsing/import pipeline
/// end-to-end, but field validation against real Perdix hardware in post-dive conditions is an
/// open M3 gate — the real protocol integration replaces the transport, keeping this parser shape.
/// </summary>
public static class ShearwaterFixtures
{
    public const byte Magic0 = 0x53; // 'S'
    public const byte Magic1 = 0x57; // 'W'
    public const byte FrameTypeHeader = 0x01;
    public const byte FrameTypeSamples = 0x02;
    public const byte FrameTypeEnd = 0x03;
    public const int MaxSamplesPerBlock = 60;

    /// <summary>The one computer the simulator "discovers".</summary>
    public static readonly DiscoveredComputer Computer =
        new(DiveComputerVendor.Shearwater, "Perdix 2", "SIM-PERDIX2-0001");

    /// <summary>Declarative source of truth for the generated frames (asserted by tests).</summary>
    public sealed record FixtureDive(
        DateTime StartUtc,
        int DurationSec,
        int SampleIntervalSec,
        double MaxDepthM,
        byte O2Percent,
        byte HePercent)
    {
        public int SampleCount => DurationSec / SampleIntervalSec;
    }

    /// <summary>
    /// Five dives — deliberately more than <see cref="Trimix.Contracts.FreeTier.FreeImportDivesPerSession"/>
    /// so a free-tier import session trips the purchase moment.
    /// </summary>
    public static readonly IReadOnlyList<FixtureDive> Dives =
    [
        new(new DateTime(2026, 6, 20, 8, 30, 0, DateTimeKind.Utc), 2580, 10, 18.3, 21, 0),
        new(new DateTime(2026, 6, 20, 14, 5, 0, DateTimeKind.Utc), 2700, 10, 32.4, 32, 0),
        new(new DateTime(2026, 6, 21, 9, 12, 0, DateTimeKind.Utc), 2160, 10, 24.6, 21, 35),
        new(new DateTime(2026, 6, 22, 10, 40, 0, DateTimeKind.Utc), 1980, 10, 41.2, 18, 45),
        new(new DateTime(2026, 6, 23, 7, 55, 0, DateTimeKind.Utc), 3000, 10, 15.8, 28, 0),
    ];

    /// <summary>
    /// Deterministic depth (meters) of sample <paramref name="sampleIndex"/>: descend (15% of the
    /// runtime), flat bottom (to 75%), ascend to a 5 m safety stop (85–97%), surface by the end.
    /// </summary>
    public static double DepthAt(FixtureDive dive, int sampleIndex)
    {
        var f = (double)(sampleIndex * dive.SampleIntervalSec) / dive.DurationSec;
        var stop = Math.Min(5.0, dive.MaxDepthM);
        var depth = f switch
        {
            < 0.15 => dive.MaxDepthM * (f / 0.15),
            < 0.75 => dive.MaxDepthM,
            < 0.85 => dive.MaxDepthM + (stop - dive.MaxDepthM) * ((f - 0.75) / 0.10),
            < 0.97 => stop,
            _ => stop * (1.0 - (f - 0.97) / 0.03),
        };
        return Math.Round(Math.Max(0, depth) * 100) / 100; // centimeter resolution, like the wire format
    }

    /// <summary>Deterministic water temperature (°C) at <paramref name="depthM"/>: 28 °C surface, −0.15 °C/m.</summary>
    public static double TempAt(double depthM) => Math.Round((28.0 - 0.15 * depthM) * 10) / 10;

    /// <summary>Builds the full deterministic frame stream for all <see cref="Dives"/>.</summary>
    public static IReadOnlyList<DiveComputerFrame> BuildFrames()
    {
        var frames = new List<DiveComputerFrame>();
        for (byte diveIndex = 0; diveIndex < Dives.Count; diveIndex++)
        {
            var dive = Dives[diveIndex];
            frames.Add(BuildHeaderFrame(diveIndex, dive));

            for (var first = 0; first < dive.SampleCount; first += MaxSamplesPerBlock)
            {
                var count = Math.Min(MaxSamplesPerBlock, dive.SampleCount - first);
                frames.Add(BuildSampleFrame(diveIndex, dive, first, count));
            }

            frames.Add(BuildFrame(FrameTypeEnd, [diveIndex]));
        }

        return frames;
    }

    private static DiveComputerFrame BuildHeaderFrame(byte diveIndex, FixtureDive dive)
    {
        var payload = new byte[14];
        payload[0] = diveIndex;
        BinaryPrimitives.WriteUInt32LittleEndian(
            payload.AsSpan(1, 4),
            (uint)new DateTimeOffset(dive.StartUtc).ToUnixTimeSeconds());
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(5, 4), (uint)dive.DurationSec);
        payload[9] = (byte)dive.SampleIntervalSec;
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(10, 2), (ushort)dive.SampleCount);
        payload[12] = dive.O2Percent;
        payload[13] = dive.HePercent;
        return BuildFrame(FrameTypeHeader, payload);
    }

    private static DiveComputerFrame BuildSampleFrame(byte diveIndex, FixtureDive dive, int firstSample, int count)
    {
        var payload = new byte[4 + count * 4];
        payload[0] = diveIndex;
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(1, 2), (ushort)firstSample);
        payload[3] = (byte)count;
        for (var i = 0; i < count; i++)
        {
            var depth = DepthAt(dive, firstSample + i);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4 + i * 4, 2), (ushort)Math.Round(depth * 100));
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(6 + i * 4, 2), (short)Math.Round(TempAt(depth) * 10));
        }

        return BuildFrame(FrameTypeSamples, payload);
    }

    /// <summary>Wraps a payload with magic, type, length, and XOR checksum (the frame layout above).</summary>
    public static DiveComputerFrame BuildFrame(byte frameType, ReadOnlySpan<byte> payload)
    {
        var bytes = new byte[5 + payload.Length];
        bytes[0] = Magic0;
        bytes[1] = Magic1;
        bytes[2] = frameType;
        bytes[3] = (byte)payload.Length;
        payload.CopyTo(bytes.AsSpan(4));
        bytes[^1] = Checksum(bytes.AsSpan(0, bytes.Length - 1));
        return new DiveComputerFrame(bytes);
    }

    /// <summary>XOR of all bytes.</summary>
    public static byte Checksum(ReadOnlySpan<byte> bytes)
    {
        byte checksum = 0;
        foreach (var b in bytes)
        {
            checksum ^= b;
        }

        return checksum;
    }
}
