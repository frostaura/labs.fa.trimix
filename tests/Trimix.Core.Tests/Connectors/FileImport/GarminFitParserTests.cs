using System.Buffers.Binary;
using Trimix.Core.Connectors.FileImport;
using Xunit;

namespace Trimix.Core.Tests.Connectors.FileImport;

/// <summary>
/// GarminFitParser against the committed <c>sample.fit</c> (one dive: 5 record samples, one EAN32 gas, a
/// dive_summary) plus targeted decoder-robustness cases (compressed-timestamp records, corrupt CRC, bad
/// signature). The fixture is spec-correct FIT, so the same parser decodes real Garmin Descent files.
/// </summary>
public class GarminFitParserTests
{
    private readonly GarminFitParser _parser = new();

    [Fact]
    public void Sniffs_fit_by_signature_and_extension()
    {
        Assert.True(_parser.CanParse(FileImportFixtures.Fit));
        Assert.True(_parser.CanParse(new PickedFile("dive.FIT", [0, 0, 0, 0, 0, 0, 0, 0, 0x2E, 0x46, 0x49, 0x54])));
        Assert.False(_parser.CanParse(new PickedFile("dive.uddf", "<uddf/>"u8.ToArray())));
    }

    [Fact]
    public void Parses_summary_metadata_and_gas()
    {
        var dive = Assert.Single(_parser.Parse(FileImportFixtures.Fit));

        Assert.Equal(new DateTime(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc), dive.StartUtc);
        Assert.Equal(40, dive.DurationSec);          // dive_summary.bottom_time (40000 ms)
        Assert.Equal(28.4, dive.MaxDepthM, 3);       // dive_summary.max_depth (28400 mm)
        Assert.Equal(11.18, dive.AvgDepthM!.Value, 3); // dive_summary.avg_depth (11180 mm)
        Assert.Equal(18.0, dive.MinTempC!.Value, 3); // coldest sample
        Assert.Equal("EAN32", dive.GasMix);

        var cylinder = Assert.Single(dive.Cylinders!);
        Assert.Equal(32, cylinder.O2);
        Assert.Equal(0, cylinder.He);
    }

    [Fact]
    public void Reconstructs_the_rich_sample_channels()
    {
        var dive = Assert.Single(_parser.Parse(FileImportFixtures.Fit));

        Assert.Equal(5, dive.Samples.Count);
        Assert.Equal([0, 10, 20, 30, 40], dive.Samples.Select(s => s.TSeconds));

        // Deepest sample carries depth/temp/NDL/ceiling/CNS.
        var deepest = dive.Samples[2];
        Assert.Equal(28.4, deepest.DepthM, 3);
        Assert.Equal(18.0, deepest.TempC!.Value, 3);
        Assert.Equal(0.0, deepest.Ndl!.Value, 3);       // ndl_time 0 s → 0 min
        Assert.Equal(3.0, deepest.CeilingM!.Value, 3);  // next_stop_depth 3000 mm
        Assert.Equal(8, deepest.Cns!.Value, 3);

        // ndl_time is converted seconds → minutes.
        Assert.Equal(9.0, dive.Samples[1].Ndl!.Value, 3); // 540 s → 9 min

        // First sample's invalid (0xFFFFFFFF / 0xFF) fields decode as absent, not sentinel numbers.
        Assert.Null(dive.Samples[0].Ndl);
        Assert.Null(dive.Samples[0].CeilingM);
        Assert.Null(dive.Samples[0].Cns);
        Assert.Null(dive.Samples[0].PpO2);
    }

