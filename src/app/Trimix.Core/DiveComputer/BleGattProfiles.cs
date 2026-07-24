using Trimix.Core.Abstractions;

namespace Trimix.Core.DiveComputer;

/// <summary>
/// One vendor's BLE GATT connection profile — the pure, testable parameter set the platform
/// <c>BleTransport</c>s consume to identify a discovered device and drive its connect →
/// resolve-characteristics → enable-notify → handshake → notify-frame pipeline.
///
/// <para><b>Honesty contract (Slice 3).</b> Only values that are publicly documented are pinned here.
/// Where a value is not public it is left null/empty and resolved at runtime (characteristics by GATT
/// property) or supplied as a deploy input via <see cref="BleGattOverrides"/> — never guessed. The one
/// value pinned as fact is the Shearwater <b>service</b> UUID, which is public
/// (libdivecomputer / Subsurface <c>core/qt-ble.cpp</c>) and already lived in the transport skeletons.</para>
/// </summary>
/// <param name="Vendor">The dive-computer family this profile identifies and connects.</param>
/// <param name="AdvertisedServiceUuids">
/// Full 128-bit service UUIDs the device advertises, used for scan-time vendor identification and to
/// locate the vendor service after discovery. Empty ⇒ this vendor has no publicly documented service
/// UUID (identify by <paramref name="NameHints"/> only; locate the service by property at runtime).
/// </param>
/// <param name="WriteCharacteristicUuid">
/// Explicit write-characteristic UUID. Null ⇒ resolve at runtime by GATT property
/// (first characteristic with <c>Write</c> or <c>WriteWithoutResponse</c>), exactly as Subsurface's
/// <c>qt-ble.cpp</c> does — it hardcodes no per-vendor characteristic UUIDs.
/// </param>
/// <param name="NotifyCharacteristicUuid">
/// Explicit notify-characteristic UUID. Null ⇒ resolve at runtime by GATT property
/// (first characteristic with <c>Notify</c> or <c>Indicate</c>).
/// </param>
/// <param name="HandshakeCommands">
/// Ordered command frames written to the write characteristic (after notifications are enabled) to make
/// the computer start streaming its dive log. Empty ⇒ no handshake is written (device streams unprompted,
/// or the real command sequence is a deploy input — see the Shearwater TODO in <see cref="BleGattProfiles"/>).
/// </param>
/// <param name="NameHints">
/// Case-insensitive substrings of the advertised device name used to identify the vendor when the service
/// UUID is absent or unmatched. Subsumes the two duplicated <c>GuessVendor</c> methods.
/// </param>
public sealed record VendorGattProfile(
    DiveComputerVendor Vendor,
    IReadOnlyList<string> AdvertisedServiceUuids,
    string? WriteCharacteristicUuid,
    string? NotifyCharacteristicUuid,
    IReadOnlyList<byte[]> HandshakeCommands,
    IReadOnlyList<string> NameHints);

/// <summary>
/// The BLE GATT vendor map (architecture overview §4, docs §9.3): identifies a scanned device's
/// <see cref="DiveComputerVendor"/> by advertised service-UUID then name hints, and supplies the
/// per-vendor connect profile the platform transports drive. Static, snapshot-immutable table swapped
/// atomically by <see cref="BleGattOverrides"/> at startup; readers (scan callbacks) see a consistent
/// snapshot without locking.
///
/// <para>Only Shearwater has a public service UUID; every other vendor is name-hint-only with null
/// characteristic UUIDs (resolved by GATT property) and an empty handshake, pending real-hardware
/// validation — the M3 kill-line.</para>
/// </summary>
public static class BleGattProfiles
{
    /// <summary>
    /// Shearwater's proprietary BLE service. PUBLIC FACT — libdivecomputer / Subsurface
    /// <c>core/qt-ble.cpp</c>; also present in the shipped transport skeletons. Covers Perdix (AI),
    /// Petrel, Teric, Peregrine, and Tern.
    /// </summary>
    public const string ShearwaterServiceUuid = "fe25c237-0ece-443c-b0aa-e02033e7029d";

    // Snapshot-immutable table. Reads take the current reference; BleGattOverrides swaps a fresh
    // dictionary in atomically at startup, so no lock is needed on the hot scan path.
    private static volatile IReadOnlyDictionary<DiveComputerVendor, VendorGattProfile> _profiles = BuildDefaults();

    /// <summary>All vendor profiles in the current (possibly override-merged) table, in enum order.</summary>
    public static IReadOnlyList<VendorGattProfile> All => [.. _profiles.Values];

