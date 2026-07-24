using System.Buffers.Binary;
using Trimix.Contracts.Dtos;
using Trimix.Core.Abstractions;

namespace Trimix.Core.DiveComputer.Protocols;

/// <summary>
/// Mares Genius / Icon HD / Sirius (the "Icon HD" protocol family) parser — clean-room C#, no
/// libdivecomputer code. Mares Icon HD memory stores a header followed by fixed-interval sample records;
/// the Genius adds a per-sample no-deco (NDL) value and a hose-transmitter tank channel. This parser decodes
/// that shape: a header with the programmed gas and interval, then fixed 8-byte sample records carrying
/// depth, temperature, NDL, and tank pressure. The single programmed mix is surfaced as one cylinder whose
/// start/end pressure is taken from the tank trace.
///
/// ── FRAME CONTRACT (best-effort; needs M3 real-hardware capture validation — see ProtocolPrimitives) ──
///
/// Every <see cref="DiveComputerFrame"/> (little-endian):
///   0     1    magic 0x4D ('M')
///   1     1    type: 0x01 header · 0x02 samples · 0x03 end
///   2     2    payload length N (u16)
///   4     N    payload
///   4+N   1    XOR checksum of bytes [0 .. 4+N)
///
/// Header (0x01, N=9): 0:4 start unix sec UTC (u32) · 4:2 duration sec (u16) · 6:1 sample interval sec ·
///   7:1 O2 % · 8:1 He %.
/// Samples (0x02, N = 8·k): k records, each 0:2 depth cm (u16) · 2:2 temp centi-°C (i16) ·
///   4:2 NDL minutes (u16; 0xFFFF = in-deco/n-a) · 6:2 tank pressure deci-bar (u16; 0xFFFF = no transmitter).
/// End (0x03, N=0): closes the dive.
///
/// Strict like <see cref="ShearwaterProtocol"/>: bad magic/length/checksum, unknown type, samples/end with no
/// open dive, a second header before end, a zero interval, zero samples, or a stream that ends mid-dive all
/// throw <see cref="DiveComputerProtocolException"/>.
/// </summary>
public sealed class MaresProtocol : IDiveComputerProtocol
{
    private const byte Magic = 0x4D;
    private const byte TypeHeader = 0x01;
    private const byte TypeSamples = 0x02;
    private const byte TypeEnd = 0x03;

    public DiveComputerVendor Vendor => DiveComputerVendor.Mares;

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
                        throw new DiveComputerProtocolException("New Mares dive header before the previous dive's end frame.");
                    }

                    current = ParseHeader(payload);
                    break;
                case TypeSamples:
                    (current ?? throw new DiveComputerProtocolException("Mares samples before any dive header."))
                        .AddSamples(payload);
                    break;
                case TypeEnd:
                    if (payload.Length != 0)
                    {
                        throw new DiveComputerProtocolException($"Mares end payload must be empty, got {payload.Length} bytes.");
                    }

                    completed.Add(
                        (current ?? throw new DiveComputerProtocolException("Mares end frame before any dive header.")).Build());
                    current = null;
                    break;
                default:
                    throw new DiveComputerProtocolException($"Unknown Mares frame type 0x{type:X2}.");
            }
        }

        if (current is not null)
        {
            throw new DiveComputerProtocolException("Mares stream ended mid-dive (no end frame — truncated download).");
        }

        return completed;
    }

    private static (byte Type, ReadOnlyMemory<byte> Payload) ValidateAndUnwrap(DiveComputerFrame frame)
    {
        var bytes = frame.Payload;
        var span = bytes.Span;
        if (span.Length < 5)
        {
            throw new DiveComputerProtocolException($"Truncated Mares frame: {span.Length} bytes is below the 5-byte envelope minimum.");
        }

        if (span[0] != Magic)
        {
            throw new DiveComputerProtocolException("Bad Mares frame magic (expected 'M').");
        }

        int declared = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(2, 2));
        if (span.Length != 5 + declared)
        {
            throw new DiveComputerProtocolException(
                $"Truncated Mares frame: envelope declares {declared} payload bytes but {span.Length - 5} arrived.");
        }

        var expected = ProtocolPrimitives.Xor(span[..^1]);
        if (span[^1] != expected)
        {
            throw new DiveComputerProtocolException(
                $"Mares checksum mismatch (expected 0x{expected:X2}, got 0x{span[^1]:X2}) — corrupt frame.");
        }

        return (span[1], bytes.Slice(4, declared));
    }

    private static DiveBuilder ParseHeader(ReadOnlyMemory<byte> payloadMem)
    {
        var payload = payloadMem.Span;
        if (payload.Length != 9)
        {
            throw new DiveComputerProtocolException($"Mares header payload must be 9 bytes, got {payload.Length}.");
        }

        var interval = payload[6];
        if (interval == 0)
        {
            throw new DiveComputerProtocolException("Mares header declares a zero sample interval (degenerate/corrupt frame).");
        }

        return new DiveBuilder(
            DateTimeOffset.FromUnixTimeSeconds(BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4))).UtcDateTime,
            BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(4, 2)),
            interval,
            payload[7],
            payload[8]);
    }

    private sealed class DiveBuilder(DateTime startUtc, int durationSec, int intervalSec, int o2, int he)
    {
        private readonly List<ProfileSampleDto> _samples = [];

        public void AddSamples(ReadOnlyMemory<byte> payloadMem)
        {
            var payload = payloadMem.Span;
            if (payload.Length % 8 != 0)
            {
                throw new DiveComputerProtocolException(
                    $"Mares sample block must be a whole number of 8-byte records, got {payload.Length} bytes.");
            }

            var count = payload.Length / 8;
            for (var i = 0; i < count; i++)
            {
                var record = payload.Slice(i * 8, 8);
                var depthM = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(0, 2)) / 100.0;
                var tempC = BinaryPrimitives.ReadInt16LittleEndian(record.Slice(2, 2)) / 100.0;

                var ndlRaw = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(4, 2));
                double? ndl = ndlRaw == ProtocolPrimitives.U16NotAvailable ? null : ndlRaw;

                var tankRaw = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(6, 2));
                double? tankBar = tankRaw == ProtocolPrimitives.U16NotAvailable ? null : tankRaw / 10.0;

                _samples.Add(new ProfileSampleDto(
                    _samples.Count * intervalSec,
                    depthM,
                    tempC,
                    PpO2: null,
                    Ndl: ndl,
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
                throw new DiveComputerProtocolException("Mares dive closed with zero samples (empty/corrupt download).");
            }

            var tankSamples = _samples.Where(s => s.TankPressureBar is not null).ToList();
            var cylinder = tankSamples.Count > 0
                ? new CylinderDto(o2, he > 0 ? he : null, tankSamples[0].TankPressureBar, tankSamples[^1].TankPressureBar)
                : new CylinderDto(o2, he > 0 ? he : null);
            IReadOnlyList<CylinderDto> cylinders = [cylinder];

            return new ImportedDive(
                startUtc,
                durationSec,
                MaxDepthM: _samples.Max(s => s.DepthM),
                AvgDepthM: Math.Round(_samples.Average(s => s.DepthM), 2),
                MinTempC: _samples.Min(s => s.TempC),
                GasMix: ProtocolPrimitives.FormatGasMix(o2, he),
                Samples: _samples,
                Cylinders: cylinders);
        }
    }
}
