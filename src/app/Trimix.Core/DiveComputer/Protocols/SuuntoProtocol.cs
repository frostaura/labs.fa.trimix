using System.Buffers.Binary;
using Trimix.Contracts.Dtos;
using Trimix.Core.Abstractions;

namespace Trimix.Core.DiveComputer.Protocols;

/// <summary>
/// Suunto EON Steel / EON Core / D5 / Ocean family parser — clean-room C#, no libdivecomputer code and
/// no static/LGPL link. The real EON BLE protocol is a length-prefixed packet transport carrying a
/// serialized object tree whose dive logs are a stream of typed sample records; this parser decodes a
/// faithful, self-consistent TLV rendering of that shape (record magic + type + length + XOR checksum),
/// accumulating samples until a footer closes the dive. It maps Suunto's characteristically rich channel
/// set: per-sample depth, temperature, NDL, deco ceiling, CNS, tank pressure (Tank POD), the active gas
/// (from a gas-definition table), and CCR setpoint.
///
/// ── FRAME CONTRACT (best-effort; needs M3 real-hardware capture validation — see ProtocolPrimitives) ──
///
/// Every <see cref="DiveComputerFrame"/> is ONE record (little-endian):
///   0     1    magic 0x53 ('S')
///   1     1    type: 0x10 dive header · 0x20 sample · 0x30 gas definition · 0x40 dive footer
///   2     2    payload length N (u16)
///   4     N    payload
///   4+N   1    XOR checksum of bytes [0 .. 4+N)
///
/// Header (0x10, N=14): 0:8 start time unix MILLISECONDS UTC (i64) · 8:4 duration sec (u32) ·
///   12:2 nominal sample interval sec (u16, metadata only — sample time is authoritative).
/// Gas definition (0x30, N=3): 0:1 gas index · 1:1 O2 % · 2:1 He %. Zero or more, before the samples.
/// Sample (0x20, N=18): 0:4 time offset from start, sec (u32) · 4:2 depth cm (u16) ·
///   6:2 temp centi-°C (i16) · 8:2 NDL minutes (u16; 0xFFFF = in-deco/n-a) · 10:2 ceiling cm (u16; 0 = none) ·
///   12:2 tank pressure deci-bar (u16; 0xFFFF = no POD) · 14:1 CNS % (u8; 0xFF = n/a) ·
///   15:1 active gas index (u8) · 16:2 CCR setpoint mbar ppO2 (u16; 0 = open-circuit).
/// Footer (0x40, N=0): closes the current dive.
///
/// The parser is strict (mirrors <see cref="ShearwaterProtocol"/>): bad magic, wrong length, checksum
/// mismatch, unknown type, sample/footer with no open dive, a second header before a footer, an unknown
/// gas index, zero samples, or a stream that ends mid-dive all throw <see cref="DiveComputerProtocolException"/>.
/// </summary>
public sealed class SuuntoProtocol : IDiveComputerProtocol
{
    private const byte Magic = 0x53;
    private const byte TypeHeader = 0x10;
    private const byte TypeSample = 0x20;
    private const byte TypeGas = 0x30;
    private const byte TypeFooter = 0x40;

