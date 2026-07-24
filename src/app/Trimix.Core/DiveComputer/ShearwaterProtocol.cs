using System.Buffers.Binary;
using Trimix.Contracts.Dtos;
using Trimix.Core.Abstractions;

namespace Trimix.Core.DiveComputer;

/// <summary>
/// Shearwater (Perdix 2) frame parser — pure C#, unit-tested against the embedded fixture frames.
/// The frame format is documented in <see cref="ShearwaterFixtures"/>; this parser is strict:
/// truncated frames, bad magic, checksum mismatches, unknown frame types, unknown dive indexes,
/// and sample overflows all throw <see cref="DiveComputerProtocolException"/>.
///
/// ⚠ M3 RISK: parsing is validated against the simulator fixture contract only. Real-hardware
/// field validation (actual Shearwater BLE protocol, post-dive salt-water conditions) is an open
/// M3 gate — see docs/prd.md "Carried-forward risks".
/// </summary>
public sealed class ShearwaterProtocol : IDiveComputerProtocol
{
    public DiveComputerVendor Vendor => DiveComputerVendor.Shearwater;

    public async Task<IReadOnlyList<ImportedDive>> ParseAsync(
        IAsyncEnumerable<DiveComputerFrame> frames,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frames);

        var builders = new Dictionary<int, DiveBuilder>();
        var completed = new List<ImportedDive>();

        await foreach (var frame in frames.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            var payload = ValidateAndUnwrap(frame);
            var frameType = frame.Payload.Span[2];
            switch (frameType)
            {
                case ShearwaterFixtures.FrameTypeHeader:
                    ParseHeader(payload, builders);
                    break;
                case ShearwaterFixtures.FrameTypeSamples:
                    ParseSamples(payload, builders);
                    break;
                case ShearwaterFixtures.FrameTypeEnd:
                    completed.Add(FinishDive(payload, builders));
                    break;
                default:
                    throw new DiveComputerProtocolException($"Unknown frame type 0x{frameType:X2}.");
            }
        }

        if (builders.Count > 0)
        {
            throw new DiveComputerProtocolException(
                $"Stream ended with {builders.Count} dive(s) missing their end frame (truncated download).");
        }

