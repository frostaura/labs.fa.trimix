namespace Trimix.Core.DiveComputer.Protocols;

/// <summary>
/// Clean-room helpers shared by the retained-BLE vendor parsers (Suunto / ScubaPro / Aqualung / Mares).
/// Nothing here touches a platform BLE API — these are pure byte/number transforms, so the parsers stay
/// in <c>Trimix.Core</c> (net10.0) and remain unit-testable against committed fixture byte arrays.
///
/// ⚠ M3 RISK (stated honestly, matching <see cref="ShearwaterFixtures"/> and the aggregator architecture
/// doc §4): each vendor parser decodes a DOCUMENTED best-effort frame contract, NOT a byte-for-byte
/// re-implementation of the vendor's proprietary BLE wire format. The public research brief confirms no
/// vendor publishes a profile format, so the exact on-wire layout (endianness quirks, delta bases,
/// sentinel values, gas-table ordering) MUST be validated against a real-hardware capture at the M3 gate.
/// The contracts below are internally consistent, exercise the full rich-channel mapping end-to-end, and
/// are shaped after each vendor's public feature set (Suunto TLV + setpoint/CNS, Uwatec delta stream,
/// Oceanic/Pelagic gas-index records, Mares strided NDL/tank), so swapping in the validated layout is a
/// localized decoder change, not a pipeline change.
/// </summary>
internal static class ProtocolPrimitives
{
    /// <summary>Sentinel meaning "channel not present in this sample" for u16 fields (0xFFFF).</summary>
    public const ushort U16NotAvailable = 0xFFFF;

    /// <summary>Sentinel meaning "channel not present in this sample" for u8 fields (0xFF).</summary>
    public const byte U8NotAvailable = 0xFF;

    /// <summary>
    /// Diver-native gas naming, identical to the Shearwater parser's rule so mixed-source logbooks read
    /// consistently: trimix as "O2/He", nitrox as "EANxx", 21/0 as "Air".
    /// </summary>
    public static string FormatGasMix(int o2Percent, int hePercent) => (o2Percent, hePercent) switch
    {
        (_, > 0) => $"{o2Percent}/{hePercent}",
        (21, _) => "Air",
        _ => $"EAN{o2Percent}",
    };

    /// <summary>XOR of every byte — the envelope integrity check for the Suunto/Aqualung/Mares contracts.</summary>
    public static byte Xor(ReadOnlySpan<byte> bytes)
    {
        byte checksum = 0;
        foreach (var b in bytes)
        {
            checksum ^= b;
        }

        return checksum;
    }

    /// <summary>Additive (mod 256) checksum — the envelope integrity check for the Uwatec "Smart" contract.</summary>
    public static byte AdditiveSum(ReadOnlySpan<byte> bytes)
    {
        byte checksum = 0;
        foreach (var b in bytes)
        {
            checksum = unchecked((byte)(checksum + b));
        }

        return checksum;
    }
}
