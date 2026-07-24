using System.Buffers.Binary;
using Trimix.Contracts.Dtos;
using Trimix.Core.Abstractions;

namespace Trimix.Core.DiveComputer.Protocols;

/// <summary>
/// Aqualung i-series (i770R / i550C / i330R, Pelagic/Oceanic "Atom" protocol family) parser — clean-room
/// C#, no libdivecomputer code. Oceanic/Pelagic memory stores a compact gas table in the header followed by
/// fixed-width sample records that reference a gas by index; the i770R adds a hose-transmitter tank channel.
/// This parser decodes that shape: a header with a 1..3 entry gas table, then fixed 6-byte sample records
/// carrying depth, temperature, whole-bar tank pressure, and the active gas index. Every table gas becomes
/// a <see cref="CylinderDto"/>; the tank-pressure trace's start/end attaches to the first table gas.
///
/// ── FRAME CONTRACT (best-effort; needs M3 real-hardware capture validation — see ProtocolPrimitives) ──
///
/// Every <see cref="DiveComputerFrame"/> (little-endian):
///   0     1    magic 0x41 ('A')
///   1     1    type: 0x01 header · 0x02 samples · 0x03 end
///   2     2    payload length N (u16)
///   4     N    payload
///   4+N   1    XOR checksum of bytes [0 .. 4+N)
///
/// Header (0x01, N = 8 + 2·G): 0:4 start unix sec UTC (u32) · 4:2 duration sec (u16) · 6:1 sample interval sec ·
///   7:1 gas count G (1..3) · then G gas entries, each 0:1 O2 % · 1:1 He %.
/// Samples (0x02, N = 6·k): k records, each 0:2 depth cm (u16) · 2:2 temp centi-°C (i16) ·
///   4:1 tank pressure whole bar (u8; 0xFF = no transmitter) · 5:1 active gas index (u8) into the header table.
/// End (0x03, N=0): closes the dive.
///
/// Strict like <see cref="ShearwaterProtocol"/>: bad magic/length/checksum, unknown type, samples/end with no
/// open dive, a second header before end, an out-of-range gas count or index, zero samples, or a stream that
/// ends mid-dive all throw <see cref="DiveComputerProtocolException"/>.
/// </summary>
public sealed class AqualungProtocol : IDiveComputerProtocol
{
    private const byte Magic = 0x41;
    private const byte TypeHeader = 0x01;
    private const byte TypeSamples = 0x02;
    private const byte TypeEnd = 0x03;
    private const int MaxGases = 3;

    public DiveComputerVendor Vendor => DiveComputerVendor.Aqualung;

    public async Task<IReadOnlyList<ImportedDive>> ParseAsync(
        IAsyncEnumerable<DiveComputerFrame> frames,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frames);

        var completed = new List<ImportedDive>();
        DiveBuilder? current = null;

        await foreach (var frame in frames.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            var (type, payload) = ValidateAndUnwrap(frame);
            switch (type)
            {
                case TypeHeader:
                    if (current is not null)
                    {
                        throw new DiveComputerProtocolException("New Aqualung dive header before the previous dive's end frame.");
                    }

                    current = ParseHeader(payload);
                    break;
                case TypeSamples:
                    (current ?? throw new DiveComputerProtocolException("Aqualung samples before any dive header."))
                        .AddSamples(payload);
                    break;
                case TypeEnd:
                    if (payload.Length != 0)
                    {
                        throw new DiveComputerProtocolException($"Aqualung end payload must be empty, got {payload.Length} bytes.");
                    }

                    completed.Add(
                        (current ?? throw new DiveComputerProtocolException("Aqualung end frame before any dive header.")).Build());
                    current = null;
                    break;
                default:
                    throw new DiveComputerProtocolException($"Unknown Aqualung frame type 0x{type:X2}.");
            }
        }

        if (current is not null)
        {
            throw new DiveComputerProtocolException("Aqualung stream ended mid-dive (no end frame — truncated download).");
        }

