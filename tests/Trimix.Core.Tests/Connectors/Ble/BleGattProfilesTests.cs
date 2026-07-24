using Trimix.Core.Abstractions;
using Trimix.Core.Configuration;
using Trimix.Core.DiveComputer;
using Xunit;

namespace Trimix.Core.Tests.Connectors.Ble;

/// <summary>
/// <see cref="BleGattProfiles"/> vendor identification + <see cref="BleGattOverrides"/> merge (docs §9.3).
/// The static profile table is process-global, so every test resets it to the in-source defaults first;
/// override tests reset again on the way out via <see cref="IDisposable"/> so ordering cannot leak state.
/// </summary>
public sealed class BleGattProfilesTests : IDisposable
{
    public BleGattProfilesTests() => BleGattProfiles.ResetToDefaults();

    public void Dispose() => BleGattProfiles.ResetToDefaults();

    // ── Identification: service UUID ──────────────────────────────────────────────────────────────

    [Fact]
    public void TryIdentify_ByServiceUuid_ResolvesShearwater()
    {
        var vendor = BleGattProfiles.TryIdentify([BleGattProfiles.ShearwaterServiceUuid], name: "XYZ");

        Assert.Equal(DiveComputerVendor.Shearwater, vendor);
    }

    [Fact]
    public void TryIdentify_ByServiceUuid_IsCaseInsensitiveAndBraceTolerant()
    {
        var vendor = BleGattProfiles.TryIdentify(
            ["{FE25C237-0ECE-443C-B0AA-E02033E7029D}"], name: null);

        Assert.Equal(DiveComputerVendor.Shearwater, vendor);
    }

    [Fact]
    public void TryIdentify_ServiceUuidWins_OverAConflictingName()
    {
        // Advertises the Shearwater service but a Suunto-ish name — service UUID is the strong signal.
        var vendor = BleGattProfiles.TryIdentify([BleGattProfiles.ShearwaterServiceUuid], name: "Suunto D5");

        Assert.Equal(DiveComputerVendor.Shearwater, vendor);
    }

    // ── Identification: name hints (subsumes GuessVendor) ─────────────────────────────────────────

    [Theory]
    [InlineData("Perdix 2", DiveComputerVendor.Shearwater)]
    [InlineData("Shearwater Teric", DiveComputerVendor.Shearwater)]
    [InlineData("Peregrine TX", DiveComputerVendor.Shearwater)]
    [InlineData("Suunto EON Steel", DiveComputerVendor.Suunto)]
    [InlineData("EON Core", DiveComputerVendor.Suunto)]
    [InlineData("Garmin Descent Mk3i", DiveComputerVendor.Garmin)]
    [InlineData("Mares Genius", DiveComputerVendor.Mares)]
    [InlineData("Scubapro Galileo 3", DiveComputerVendor.ScubaPro)]
    [InlineData("Aqualung i770R", DiveComputerVendor.Aqualung)]
    public void TryIdentify_ByNameHint_ResolvesVendor(string advertisedName, DiveComputerVendor expected)
    {
        var vendor = BleGattProfiles.TryIdentify(advertisedServiceUuids: null, name: advertisedName);

        Assert.Equal(expected, vendor);
    }

    [Fact]
    public void TryIdentify_UnknownDevice_ReturnsNull()
    {
        Assert.Null(BleGattProfiles.TryIdentify(["0000180f-0000-1000-8000-00805f9b34fb"], name: "Fitbit Charge"));
        Assert.Null(BleGattProfiles.TryIdentify(advertisedServiceUuids: null, name: "Some Random Speaker"));
        Assert.Null(BleGattProfiles.TryIdentify(advertisedServiceUuids: null, name: null));
    }

    [Theory]
    [InlineData("Lantern Speaker")] // contains "Tern" but not word-bounded — must NOT be claimed as Shearwater
    [InlineData("Kids D500 Toy")]   // contains "D5" inside "D500" — not a Suunto D5
    [InlineData("HID512 Sensor")]   // "D5" mid-token again
    public void TryIdentify_UnboundedSubstring_DoesNotFalseMatch(string advertisedName) =>
        Assert.Null(BleGattProfiles.TryIdentify(advertisedServiceUuids: null, name: advertisedName));

    [Theory]
    [InlineData("Suunto D5", DiveComputerVendor.Suunto)]
    [InlineData("D5", DiveComputerVendor.Suunto)]           // the model token on its own is word-bounded
    [InlineData("Shearwater Tern", DiveComputerVendor.Shearwater)]
    public void TryIdentify_WordBoundedHint_StillResolves(string advertisedName, DiveComputerVendor expected) =>
        Assert.Equal(expected, BleGattProfiles.TryIdentify(advertisedServiceUuids: null, name: advertisedName));

    // ── Default table facts ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void Defaults_ShearwaterServiceUuidPresent_AndCharsResolveByProperty()
    {
        var shearwater = BleGattProfiles.For(DiveComputerVendor.Shearwater);

        Assert.NotNull(shearwater);
        Assert.Contains(BleGattProfiles.ShearwaterServiceUuid, shearwater!.AdvertisedServiceUuids);
        // Char UUIDs are deliberately null (resolve by GATT property; candidates are verify-before-pin).
        Assert.Null(shearwater.WriteCharacteristicUuid);
        Assert.Null(shearwater.NotifyCharacteristicUuid);
        // No guessed handshake ships — the real one is a hardware-gated deploy input.
        Assert.Empty(shearwater.HandshakeCommands);
    }

