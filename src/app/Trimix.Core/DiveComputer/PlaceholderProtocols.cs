using Trimix.Core.Abstractions;

namespace Trimix.Core.DiveComputer;

// Only Garmin remains a typed NotSupported placeholder: Garmin deliberately blocks third-party BLE dive
// download, so the clean Garmin path is FIT/cloud import, never a BLE protocol. Suunto, ScubaPro, Aqualung
// and Mares now have real clean-room parsers under DiveComputer/Protocols; Shearwater ships in this namespace.

/// <summary>
/// Garmin BLE protocol placeholder — Garmin blocks third-party BLE dive download, so there is no BLE
/// protocol to implement; the clean Garmin path is FIT file / cloud import (Connectors/FileImport + Cloud).
/// </summary>
public sealed class GarminProtocol : IDiveComputerProtocol
{
    public DiveComputerVendor Vendor => DiveComputerVendor.Garmin;

    public Task<IReadOnlyList<ImportedDive>> ParseAsync(
        IAsyncEnumerable<DiveComputerFrame> frames,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "Garmin has no third-party BLE dive download; import Garmin dives via FIT file or the Garmin cloud connector.");
}
