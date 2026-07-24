using Trimix.Core.Abstractions;

namespace Trimix.Core.Tests.Connectors.Ble;

/// <summary>
/// Committed fixture-frame builders for the retained-BLE vendor parsers (Suunto / ScubaPro / Aqualung /
/// Mares). These are the deterministic, offline counterpart to the parsers' documented frame contracts — no
/// network, no hardware. Two envelope shapes cover all four vendors: a 2-byte-length + XOR envelope
/// (Suunto/Aqualung/Mares) and a 1-byte-length + additive-checksum envelope (ScubaPro/Uwatec). The parsers'
/// own primitives are <c>internal</c>, so the checksums are recomputed here independently — a fixture and a
/// parser that agree via two independent checksum implementations is a real cross-check, not a tautology.
/// </summary>
internal static class BleFrameBuilder
{
    /// <summary>2-byte little-endian length + trailing XOR checksum (Suunto/Aqualung/Mares envelope).</summary>
    public static DiveComputerFrame XorFrame2(byte magic, byte type, ReadOnlySpan<byte> payload)
    {
        var bytes = new byte[5 + payload.Length];
        bytes[0] = magic;
        bytes[1] = type;
        bytes[2] = (byte)(payload.Length & 0xFF);
        bytes[3] = (byte)(payload.Length >> 8);
        payload.CopyTo(bytes.AsSpan(4));
        byte xor = 0;
        for (var i = 0; i < bytes.Length - 1; i++)
        {
            xor ^= bytes[i];
        }

        bytes[^1] = xor;
        return new DiveComputerFrame(bytes);
    }

    /// <summary>1-byte length + trailing additive (mod-256) checksum (ScubaPro/Uwatec envelope).</summary>
    public static DiveComputerFrame AdditiveFrame1(byte magic, byte type, ReadOnlySpan<byte> payload)
    {
        var bytes = new byte[4 + payload.Length];
        bytes[0] = magic;
        bytes[1] = type;
        bytes[2] = (byte)payload.Length;
        payload.CopyTo(bytes.AsSpan(3));
        byte sum = 0;
        for (var i = 0; i < bytes.Length - 1; i++)
        {
            sum = unchecked((byte)(sum + bytes[i]));
        }

        bytes[^1] = sum;
        return new DiveComputerFrame(bytes);
    }

    /// <summary>Unix seconds (UTC) for a fixture instant.</summary>
    public static uint UnixSeconds(DateTime utc) => (uint)new DateTimeOffset(utc, TimeSpan.Zero).ToUnixTimeSeconds();

    /// <summary>Unix milliseconds (UTC) for a fixture instant (Suunto header uses ms).</summary>
    public static long UnixMillis(DateTime utc) => new DateTimeOffset(utc, TimeSpan.Zero).ToUnixTimeMilliseconds();
}
