using System.Buffers.Binary;
using Trimix.Contracts.Dtos;
using Trimix.Core.Abstractions;

namespace Trimix.Core.DiveComputer.Protocols;

/// <summary>
/// ScubaPro / Uwatec Galileo G2 / G3 (the "Smart" protocol family) parser — clean-room C#, no
/// libdivecomputer code. Uwatec's on-device memory is delta-compressed: an absolute base sample followed
/// by per-interval signed deltas. This parser reproduces that character faithfully — a header carries the
/// gas, interval, and absolute base for depth / temperature / tank pressure, then sample-batch frames carry
/// signed deltas the parser accumulates. It maps the Galileo channel set: depth, temperature, and (with a
/// Smart+ transmitter) tank pressure; gas is the single programmed mix, surfaced as one cylinder whose
/// start/end pressure is taken from the reconstructed tank trace.
///
/// ── FRAME CONTRACT (best-effort; needs M3 real-hardware capture validation — see ProtocolPrimitives) ──
///
/// Every <see cref="DiveComputerFrame"/> (little-endian):
///   0     1    magic 0x55 ('U')
///   1     1    type: 0x01 header · 0x02 sample batch · 0x03 end
///   2     1    payload length N
///   3     N    payload
///   3+N   1    additive (mod-256) checksum of bytes [0 .. 3+N)
///
/// Header (0x01, N=15): 0:4 start unix sec UTC (u32) · 4:2 duration sec (u16) · 6:1 sample interval sec ·
///   7:1 O2 % · 8:1 He % · 9:2 base depth cm (u16) · 11:2 base temp centi-°C (i16) ·
///   13:2 base tank pressure deci-bar (u16; 0xFFFF = no transmitter → whole trace null).
/// Sample batch (0x02, N=4·k): k records, each 0:2 depth delta cm (i16) · 2:1 temp delta deci-°C (i8) ·
///   3:1 tank pressure delta deci-bar (i8). Deltas accumulate onto the running value; the base counts as
///   sample 0, the first delta produces sample 1 at t = interval, and so on.
/// End (0x03, N=0): closes the dive.
///
/// Strict like <see cref="ShearwaterProtocol"/>: bad magic/length/checksum, unknown type, batch/end with no
/// open dive, a second header before end, a negative reconstructed depth, or a stream that ends mid-dive all
/// throw <see cref="DiveComputerProtocolException"/>.
/// </summary>
public sealed class ScubaProProtocol : IDiveComputerProtocol
{
    private const byte Magic = 0x55;
    private const byte TypeHeader = 0x01;
    private const byte TypeSamples = 0x02;
    private const byte TypeEnd = 0x03;