    [Fact]
    public void Corrupt_crc_throws()
    {
        var bytes = (byte[])FileImportFixtures.Fit.Contents.Clone();
        bytes[^1] ^= 0xFF; // flip the trailing CRC

        var ex = Assert.Throws<DiveFileParseException>(() => _parser.Parse(new PickedFile("bad.fit", bytes)));
        Assert.Contains("CRC", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Missing_signature_throws()
    {
        var bytes = (byte[])FileImportFixtures.Fit.Contents.Clone();
        bytes[8] = 0x00; // corrupt the ".FIT" signature

        Assert.Throws<DiveFileParseException>(() => _parser.Parse(new PickedFile("bad.fit", bytes)));
    }

    [Fact]
    public void Field_size_smaller_than_its_base_type_width_throws_parse_exception()
    {
        // A crafted (valid-CRC) FIT declaring a uint16 field (base type 4, 2-byte) with size 1 must raise the
        // parser's DiveFileParseException — NOT an unhandled ArgumentOutOfRangeException from a width-blind read.
        var records = new List<byte>();
        // Definition local 0 (global 20): one field num=91, size=1, baseType 0x84 (uint16).
        records.AddRange([0x40, 0x00, 0x00]);
        records.AddRange(BitConverter.GetBytes((ushort)20));
        records.Add(1);
        records.AddRange([91, 1, 0x84]);
        // Data record local 0: a single byte — one short of the 2-byte uint16 the definition claims.
        records.AddRange([0x00, 0x2A]);

        var file = BuildFit(records.ToArray());

        var ex = Assert.Throws<DiveFileParseException>(() => _parser.Parse(new PickedFile("malformed.fit", file)));
        Assert.Contains("size", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Decodes_compressed_timestamp_records()
    {
        // A minimal FIT stream that exercises the compressed-timestamp header path. Per the FIT spec, a
        // compressed-timestamp record's definition omits field 253 (the timestamp rides in the header),
        // and the reference is established by a separate full-timestamp message.
        var records = new List<byte>();

        // Definition local 1 (global 20): timestamp(253,u32) + depth(91,u32) — the full-timestamp record.
        records.AddRange([0x41, 0x00, 0x00]);
        records.AddRange(BitConverter.GetBytes((ushort)20));
        records.Add(2);
        records.AddRange([253, 4, 0x86, 91, 4, 0x86]);

        // Full-timestamp record establishes the reference at 1024 (low 5 bits = 0), depth 5000 mm.
        records.Add(0x01);
        records.AddRange(BitConverter.GetBytes((uint)1024));
        records.AddRange(BitConverter.GetBytes((uint)5000));

        // Definition local 0 (global 20): depth(91,u32) only — used by compressed-timestamp records.
        records.AddRange([0x40, 0x00, 0x00]);
        records.AddRange(BitConverter.GetBytes((ushort)20));
        records.Add(1);
        records.AddRange([91, 4, 0x86]);

        // Compressed-timestamp record: header 0x8A (bit7 set, local 0, 5-bit offset 10) → ts 1034; depth 6000 mm.
        records.Add(0x8A);
        records.AddRange(BitConverter.GetBytes((uint)6000));

        var file = BuildFit(records.ToArray());
        var dive = Assert.Single(_parser.Parse(new PickedFile("compressed.fit", file)));

        Assert.Equal(2, dive.Samples.Count);
        Assert.Equal(0, dive.Samples[0].TSeconds);
        Assert.Equal(10, dive.Samples[1].TSeconds); // 1034 − 1024
        Assert.Equal(6.0, dive.Samples[1].DepthM, 3);
    }

    /// <summary>Wraps a record-region byte block in a valid 12-byte FIT header + trailing CRC.</summary>
    private static byte[] BuildFit(byte[] records)
    {
        var header = new byte[12];
        header[0] = 12;
        header[1] = 0x10;
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(2, 2), 2140);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4, 4), (uint)records.Length);
        header[8] = 0x2E;
        header[9] = 0x46;
        header[10] = 0x49;
        header[11] = 0x54;

        var body = new byte[header.Length + records.Length];
        header.CopyTo(body, 0);
        records.CopyTo(body, header.Length);

        var crc = Crc16(body);
        return [.. body, (byte)(crc & 0xFF), (byte)(crc >> 8)];
    }

    private static readonly ushort[] CrcTable =
    [
        0x0000, 0xCC01, 0xD801, 0x1400, 0xF001, 0x3C00, 0x2800, 0xE401,
        0xA001, 0x6C00, 0x7800, 0xB401, 0x5000, 0x9C01, 0x8801, 0x4400,
    ];

    private static ushort Crc16(ReadOnlySpan<byte> data)
    {
        ushort crc = 0;
        foreach (byte b in data)
        {
            ushort tmp = CrcTable[crc & 0xF];
            crc = (ushort)((crc >> 4) & 0x0FFF);
            crc = (ushort)(crc ^ tmp ^ CrcTable[b & 0xF]);
            tmp = CrcTable[crc & 0xF];
            crc = (ushort)((crc >> 4) & 0x0FFF);
            crc = (ushort)(crc ^ tmp ^ CrcTable[(b >> 4) & 0xF]);
        }

        return crc;
    }
}
