using System.Globalization;
using Trimix.Core.Abstractions;

namespace Trimix.Core.DiveComputer;

/// <summary>
/// The BLE-direct-from-owned-hardware source connector (architecture overview §2). Wraps the existing
/// <see cref="IDiveComputerTransport"/> + the four-wide <see cref="IDiveComputerProtocol"/> abstraction
/// (their signatures unchanged), demoting the whole BLE stack to one <see cref="ConnectorKind.BleDevice"/>
/// connector. Reading the diver's own computer touches no vendor cloud or cloud ToS, so this connector is
/// <see cref="LegalPosture.OwnedHardware"/> and ships on.
///
/// Interactive-BLE affordances (<see cref="ScanAsync"/>, <see cref="SelectDevice"/>) are exposed beyond
/// the <see cref="IDiveSourceConnector"/> port because they are BLE-specific; the generic path uses only
/// the port methods. State (<c>_target</c>/<c>_buffer</c>) is per-session — the connector is a singleton
/// matching the single-import-at-a-time model, mirroring the singleton transport.
/// </summary>
public sealed class BleDeviceSourceConnector(
    IDiveComputerTransport transport,
    IEnumerable<IDiveComputerProtocol> protocols) : IDiveSourceConnector
{
    private DiscoveredComputer? _target;
    private IReadOnlyList<ImportedDive> _buffer = [];

    public ConnectorDescriptor Descriptor { get; } = new(
        ConnectorId: "ble_device",
        Kind: ConnectorKind.BleDevice,
        Legal: LegalPosture.OwnedHardware,
        Fidelity: ProfileFidelity.FullProfile,
        Auth: AuthModel.OnDeviceConsent,
        Platforms: Platforms.Both,
        EnabledByDefault: true);

    /// <summary>BLE-specific affordance (not on the port): scans for nearby dive computers until cancelled.</summary>
    public IAsyncEnumerable<DiscoveredComputer> ScanAsync(CancellationToken cancellationToken = default) =>
        transport.ScanAsync(cancellationToken);

    /// <summary>BLE-specific affordance (not on the port): pins the discovered device this session imports from.</summary>
    public void SelectDevice(DiscoveredComputer computer)
    {
        ArgumentNullException.ThrowIfNull(computer);
        _target = computer;
    }

    /// <summary>BLE permission is handled by the VM this phase; the connector reports Granted.</summary>
    public Task<ConnectorAuthResult> AuthenticateAsync(CancellationToken ct = default) =>
        Task.FromResult(new ConnectorAuthResult(ConnectorAuthStatus.Granted));

    public async Task<IReadOnlyList<DiveRef>> ListDivesSinceAsync(SyncCursor cursor, CancellationToken ct = default)
    {
        var target = _target ?? throw new InvalidOperationException("SelectDevice must be called before listing dives.");
        var protocol = protocols.FirstOrDefault(p => p.Vendor == target.Vendor)
            ?? throw new NotSupportedException($"No protocol registered for vendor '{target.Vendor}'.");

        await transport.ConnectAsync(target, ct).ConfigureAwait(false);
        try
        {
            _buffer = await protocol.ParseAsync(transport.ReadFramesAsync(ct), ct).ConfigureAwait(false);
        }
        finally
        {
            await transport.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
        }

        var refs = new List<DiveRef>(_buffer.Count);
        for (var i = 0; i < _buffer.Count; i++)
        {
            var dive = _buffer[i];
            refs.Add(new DiveRef(
                Descriptor.ConnectorId,
                i.ToString(CultureInfo.InvariantCulture),
                dive.StartUtc,
                dive.DurationSec));
        }

        return refs;
    }

    public Task<ImportedDive> FetchProfileAsync(DiveRef diveRef, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(diveRef);
        var index = int.Parse(diveRef.SourceId, CultureInfo.InvariantCulture);
        return Task.FromResult(_buffer[index]);
    }
}