        return completed;
    }

    /// <summary>Checks magic, declared length, and XOR checksum; returns the payload bytes.</summary>
    private static ReadOnlySpan<byte> ValidateAndUnwrap(DiveComputerFrame frame)
    {
        var bytes = frame.Payload.Span;
        if (bytes.Length < 5)
        {
            throw new DiveComputerProtocolException($"Truncated frame: {bytes.Length} bytes is below the 5-byte envelope minimum.");
        }

        if (bytes[0] != ShearwaterFixtures.Magic0 || bytes[1] != ShearwaterFixtures.Magic1)
        {
            throw new DiveComputerProtocolException("Bad frame magic (expected 'SW').");
        }

        int declared = bytes[3];
        if (bytes.Length != 5 + declared)
        {
            throw new DiveComputerProtocolException(
                $"Truncated frame: envelope declares {declared} payload bytes but {bytes.Length - 5} arrived.");
        }

        var expected = ShearwaterFixtures.Checksum(bytes[..^1]);
        if (bytes[^1] != expected)
        {
            throw new DiveComputerProtocolException(
                $"Checksum mismatch (expected 0x{expected:X2}, got 0x{bytes[^1]:X2}) — corrupt frame.");
        }

        return bytes.Slice(4, declared);
    }

    private static void ParseHeader(ReadOnlySpan<byte> payload, Dictionary<int, DiveBuilder> builders)
    {
        if (payload.Length != 14)
        {
            throw new DiveComputerProtocolException($"Dive header payload must be 14 bytes, got {payload.Length}.");
        }

        int diveIndex = payload[0];
        if (builders.ContainsKey(diveIndex))
        {
            throw new DiveComputerProtocolException($"Duplicate header for dive {diveIndex}.");
        }

        int sampleIntervalSec = payload[9];
        int sampleCount = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(10, 2));

        // Reject degenerate-but-checksum-valid headers inside the parser's exception contract (the
        // 1-byte XOR only catches single-byte corruption). Zero samples would make Build()'s
        // samples.Max() throw InvalidOperationException outside the contract; a zero interval would
        // stamp every sample at TSeconds=0 and violate the server's (DiveId, TSeconds) composite PK.
        if (sampleCount == 0)
        {
            throw new DiveComputerProtocolException($"Dive {diveIndex} header declares zero samples (degenerate/corrupt frame).");
        }

        if (sampleIntervalSec == 0)
        {
            throw new DiveComputerProtocolException($"Dive {diveIndex} header declares a zero sample interval (degenerate/corrupt frame).");
        }

        builders[diveIndex] = new DiveBuilder(
            StartUtc: DateTimeOffset.FromUnixTimeSeconds(BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(1, 4))).UtcDateTime,
            DurationSec: (int)BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(5, 4)),
            SampleIntervalSec: sampleIntervalSec,
            SampleCount: sampleCount,
            O2Percent: payload[12],
            HePercent: payload[13]);
    }

    private static void ParseSamples(ReadOnlySpan<byte> payload, Dictionary<int, DiveBuilder> builders)
    {
        if (payload.Length < 4)
        {
            throw new DiveComputerProtocolException("Sample block payload below the 4-byte block header.");
        }

        int diveIndex = payload[0];
        if (!builders.TryGetValue(diveIndex, out var builder))
        {
            throw new DiveComputerProtocolException($"Sample block for unknown dive {diveIndex} (no header seen).");
        }

        int firstSample = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(1, 2));
        int count = payload[3];
        if (payload.Length != 4 + count * 4)
        {
            throw new DiveComputerProtocolException(
                $"Sample block declares {count} samples but carries {(payload.Length - 4) / 4}.");
        }

        if (firstSample + count > builder.SampleCount)
        {
            throw new DiveComputerProtocolException(
                $"Sample block overflows dive {diveIndex}: samples [{firstSample}..{firstSample + count}) exceed the declared {builder.SampleCount}.");
        }

        for (var i = 0; i < count; i++)
        {
            var depthCm = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(4 + i * 4, 2));
            var tempDeci = BinaryPrimitives.ReadInt16LittleEndian(payload.Slice(6 + i * 4, 2));
            builder.SetSample(firstSample + i, depthCm / 100.0, tempDeci / 10.0);
        }
    }

    private static ImportedDive FinishDive(ReadOnlySpan<byte> payload, Dictionary<int, DiveBuilder> builders)
    {
        if (payload.Length != 1)
        {
            throw new DiveComputerProtocolException($"Dive end payload must be 1 byte, got {payload.Length}.");
        }

        int diveIndex = payload[0];
        if (!builders.Remove(diveIndex, out var builder))
        {
            throw new DiveComputerProtocolException($"End frame for unknown dive {diveIndex} (no header seen).");
        }

        return builder.Build(diveIndex);
    }

    private sealed record DiveBuilder(
        DateTime StartUtc,
        int DurationSec,
        int SampleIntervalSec,
        int SampleCount,
        byte O2Percent,
        byte HePercent)
    {
        private readonly (double DepthM, double TempC)?[] _samples = new (double, double)?[SampleCount];

        public void SetSample(int index, double depthM, double tempC) => _samples[index] = (depthM, tempC);

        public ImportedDive Build(int diveIndex)
        {
            // Defense in depth: ParseHeader already rejects SampleCount==0, but never let an empty
            // sample array reach samples.Max()/Average() (which throw outside the parser's contract).
            if (SampleCount == 0)
            {
                throw new DiveComputerProtocolException($"Dive {diveIndex} has no samples (degenerate header).");
            }

            var received = _samples.Count(s => s is not null);
            if (received != SampleCount)
            {
                throw new DiveComputerProtocolException(
                    $"Dive {diveIndex} ended with {received}/{SampleCount} samples (incomplete download).");
            }

            var samples = new ProfileSampleDto[SampleCount];
            for (var i = 0; i < SampleCount; i++)
            {
                var (depthM, tempC) = _samples[i]!.Value;
                samples[i] = new ProfileSampleDto(i * SampleIntervalSec, depthM, tempC, PpO2: null);
            }

            return new ImportedDive(
                StartUtc,
                DurationSec,
                MaxDepthM: samples.Max(s => s.DepthM),
                AvgDepthM: Math.Round(samples.Average(s => s.DepthM), 2),
                MinTempC: samples.Min(s => s.TempC),
                GasMix: FormatGasMix(O2Percent, HePercent),
                Samples: samples);
        }

        /// <summary>Diver-native gas naming: trimix as "O2/He", nitrox as "EANxx", 21/0 as "Air".</summary>
        private static string FormatGasMix(byte o2, byte he) => (o2, he) switch
        {
            (_, > 0) => $"{o2}/{he}",
            (21, _) => "Air",
            _ => $"EAN{o2}",
        };
    }
}
