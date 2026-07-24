using System.Buffers.Binary;

namespace Trimix.Core.Connectors.FileImport;

/// <summary>
/// Minimal, dependency-free FIT decoder for dive messages (architecture overview §2, file-import). Decodes
/// the FIT header, validates the trailing CRC-16, then walks the record region: definition messages,
/// normal data messages, and compressed-timestamp data messages, in either byte order. Each data message is
/// surfaced as its global message number + a map of field-number → numeric value, with FIT "invalid"
/// sentinels dropped (so an absent temperature reads as missing, not <c>0x7F</c>). Dive-level interpretation
/// (depth/temp/gas/summary) lives in <see cref="GarminFitParser"/>. This is spec-correct against the public
/// FIT protocol, so it decodes real Garmin Descent <c>.FIT</c> files, not just the fixture.
/// </summary>
internal static class FitReader
{
    /// <summary>One decoded FIT data message. <paramref name="Timestamp"/> is the FIT-epoch seconds when the message carries/derives one.</summary>
    public sealed record FitMessage(int GlobalMessageNumber, IReadOnlyDictionary<int, double> Fields, long? Timestamp);

    private const int TimestampFieldNumber = 253;

    private sealed class Definition
    {
        public int GlobalMessageNumber { get; init; }

        public bool BigEndian { get; init; }

        public (int Num, int Size, int BaseType)[] Fields { get; init; } = [];

        /// <summary>Total bytes of developer fields appended to each data message (read past, not interpreted).</summary>
        public int DeveloperBytes { get; init; }
    }