    public DiveComputerVendor Vendor => DiveComputerVendor.Suunto;

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
                        throw new DiveComputerProtocolException("New Suunto dive header before the previous dive's footer.");
                    }

                    current = ParseHeader(payload);
                    break;
                case TypeGas:
                    Require(current, "gas definition").AddGas(payload);
                    break;
                case TypeSample:
                    Require(current, "sample").AddSample(payload);
                    break;
                case TypeFooter:
                    if (payload.Length != 0)
                    {
                        throw new DiveComputerProtocolException($"Suunto footer payload must be empty, got {payload.Length} bytes.");
                    }

                    completed.Add(Require(current, "footer").Build());
                    current = null;
                    break;
                default:
                    throw new DiveComputerProtocolException($"Unknown Suunto record type 0x{type:X2}.");
            }
        }

        if (current is not null)
        {
            throw new DiveComputerProtocolException("Suunto stream ended mid-dive (no footer — truncated download).");
        }

        return completed;
    }

    private static DiveBuilder Require(DiveBuilder? builder, string what) =>
        builder ?? throw new DiveComputerProtocolException($"Suunto {what} record before any dive header.");

    private static (byte Type, ReadOnlyMemory<byte> Payload) ValidateAndUnwrap(DiveComputerFrame frame)
    {
        var bytes = frame.Payload;
        var span = bytes.Span;
        if (span.Length < 5)
        {
            throw new DiveComputerProtocolException($"Truncated Suunto record: {span.Length} bytes is below the 5-byte envelope minimum.");
        }

        if (span[0] != Magic)
        {
            throw new DiveComputerProtocolException("Bad Suunto record magic (expected 'S').");
        }

        int declared = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(2, 2));
        if (span.Length != 5 + declared)
        {
            throw new DiveComputerProtocolException(
                $"Truncated Suunto record: envelope declares {declared} payload bytes but {span.Length - 5} arrived.");
        }

        var expected = ProtocolPrimitives.Xor(span[..^1]);
        if (span[^1] != expected)
        {
            throw new DiveComputerProtocolException(
                $"Suunto checksum mismatch (expected 0x{expected:X2}, got 0x{span[^1]:X2}) — corrupt record.");
        }

        return (span[1], bytes.Slice(4, declared));
    }

    private static DiveBuilder ParseHeader(ReadOnlyMemory<byte> payloadMem)
    {
        var payload = payloadMem.Span;
        if (payload.Length != 14)
        {
            throw new DiveComputerProtocolException($"Suunto header payload must be 14 bytes, got {payload.Length}.");
        }

        var startMs = BinaryPrimitives.ReadInt64LittleEndian(payload.Slice(0, 8));
        var duration = (int)BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(8, 4));
        var interval = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(12, 2));
        if (interval == 0)
        {
            throw new DiveComputerProtocolException("Suunto header declares a zero sample interval (degenerate/corrupt record).");
        }

        return new DiveBuilder(DateTimeOffset.FromUnixTimeMilliseconds(startMs).UtcDateTime, duration);
    }

    private sealed class DiveBuilder(DateTime startUtc, int durationSec)
    {
        private readonly List<ProfileSampleDto> _samples = [];
        private readonly Dictionary<int, (int O2, int He)> _gases = [];
        private int _lastTime = -1;

        public void AddGas(ReadOnlyMemory<byte> payloadMem)
        {
            var payload = payloadMem.Span;
            if (payload.Length != 3)
            {
                throw new DiveComputerProtocolException($"Suunto gas definition must be 3 bytes, got {payload.Length}.");
            }

            _gases[payload[0]] = (payload[1], payload[2]);
        }

        public void AddSample(ReadOnlyMemory<byte> payloadMem)
        {
            var payload = payloadMem.Span;
            if (payload.Length != 18)
            {
                throw new DiveComputerProtocolException($"Suunto sample must be 18 bytes, got {payload.Length}.");
            }

            var t = (int)BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(0, 4));
            if (t <= _lastTime)
            {
                throw new DiveComputerProtocolException(
                    $"Suunto samples must strictly increase in time (got {t}s after {_lastTime}s).");
            }

            _lastTime = t;

            var depthM = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(4, 2)) / 100.0;
            var tempC = BinaryPrimitives.ReadInt16LittleEndian(payload.Slice(6, 2)) / 100.0;

            var ndlRaw = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(8, 2));
            double? ndl = ndlRaw == ProtocolPrimitives.U16NotAvailable ? null : ndlRaw;

            var ceilingCm = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(10, 2));
            double? ceilingM = ceilingCm == 0 ? null : ceilingCm / 100.0;

            var tankRaw = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(12, 2));
            double? tankBar = tankRaw == ProtocolPrimitives.U16NotAvailable ? null : tankRaw / 10.0;

            var cnsRaw = payload[14];
            double? cns = cnsRaw == ProtocolPrimitives.U8NotAvailable ? null : cnsRaw;

            var gasIndex = payload[15];
            if (!_gases.ContainsKey(gasIndex))
            {
                throw new DiveComputerProtocolException($"Suunto sample references unknown gas index {gasIndex}.");
            }

            var setpointRaw = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(16, 2));
            double? setpoint = setpointRaw == 0 ? null : setpointRaw / 1000.0;

            _samples.Add(new ProfileSampleDto(
                t,
                depthM,
                tempC,
                PpO2: null,
                Ndl: ndl,
                CeilingM: ceilingM,
                AscentRateMPerMin: null,
                Cns: cns,
                TankPressureBar: tankBar,
                Setpoint: setpoint,
                HeartRate: null));
        }

        public ImportedDive Build()
        {
            if (_samples.Count == 0)
            {
                throw new DiveComputerProtocolException("Suunto dive closed with zero samples (empty/corrupt download).");
            }

            var primaryGas = _gases.Count > 0 ? _gases[_gases.Keys.Min()] : (O2: 21, He: 0);
            var tankSamples = _samples.Where(s => s.TankPressureBar is not null).ToList();

            var cylinders = _gases
                .OrderBy(g => g.Key)
                .Select(g => new CylinderDto(
                    O2: g.Value.O2,
                    He: g.Value.He > 0 ? g.Value.He : null,
                    // Attach the tank-pressure trace's start/end to the primary gas (index-min); Suunto's
                    // single Tank POD channel is not per-cylinder, so richer attribution waits on M3 data.
                    StartBar: g.Key == _gases.Keys.Min() && tankSamples.Count > 0 ? tankSamples[0].TankPressureBar : null,
                    EndBar: g.Key == _gases.Keys.Min() && tankSamples.Count > 0 ? tankSamples[^1].TankPressureBar : null))
                .ToList();

            return new ImportedDive(
                startUtc,
                durationSec,
                MaxDepthM: _samples.Max(s => s.DepthM),
                AvgDepthM: Math.Round(_samples.Average(s => s.DepthM), 2),
                MinTempC: _samples.Min(s => s.TempC),
                GasMix: ProtocolPrimitives.FormatGasMix(primaryGas.O2, primaryGas.He),
                Samples: _samples,
                Cylinders: cylinders.Count > 0 ? cylinders : null);
        }
    }
}
