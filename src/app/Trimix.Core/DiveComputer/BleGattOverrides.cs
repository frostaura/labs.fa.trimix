using System.Globalization;
using Trimix.Core.Abstractions;
using Trimix.Core.Configuration;

namespace Trimix.Core.DiveComputer;

/// <summary>
/// Merges deploy-time BLE overrides (Slice 4's <see cref="TrimixClientConfig.Ble"/> —
/// <c>Dictionary&lt;vendorName, <see cref="BleVendorConfig"/>&gt;</c>) into the static
/// <see cref="BleGattProfiles"/> table at startup. The integrator calls
/// <c>BleGattOverrides.Apply(config.Ble)</c> once in MauiProgram (before/after <c>AddTrimixCore</c>) —
/// no DI change and no glue, since this consumes the config type Slice 4 already ships.
///
/// <para>This is the injection point for the genuinely-secret / hardware-gated inputs Slice 3 refuses to
/// guess: the real advertised service UUID, the write/notify characteristic UUIDs once confirmed on
/// hardware, and the validated download-handshake bytes. Default no-op: a null or empty dictionary — or a
/// vendor bag whose fields are all null — leaves the in-source profiles untouched.</para>
/// </summary>
public static class BleGattOverrides
{
    public static void Apply(IReadOnlyDictionary<string, BleVendorConfig>? overrides)
    {
        if (overrides is not { Count: > 0 })
        {
            return;
        }

        foreach (var (vendorName, ovr) in overrides)
        {
            if (ovr is null || !Enum.TryParse<DiveComputerVendor>(vendorName, ignoreCase: true, out var vendor))
            {
                continue; // Unknown vendor key — ignore (never invent a profile for it).
            }

            var current = BleGattProfiles.For(vendor);
            if (current is null)
            {
                continue;
            }

            // Service UUID: union (keep the in-source public UUID, add the operator's real one, deduped).
            var serviceUuids = current.AdvertisedServiceUuids;
            if (!string.IsNullOrWhiteSpace(ovr.ServiceUuid) &&
                !serviceUuids.Any(u => string.Equals(u, ovr.ServiceUuid, StringComparison.OrdinalIgnoreCase)))
            {
                serviceUuids = [.. serviceUuids, ovr.ServiceUuid.Trim()];
            }

            // Characteristics: an explicit override replaces the null "resolve-by-property" default.
            var writeChar = string.IsNullOrWhiteSpace(ovr.WriteCharUuid) ? current.WriteCharacteristicUuid : ovr.WriteCharUuid.Trim();
            var notifyChar = string.IsNullOrWhiteSpace(ovr.NotifyCharUuid) ? current.NotifyCharacteristicUuid : ovr.NotifyCharUuid.Trim();

            // Handshake: a non-empty, well-formed hex value replaces the (empty) in-source handshake.
            var handshake = current.HandshakeCommands;
            var parsed = ParseHandshake(ovr.HandshakeCommandsHex);
            if (parsed.Count > 0)
            {
                handshake = parsed;
            }

            BleGattProfiles.Replace(current with
            {
                AdvertisedServiceUuids = serviceUuids,
                WriteCharacteristicUuid = writeChar,
                NotifyCharacteristicUuid = notifyChar,
                HandshakeCommands = handshake,
            });
        }
    }

    /// <summary>
    /// Parses the deploy handshake string into ordered command frames. Multiple commands may be given
    /// separated by <c>,</c> / <c>;</c> / <c>|</c> / newlines; whitespace, <c>0x</c>, <c>-</c> and <c>:</c>
    /// WITHIN a command are byte separators. Malformed (odd-length / non-hex) commands are dropped.
    /// </summary>
    internal static IReadOnlyList<byte[]> ParseHandshake(string? handshakeHex)
    {
        if (string.IsNullOrWhiteSpace(handshakeHex))
        {
            return [];
        }

        var commands = new List<byte[]>();
        foreach (var token in handshakeHex.Split([',', ';', '|', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (ParseHex(token) is { Length: > 0 } bytes)
            {
                commands.Add(bytes);
            }
        }

        return commands;
    }

    /// <summary>
    /// Parses one hex command into bytes, tolerating <c>0x</c> prefixes and <c>-</c>/<c>:</c>/whitespace
    /// byte separators. Returns an empty array for input that is null, empty, or not valid even-length hex.
    /// </summary>
    internal static byte[] ParseHex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return [];
        }

        var cleaned = hex
            .Replace("0x", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(":", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("\t", string.Empty, StringComparison.Ordinal);

        if (cleaned.Length == 0 || cleaned.Length % 2 != 0)
        {
            return [];
        }

        var bytes = new byte[cleaned.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            if (!byte.TryParse(cleaned.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bytes[i]))
            {
                return [];
            }
        }

        return bytes;
    }
}