    public static IReadOnlyList<FitMessage> Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 12)
        {
            throw new DiveFileParseException("FIT file is shorter than the 12-byte header.");
        }

        int headerSize = bytes[0];
        if (headerSize is not (12 or 14))
        {
            throw new DiveFileParseException($"Unexpected FIT header size {headerSize} (expected 12 or 14).");
        }

        if (bytes[8] != 0x2E || bytes[9] != 0x46 || bytes[10] != 0x49 || bytes[11] != 0x54)
        {
            throw new DiveFileParseException("Missing \".FIT\" signature — not a FIT file.");
        }

        uint dataSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(4, 4));
        long dataEndLong = (long)headerSize + dataSize;
        if (dataEndLong + 2 > bytes.Length)
        {
            throw new DiveFileParseException("FIT data size exceeds the file length (truncated download).");
        }

        int dataEnd = (int)dataEndLong;
        ushort fileCrc = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(dataEnd, 2));
        ushort computed = Crc16(bytes[..dataEnd]);
        if (fileCrc != computed)
        {
            throw new DiveFileParseException($"FIT CRC mismatch (file 0x{fileCrc:X4}, computed 0x{computed:X4}) — corrupt file.");
        }

        var definitions = new Definition?[16];
        var messages = new List<FitMessage>();
        long lastTimestamp = 0;
        int pos = headerSize;

        while (pos < dataEnd)
        {
            byte header = bytes[pos++];
            bool compressedTimestamp = (header & 0x80) != 0;
            int localType;
            long? messageTimestamp = null;

            if (compressedTimestamp)
            {
                localType = (header >> 5) & 0x03;
                int offset = header & 0x1F;
                long rolled = (lastTimestamp & ~0x1FL) + offset;
                if (offset < (lastTimestamp & 0x1F))
                {
                    rolled += 0x20; // 5-bit offset wrapped
                }

                lastTimestamp = rolled;
                messageTimestamp = rolled;
            }
            else
            {
                localType = header & 0x0F;
            }

            bool isDefinition = !compressedTimestamp && (header & 0x40) != 0;
            if (isDefinition)
            {
                definitions[localType] = ReadDefinition(bytes, dataEnd, hasDeveloperFields: (header & 0x20) != 0, ref pos);
                continue;
            }

            var definition = definitions[localType]
                ?? throw new DiveFileParseException($"FIT data message references undefined local type {localType}.");

            var fields = new Dictionary<int, double>();
            foreach (var (num, size, baseType) in definition.Fields)
            {
                if (pos + size > dataEnd)
                {
                    throw new DiveFileParseException("Truncated FIT data message.");
                }

                // A numeric field's declared size must be at least its base type's element width; a crafted
                // file declaring e.g. baseType uint16 with size 1 would otherwise make the width-blind
                // multi-byte reads below throw a raw ArgumentOutOfRangeException instead of the parser's
                // contractual DiveFileParseException.
                var scalarWidth = ScalarWidth(baseType);
                if (scalarWidth > 0 && size < scalarWidth)
                {
                    throw new DiveFileParseException(
                        $"Malformed FIT field: declared size {size} is smaller than base type {baseType}'s {scalarWidth}-byte width.");
                }

                var slice = bytes.Slice(pos, size);
                pos += size;
                if (TryDecodeScalar(slice, baseType, definition.BigEndian, out double value))
                {
                    fields[num] = value;
                    if (num == TimestampFieldNumber)
                    {
                        lastTimestamp = (long)value;
                        messageTimestamp = (long)value;
                    }
                }
            }

            pos += definition.DeveloperBytes; // developer data is present in the record but not interpreted
            messages.Add(new FitMessage(definition.GlobalMessageNumber, fields, messageTimestamp));
        }

        return messages;
    }

    private static Definition ReadDefinition(ReadOnlySpan<byte> bytes, int dataEnd, bool hasDeveloperFields, ref int pos)
    {
        if (pos + 5 > dataEnd)
        {
            throw new DiveFileParseException("Truncated FIT definition message.");
        }

        pos++; // reserved
        bool bigEndian = bytes[pos++] == 1;
        int globalMessageNumber = bigEndian
            ? BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(pos, 2))
            : BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(pos, 2));
        pos += 2;
        int fieldCount = bytes[pos++];
        if (pos + fieldCount * 3 > dataEnd)
        {
            throw new DiveFileParseException("Truncated FIT field definitions.");
        }

        var fields = new (int, int, int)[fieldCount];
        for (var i = 0; i < fieldCount; i++)
        {
            fields[i] = (bytes[pos], bytes[pos + 1], bytes[pos + 2] & 0x1F);
            pos += 3;
        }

        int developerBytes = 0;
        if (hasDeveloperFields)
        {
            if (pos >= dataEnd)
            {
                throw new DiveFileParseException("Truncated FIT developer-field header.");
            }

            int developerCount = bytes[pos++];
            if (pos + developerCount * 3 > dataEnd)
            {
                throw new DiveFileParseException("Truncated FIT developer-field definitions.");
            }

            for (var i = 0; i < developerCount; i++)
            {
                developerBytes += bytes[pos + 1]; // field size
                pos += 3;
            }
        }

        return new Definition
        {
            GlobalMessageNumber = globalMessageNumber,
            BigEndian = bigEndian,
            Fields = fields,
            DeveloperBytes = developerBytes,
        };
    }

    /// <summary>Element width (bytes) of a numeric FIT base type, or 0 for string / non-numeric types.</summary>
    private static int ScalarWidth(int baseType) => baseType switch
    {
        0 or 1 or 2 or 10 or 13 => 1, // enum, sint8, uint8, uint8z, byte
        3 or 4 or 11 => 2,            // sint16, uint16, uint16z
        5 or 6 or 8 or 12 => 4,       // sint32, uint32, float32, uint32z
        9 => 8,                       // float64
        _ => 0,                       // string (7) and anything else — no numeric width to enforce
    };

    /// <summary>Decodes the first element of a FIT field, returning false for the type's "invalid" sentinel.</summary>
    private static bool TryDecodeScalar(ReadOnlySpan<byte> slice, int baseType, bool bigEndian, out double value)
    {
        value = 0;
        switch (baseType)
        {
            case 0: // enum
            case 2: // uint8
            case 13: // byte
                if (slice[0] == 0xFF)
                {
                    return false;
                }

                value = slice[0];
                return true;
            case 10: // uint8z
                if (slice[0] == 0)
                {
                    return false;
                }

                value = slice[0];
                return true;
            case 1: // sint8
                var s8 = (sbyte)slice[0];
                if (s8 == 0x7F)
                {
                    return false;
                }

                value = s8;
                return true;
            case 3: // sint16
                short s16 = bigEndian ? BinaryPrimitives.ReadInt16BigEndian(slice[..2]) : BinaryPrimitives.ReadInt16LittleEndian(slice[..2]);
                if (s16 == 0x7FFF)
                {
                    return false;
                }

                value = s16;
                return true;
            case 4: // uint16
                ushort u16 = bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(slice[..2]) : BinaryPrimitives.ReadUInt16LittleEndian(slice[..2]);
                if (u16 == 0xFFFF)
                {
                    return false;
                }

                value = u16;
                return true;
            case 11: // uint16z
                ushort u16z = bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(slice[..2]) : BinaryPrimitives.ReadUInt16LittleEndian(slice[..2]);
                if (u16z == 0)
                {
                    return false;
                }

                value = u16z;
                return true;
            case 5: // sint32
                int s32 = bigEndian ? BinaryPrimitives.ReadInt32BigEndian(slice[..4]) : BinaryPrimitives.ReadInt32LittleEndian(slice[..4]);
                if (s32 == 0x7FFFFFFF)
                {
                    return false;
                }

                value = s32;
                return true;
            case 6: // uint32
                uint u32 = bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(slice[..4]) : BinaryPrimitives.ReadUInt32LittleEndian(slice[..4]);
                if (u32 == 0xFFFFFFFF)
                {
                    return false;
                }

                value = u32;
                return true;
            case 12: // uint32z
                uint u32z = bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(slice[..4]) : BinaryPrimitives.ReadUInt32LittleEndian(slice[..4]);
                if (u32z == 0)
                {
                    return false;
                }

                value = u32z;
                return true;
            case 8: // float32
                float f32 = bigEndian ? BinaryPrimitives.ReadSingleBigEndian(slice[..4]) : BinaryPrimitives.ReadSingleLittleEndian(slice[..4]);
                if (float.IsNaN(f32))
                {
                    return false;
                }

                value = f32;
                return true;
            case 9: // float64
                double f64 = bigEndian ? BinaryPrimitives.ReadDoubleBigEndian(slice[..8]) : BinaryPrimitives.ReadDoubleLittleEndian(slice[..8]);
                if (double.IsNaN(f64))
                {
                    return false;
                }

                value = f64;
                return true;
            default: // string (7) and anything else — not needed for dive numerics
                return false;
        }
    }

    private static readonly ushort[] CrcTable =
    [
        0x0000, 0xCC01, 0xD801, 0x1400, 0xF001, 0x3C00, 0x2800, 0xE401,
        0xA001, 0x6C00, 0x7800, 0xB401, 0x5000, 0x9C01, 0x8801, 0x4400,
    ];

    /// <summary>The FIT protocol's CRC-16 (nibble-table variant) over the header+record region.</summary>
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