    public DiveComputerVendor Vendor => DiveComputerVendor.ScubaPro;

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
                        throw new DiveComputerProtocolException("New ScubaPro dive header before the previous dive's end frame.");
                    }

                    current = ParseHeader(payload);
                    break;
                case TypeSamples:
                    (current ?? throw new DiveComputerProtocolException("ScubaPro sample batch before any dive header."))
                        .AddBatch(payload);
                    break;
                case TypeEnd:
                    if (payload.Length != 0)
                    {
                        throw new DiveComputerProtocolException($"ScubaPro end payload must be empty, got {payload.Length} bytes.");
                    }

                    completed.Add(
                        (current ?? throw new DiveComputerProtocolException("ScubaPro end frame before any dive header.")).Build());
                    current = null;
                    break;
                default:
                    throw new DiveComputerProtocolException($"Unknown ScubaPro frame type 0x{type:X2}.");
            }
        }

        if (current is not null)
        {
            throw new DiveComputerProtocolException("ScubaPro stream ended mid-dive (no end frame — truncated download).");
        }

        return completed;
    }

    private static (byte Type, ReadOnlyMemory<byte> Payload) ValidateAndUnwrap(DiveComputerFrame frame)
    {
        var bytes = frame.Payload;
        var span = bytes.Span;
        if (span.Length < 4)
        {
            throw new DiveComputerProtocolException($"Truncated ScubaPro frame: {span.Length} bytes is below the 4-byte envelope minimum.");
        }

        if (span[0] != Magic)
        {
            throw new DiveComputerProtocolException("Bad ScubaPro frame magic (expected 'U').");
        }

        int declared = span[2];
        if (span.Length != 4 + declared)
        {
            throw new DiveComputerProtocolException(
                $"Truncated ScubaPro frame: envelope declares {declared} payload bytes but {span.Length - 4} arrived.");
        }

        var expected = ProtocolPrimitives.AdditiveSum(span[..^1]);
        if (span[^1] != expected)
        {
            throw new DiveComputerProtocolException(
                $"ScubaPro checksum mismatch (expected 0x{expected:X2}, got 0x{span[^1]:X2}) — corrupt frame.");
        }

        return (span[1], bytes.Slice(3, declared));
    }

    private static DiveBuilder ParseHeader(ReadOnlyMemory<byte> payloadMem)
    {
        var payload = payloadMem.Span;
        if (payload.Length != 15)
        {
            throw new DiveComputerProtocolException($"ScubaPro header payload must be 15 bytes, got {payload.Length}.");
        }

        var interval = payload[6];
        if (interval == 0)
        {
            throw new DiveComputerProtocolException("ScubaPro header declares a zero sample interval (degenerate/corrupt frame).");
        }

        var tankRaw = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(13, 2));
        return new DiveBuilder(
            StartUtc: DateTimeOffset.FromUnixTimeSeconds(BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4))).UtcDateTime,
            DurationSec: BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(4, 2)),
            IntervalSec: interval,
            O2: payload[7],
            He: payload[8],
            BaseDepthM: BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(9, 2)) / 100.0,
            BaseTempC: BinaryPrimitives.ReadInt16LittleEndian(payload.Slice(11, 2)) / 100.0,
            BaseTankBar: tankRaw == ProtocolPrimitives.U16NotAvailable ? null : tankRaw / 10.0);
    }

    private sealed class DiveBuilder
    {
        private readonly DateTime _startUtc;
        private readonly int _durationSec;
        private readonly int _intervalSec;
        private readonly int _o2;
        private readonly int _he;
        private readonly List<ProfileSampleDto> _samples;
        private double _depthM;
        private double _tempC;
        private double? _tankBar;

        public DiveBuilder(
            DateTime StartUtc,
            int DurationSec,
            int IntervalSec,
            int O2,
            int He,
            double BaseDepthM,
            double BaseTempC,
            double? BaseTankBar)
        {
            _startUtc = StartUtc;
            _durationSec = DurationSec;
            _intervalSec = IntervalSec;
            _o2 = O2;
            _he = He;
            _depthM = BaseDepthM;
            _tempC = BaseTempC;
            _tankBar = BaseTankBar;
            _samples = [Sample(0)];
        }

        public void AddBatch(ReadOnlyMemory<byte> payloadMem)
        {
            var payload = payloadMem.Span;
            if (payload.Length % 4 != 0)
            {
                throw new DiveComputerProtocolException(
                    $"ScubaPro sample batch must be a whole number of 4-byte deltas, got {payload.Length} bytes.");
            }

            var count = payload.Length / 4;
            for (var i = 0; i < count; i++)
            {
                var depthDeltaCm = BinaryPrimitives.ReadInt16LittleEndian(payload.Slice(i * 4, 2));
                var tempDeltaDeci = (sbyte)payload[i * 4 + 2];
                var tankDeltaDeci = (sbyte)payload[i * 4 + 3];

                _depthM = Math.Round(_depthM + depthDeltaCm / 100.0, 2);
                if (_depthM < 0)
                {
                    throw new DiveComputerProtocolException("ScubaPro depth delta produced a negative depth (corrupt delta stream).");
                }

                _tempC = Math.Round(_tempC + tempDeltaDeci / 10.0, 2);
                if (_tankBar is { } tank)
                {
                    _tankBar = Math.Round(tank + tankDeltaDeci / 10.0, 2);
                }

                _samples.Add(Sample(_samples.Count));
            }
        }

        private ProfileSampleDto Sample(int ordinal) => new(
            ordinal * _intervalSec,
            _depthM,
            _tempC,
            PpO2: null,
            Ndl: null,
            CeilingM: null,
            AscentRateMPerMin: null,
            Cns: null,
            TankPressureBar: _tankBar,
            Setpoint: null,
            HeartRate: null);

        public ImportedDive Build()
        {
            var tankSamples = _samples.Where(s => s.TankPressureBar is not null).ToList();
            var cylinder = tankSamples.Count > 0
                ? new CylinderDto(_o2, _he > 0 ? _he : null, tankSamples[0].TankPressureBar, tankSamples[^1].TankPressureBar)
                : new CylinderDto(_o2, _he > 0 ? _he : null);
            IReadOnlyList<CylinderDto> cylinders = [cylinder];

            return new ImportedDive(
                _startUtc,
                _durationSec,
                MaxDepthM: _samples.Max(s => s.DepthM),
                AvgDepthM: Math.Round(_samples.Average(s => s.DepthM), 2),
                MinTempC: _samples.Min(s => s.TempC),
                GasMix: ProtocolPrimitives.FormatGasMix(_o2, _he),
                Samples: _samples,
                Cylinders: cylinders);
        }
    }
}
