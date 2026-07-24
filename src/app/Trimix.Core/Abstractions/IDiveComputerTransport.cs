namespace Trimix.Core.Abstractions;

/// <summary>
/// The dive-computer vendors the BLE import abstraction targets. Shearwater/Suunto/ScubaPro/Aqualung/Mares
/// have real clean-room protocol parsers; Garmin stays a NotSupported placeholder (Garmin blocks third-party
/// BLE dive download — FIT/cloud is the only Garmin path). Values are append-only (never renumber).
/// </summary>
public enum DiveComputerVendor
{
    Shearwater = 0,
    Suunto = 1,
    Garmin = 2,
    Mares = 3,
    ScubaPro = 4,
    Aqualung = 5,
}

/// <summary>A dive computer discovered during a BLE scan.</summary>
public sealed record DiscoveredComputer(DiveComputerVendor Vendor, string Model, string Id);

/// <summary>One raw frame read from a connected dive computer, parsed by an <see cref="IDiveComputerProtocol"/>.</summary>
public sealed record DiveComputerFrame(ReadOnlyMemory<byte> Payload);

/// <summary>
/// BLE transport abstraction (architecture overview §4). Implementations: SimulatorTransport
/// (deterministic fixture frames in Core; default in Debug) and BleTransport (App/Android, behind DI flag).
/// </summary>
public interface IDiveComputerTransport
{
    /// <summary>Scans for nearby dive computers until cancelled.</summary>
    IAsyncEnumerable<DiscoveredComputer> ScanAsync(CancellationToken cancellationToken = default);

    Task ConnectAsync(DiscoveredComputer computer, CancellationToken cancellationToken = default);

    /// <summary>Streams raw frames from the connected computer.</summary>
    IAsyncEnumerable<DiveComputerFrame> ReadFramesAsync(CancellationToken cancellationToken = default);

    Task DisconnectAsync(CancellationToken cancellationToken = default);
}