    /// <summary>The connect profile for <paramref name="vendor"/>, or null if none is registered.</summary>
    public static VendorGattProfile? For(DiveComputerVendor vendor) =>
        _profiles.TryGetValue(vendor, out var profile) ? profile : null;

    /// <summary>
    /// Identifies the vendor of a scanned device: advertised service UUID first (exact, case-insensitive),
    /// then advertised-name substring hints. Returns null for devices that match no known dive-computer
    /// family so callers ignore them. Subsumes the platform <c>GuessVendor</c> heuristics.
    /// </summary>
    public static DiveComputerVendor? TryIdentify(IReadOnlyList<string>? advertisedServiceUuids, string? name)
    {
        var snapshot = _profiles;

        // 1) Service-UUID match wins (the strong signal). Normalise both sides to lowercase 128-bit form.
        if (advertisedServiceUuids is { Count: > 0 })
        {
            var advertised = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var uuid in advertisedServiceUuids)
            {
                if (NormalizeUuid(uuid) is { } normalized)
                {
                    advertised.Add(normalized);
                }
            }

            foreach (var profile in snapshot.Values)
            {
                foreach (var serviceUuid in profile.AdvertisedServiceUuids)
                {
                    if (NormalizeUuid(serviceUuid) is { } normalized && advertised.Contains(normalized))
                    {
                        return profile.Vendor;
                    }
                }
            }
        }