        return completed;
    }

    private static (byte Type, ReadOnlyMemory<byte> Payload) ValidateAndUnwrap(DiveComputerFrame frame)
    {
        var bytes = frame.Payload;
        var span = bytes.Span;
        if (span.Length < 5)
        {
            throw new DiveComputerProtocolException($"Truncated Aqualung frame: {span.Length} bytes is below the 5-byte envelope minimum.");
        }

        if (span[0] != Magic)
        {
            throw new DiveComputerProtocolException("Bad Aqualung frame magic (expected 'A').");
        }

        int declared = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(2, 2));
        if (span.Length != 5 + declared)
        {
            throw new DiveComputerProtocolException(
                $"Truncated Aqualung frame: envelope declares {declared} payload bytes but {span.Length - 5} arrived.");
        }

        var expected = ProtocolPrimitives.Xor(span[..^1]);
        if (span[^1] != expected)
        {
            throw new DiveComputerProtocolException(
                $"Aqualung checksum mismatch (expected 0x{expected:X2}, got 0x{span[^1]:X2}) — corrupt frame.");
        }

        return (span[1], bytes.Slice(4, declared));
    }

    private static DiveBuilder ParseHeader(ReadOnlyMemory<byte> payloadMem)
    {
        var payload = payloadMem.Span;
        if (payload.Length < 8)
        {
            throw new DiveComputerProtocolException($"Aqualung header must be at least 8 bytes, got {payload.Length}.");
        }

        var interval = payload[6];
        if (interval == 0)
        {
            throw new DiveComputerProtocolException("Aqualung header declares a zero sample interval (degenerate/corrupt frame).");
        }

        int gasCount = payload[7];
        if (gasCount is < 1 or > MaxGases)
        {
            throw new DiveComputerProtocolException($"Aqualung header declares {gasCount} gases (expected 1..{MaxGases}).");
        }

        if (payload.Length != 8 + gasCount * 2)
        {
            throw new DiveComputerProtocolException(
                $"Aqualung header declares {gasCount} gases but payload is {payload.Length} bytes (expected {8 + gasCount * 2}).");
        }

        var gases = new (int O2, int He)[gasCount];
        for (var i = 0; i < gasCount; i++)
        {
            gases[i] = (payload[8 + i * 2], payload[9 + i * 2]);
        }

        return new DiveBuilder(
            DateTimeOffset.FromUnixTimeSeconds(BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4))).UtcDateTime,
            BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(4, 2)),
            interval,
            gases);
    }

    private sealed class DiveBuilder(DateTime startUtc, int durationSec, int intervalSec, (int O2, int He)[] gases)
    {
        private readonly List<ProfileSampleDto> _samples = [];

        public void AddSamples(ReadOnlyMemory<byte> payloadMem)
        {
            var payload = payloadMem.Span;
            if (payload.Length % 6 != 0)
            {
                throw new DiveComputerProtocolException(
                    $"Aqualung sample block must be a whole number of 6-byte records, got {payload.Length} bytes.");
            }

            var count = payload.Length / 6;
            for (var i = 0; i < count; i++)
            {
                var record = payload.Slice(i * 6, 6);
                var depthM = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(0, 2)) / 100.0;
                var tempC = BinaryPrimitives.ReadInt16LittleEndian(record.Slice(2, 2)) / 100.0;
                var tankRaw = record[4];
                double? tankBar = tankRaw == ProtocolPrimitives.U8NotAvailable ? null : tankRaw;
                var gasIndex = record[5];
                if (gasIndex >= gases.Length)
                {
                    throw new DiveComputerProtocolException($"Aqualung sample references gas index {gasIndex} outside the {gases.Length}-gas table.");
                }

                _samples.Add(new ProfileSampleDto(
                    _samples.Count * intervalSec,
                    depthM,
                    tempC,
                    PpO2: null,
                    Ndl: null,
                    CeilingM: null,
                    AscentRateMPerMin: null,
                    Cns: null,
                    TankPressureBar: tankBar,
                    Setpoint: null,
                    HeartRate: null));
            }
        }

        public ImportedDive Build()
        {
            if (_samples.Count == 0)
            {
                throw new DiveComputerProtocolException("Aqualung dive closed with zero samples (empty/corrupt download).");
            }

            var tankSamples = _samples.Where(s => s.TankPressureBar is not null).ToList();
            var cylinders = new List<CylinderDto>(gases.Length);
            for (var i = 0; i < gases.Length; i++)
            {
                var (o2, he) = gases[i];
                cylinders.Add(new CylinderDto(
                    O2: o2,
                    He: he > 0 ? he : null,
                    StartBar: i == 0 && tankSamples.Count > 0 ? tankSamples[0].TankPressureBar : null,
                    EndBar: i == 0 && tankSamples.Count > 0 ? tankSamples[^1].TankPressureBar : null));
            }

            return new ImportedDive(
                startUtc,
                durationSec,
                MaxDepthM: _samples.Max(s => s.DepthM),
                AvgDepthM: Math.Round(_samples.Average(s => s.DepthM), 2),
                MinTempC: _samples.Min(s => s.TempC),
                GasMix: ProtocolPrimitives.FormatGasMix(gases[0].O2, gases[0].He),
                Samples: _samples,
                Cylinders: cylinders);
        }
    }
}