    [Fact]
    public void Defaults_AllSixVendorsRegistered()
    {
        foreach (var vendor in Enum.GetValues<DiveComputerVendor>())
        {
            Assert.NotNull(BleGattProfiles.For(vendor));
        }
    }

    [Fact]
    public void Defaults_OnlyShearwaterHasAPublicServiceUuid()
    {
        foreach (var profile in BleGattProfiles.All)
        {
            if (profile.Vendor == DiveComputerVendor.Shearwater)
            {
                Assert.NotEmpty(profile.AdvertisedServiceUuids);
            }
            else
            {
                Assert.Empty(profile.AdvertisedServiceUuids);
            }
        }
    }

    // ── Override merge ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Apply_Override_PinsCharUuids_AndParsesHandshake_AndUnionsServiceUuid()
    {
        BleGattOverrides.Apply(new Dictionary<string, BleVendorConfig>
        {
            ["Shearwater"] = new BleVendorConfig
            {
                ServiceUuid = "0000abcd-0000-1000-8000-00805f9b34fb",
                WriteCharUuid = "27b7570b-359e-45a3-91bb-cf7e70049bd2",
                NotifyCharUuid = "27b7570a-359e-45a3-91bb-cf7e70049bd2",
                // Two comma-separated commands; internal whitespace/0x/- are byte separators.
                HandshakeCommandsHex = "35 10 34 00 00 00 00, 0x36-00",
            },
        });

        var shearwater = BleGattProfiles.For(DiveComputerVendor.Shearwater)!;

        Assert.Equal("27b7570b-359e-45a3-91bb-cf7e70049bd2", shearwater.WriteCharacteristicUuid);
        Assert.Equal("27b7570a-359e-45a3-91bb-cf7e70049bd2", shearwater.NotifyCharacteristicUuid);

        // Public UUID is retained and the operator UUID is added (union, not replace).
        Assert.Contains(BleGattProfiles.ShearwaterServiceUuid, shearwater.AdvertisedServiceUuids);
        Assert.Contains("0000abcd-0000-1000-8000-00805f9b34fb", shearwater.AdvertisedServiceUuids);

        // Handshake hex parsed (separators/0x tolerated) into ordered byte frames.
        Assert.Equal(2, shearwater.HandshakeCommands.Count);
        Assert.Equal(new byte[] { 0x35, 0x10, 0x34, 0x00, 0x00, 0x00, 0x00 }, shearwater.HandshakeCommands[0]);
        Assert.Equal(new byte[] { 0x36, 0x00 }, shearwater.HandshakeCommands[1]);
    }

    [Fact]
    public void Apply_IsCaseInsensitiveOnVendorKey()
    {
        BleGattOverrides.Apply(new Dictionary<string, BleVendorConfig>
        {
            ["shEArWaTeR"] = new BleVendorConfig { WriteCharUuid = "27b7570b-359e-45a3-91bb-cf7e70049bd2" },
        });

        Assert.Equal(
            "27b7570b-359e-45a3-91bb-cf7e70049bd2",
            BleGattProfiles.For(DiveComputerVendor.Shearwater)!.WriteCharacteristicUuid);
    }

    [Fact]
    public void Apply_UnknownVendorKey_IsIgnored()
    {
        BleGattOverrides.Apply(new Dictionary<string, BleVendorConfig>
        {
            ["Cressi"] = new BleVendorConfig { WriteCharUuid = "0000abcd-0000-1000-8000-00805f9b34fb" },
        });

        // No profile invented, defaults untouched.
        Assert.Null(BleGattProfiles.For(DiveComputerVendor.Shearwater)!.WriteCharacteristicUuid);
    }

    [Fact]
    public void Apply_Null_IsNoOp()
    {
        BleGattOverrides.Apply(null);

        var shearwater = BleGattProfiles.For(DiveComputerVendor.Shearwater)!;
        Assert.Null(shearwater.WriteCharacteristicUuid);
        Assert.Empty(shearwater.HandshakeCommands);
    }

    [Fact]
    public void Apply_EmptyDictionary_LeavesDefaultsIntact()
    {
        BleGattOverrides.Apply(new Dictionary<string, BleVendorConfig>());

        Assert.Single(BleGattProfiles.For(DiveComputerVendor.Shearwater)!.AdvertisedServiceUuids);
    }

    [Fact]
    public void Apply_HandshakeHex_DropsMalformedCommands_KeepsValidOnes()
    {
        BleGattOverrides.Apply(new Dictionary<string, BleVendorConfig>
        {
            // "3" (odd length) and "zz" (non-hex) are dropped; "AB:CD" survives.
            ["Mares"] = new BleVendorConfig { HandshakeCommandsHex = "3, zz, AB:CD" },
        });

        var mares = BleGattProfiles.For(DiveComputerVendor.Mares)!;
        Assert.Single(mares.HandshakeCommands);
        Assert.Equal(new byte[] { 0xAB, 0xCD }, mares.HandshakeCommands[0]);
    }

    [Fact]
    public void Apply_AllMalformedHandshake_LeavesHandshakeEmpty()
    {
        BleGattOverrides.Apply(new Dictionary<string, BleVendorConfig>
        {
            ["Mares"] = new BleVendorConfig { HandshakeCommandsHex = "3, zz" },
        });

        Assert.Empty(BleGattProfiles.For(DiveComputerVendor.Mares)!.HandshakeCommands);
    }
}