        // 2) Name-hint fallback (case-insensitive, WORD-BOUNDED), for vendors without a public service UUID.
        // Boundary-matching (not raw substring) is what stops a short token like "Tern" from claiming
        // "Lantern Speaker", or "D5" from matching inside "HID512", as a dive computer.
        if (!string.IsNullOrWhiteSpace(name))
        {
            var trimmed = name.Trim();
            foreach (var profile in snapshot.Values)
            {
                foreach (var hint in profile.NameHints)
                {
                    if (ContainsHintAtWordBoundary(trimmed, hint))
                    {
                        return profile.Vendor;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// True when <paramref name="hint"/> appears in <paramref name="name"/> delimited by word boundaries —
    /// i.e. not immediately flanked by another letter or digit on either side. Prevents unbounded substring
    /// false-positives (e.g. "Tern" inside "Lantern") while still matching "Suunto D5" or "Aqualung i300".
    /// </summary>
    private static bool ContainsHintAtWordBoundary(string name, string hint)
    {
        if (string.IsNullOrEmpty(hint))
        {
            return false;
        }

        var index = name.IndexOf(hint, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            var beforeOk = index == 0 || !char.IsLetterOrDigit(name[index - 1]);
            var afterPosition = index + hint.Length;
            var afterOk = afterPosition >= name.Length || !char.IsLetterOrDigit(name[afterPosition]);
            if (beforeOk && afterOk)
            {
                return true;
            }

            index = name.IndexOf(hint, index + 1, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    /// <summary>
    /// Normalises a UUID string for comparison: lowercase, braces/whitespace stripped. A bare 16-/32-bit
    /// short UUID (<c>"XXXX"</c>) is expanded to its 128-bit Bluetooth Base form. Returns null for input
    /// that is null/blank. Public so the platform transports canonicalise discovered GATT UUIDs the same way.
    /// </summary>
    public static string? NormalizeUuid(string? uuid)
    {
        if (string.IsNullOrWhiteSpace(uuid))
        {
            return null;
        }

        var cleaned = uuid.Trim().Trim('{', '}').ToLowerInvariant();
        if (cleaned.Length is 4 or 8 && IsHex(cleaned))
        {
            // 16-/32-bit short UUID → Bluetooth Base UUID (0000XXXX-0000-1000-8000-00805f9b34fb).
            var prefix = cleaned.PadLeft(8, '0');
            return $"{prefix}-0000-1000-8000-00805f9b34fb";
        }

        return cleaned;
    }

    private static bool IsHex(string value)
    {
        foreach (var c in value)
        {
            if (!Uri.IsHexDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Atomically replaces (or inserts) one vendor's profile. Used by <see cref="BleGattOverrides"/>.</summary>
    internal static void Replace(VendorGattProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var next = new Dictionary<DiveComputerVendor, VendorGattProfile>(_profiles)
        {
            [profile.Vendor] = profile,
        };
        _profiles = next;
    }

    /// <summary>Restores the in-source default table (drops any applied overrides). Primarily for tests.</summary>
    public static void ResetToDefaults() => _profiles = BuildDefaults();

    private static IReadOnlyDictionary<DiveComputerVendor, VendorGattProfile> BuildDefaults()
    {
        var profiles = new VendorGattProfile[]
        {
            // ── Shearwater ── the one fully-real-in-source vendor for M1/M2. Service UUID is public fact.
            // Characteristic UUIDs are LEFT NULL and resolved by GATT property at connect time (Write /
            // WriteWithoutResponse for write; Notify / Indicate for notify) — Subsurface's qt-ble.cpp does
            // exactly this and hardcodes no per-vendor characteristic UUIDs.
            //
            // ⚠ VERIFY-BEFORE-PIN candidates (community BLE scans, NOT validated in-repo against hardware):
            //   write/notify characteristics under this service have been reported as
            //     27b7570b-359e-45a3-91bb-cf7e70049bd2  (write + write-no-response + notify)
            //     27b7570a-359e-45a3-91bb-cf7e70049bd2  (read/notify)
            //   Do NOT pin these as WriteCharacteristicUuid/NotifyCharacteristicUuid until confirmed on a
            //   real Perdix 2; supply them via BleGattOverrides once validated. Property-resolution below
            //   is correct regardless of their exact identities.
            //
            // ⚠ TODO(M3 hardware): the real download handshake is EMPTY here on purpose. libdivecomputer's
            //   shearwater_common.c initiates a download with a parametric PC-protocol request
            //   (req_init = { 0x35, compression?0x10:0x00, 0x34, <addr:be32>, <blocksize:be24> } → device
            //   replies 0x75,0x10,…, then sequential 0x36 block reads). Transcribing that command sequence
            //   AND mapping the returned log format onto ImportedDive is hardware-gated M3 work and out of
            //   this slice; until then the shipped ShearwaterProtocol fixture-frame parser stays the decoder.
            //   Operators/BD supply validated handshake bytes via BleGattOverrides (Ble:Shearwater:HandshakeCommandsHex).
            new(
                DiveComputerVendor.Shearwater,
                AdvertisedServiceUuids: [ShearwaterServiceUuid],
                WriteCharacteristicUuid: null,
                NotifyCharacteristicUuid: null,
                HandshakeCommands: [],
                NameHints: ["Perdix", "Petrel", "Teric", "Peregrine", "Tern", "Shearwater"]),

            // ── Suunto ── no publicly documented BLE service UUID → name-hint identification only; service +
            // characteristics resolved by property at runtime; handshake is a deploy input (BleGattOverrides).
            new(
                DiveComputerVendor.Suunto,
                AdvertisedServiceUuids: [],
                WriteCharacteristicUuid: null,
                NotifyCharacteristicUuid: null,
                HandshakeCommands: [],
                NameHints: ["Suunto", "EON Steel", "EON Core", "D5"]),

            // ── Garmin ── name-hint only. NOTE: Garmin blocks third-party BLE dive download (FIT/Garmin cloud
            // is the only path — see GarminProtocol NotSupported placeholder). This entry lets a Descent be
            // recognised so the UI can explain the cloud path; no BLE handshake will ever be valid here.
            new(
                DiveComputerVendor.Garmin,
                AdvertisedServiceUuids: [],
                WriteCharacteristicUuid: null,
                NotifyCharacteristicUuid: null,
                HandshakeCommands: [],
                NameHints: ["Descent", "Garmin"]),

            // ── Mares ── name-hint only; service/characteristics by property; handshake is a deploy input.
            new(
                DiveComputerVendor.Mares,
                AdvertisedServiceUuids: [],
                WriteCharacteristicUuid: null,
                NotifyCharacteristicUuid: null,
                HandshakeCommands: [],
                NameHints: ["Mares", "Genius", "Puck", "Quad Ci"]),

            // ── ScubaPro / Aqualung ── real clean-room protocol parsers exist, so wire identification too.
            // Name hints are HEURISTIC and unvalidated against hardware advertised names; service +
            // characteristics resolved by property; handshake is a deploy input. Kept conservative
            // (distinctive model tokens) to avoid false positives.
            new(
                DiveComputerVendor.ScubaPro,
                AdvertisedServiceUuids: [],
                WriteCharacteristicUuid: null,
                NotifyCharacteristicUuid: null,
                HandshakeCommands: [],
                NameHints: ["Scubapro", "Galileo", "Aladin", "Luna"]),
            new(
                DiveComputerVendor.Aqualung,
                AdvertisedServiceUuids: [],
                WriteCharacteristicUuid: null,
                NotifyCharacteristicUuid: null,
                HandshakeCommands: [],
                NameHints: ["Aqualung", "Apeks", "i770", "i330", "i300", "DSX"]),
        };

        // Deterministic order (enum value) so TryIdentify is stable when a name could match two families.
        return profiles.OrderBy(p => (int)p.Vendor).ToDictionary(p => p.Vendor);
    }
}
