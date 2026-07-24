using System.Runtime.CompilerServices;
using Trimix.Core.Abstractions;

namespace Trimix.Core.DiveComputer;

/// <summary>
/// Deterministic <see cref="IDiveComputerTransport"/> (architecture overview §4): "discovers" a
/// simulated Shearwater Perdix 2 and streams the embedded fixture frames. Default transport in
/// Debug; the App's BleTransport (Android BLE) replaces it behind the DI flag.
/// </summary>
public sealed class SimulatorTransport : IDiveComputerTransport
{
    private DiscoveredComputer? _connected;

    public async IAsyncEnumerable<DiscoveredComputer> ScanAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        yield return ShearwaterFixtures.Computer;
    }

    public Task ConnectAsync(DiscoveredComputer computer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(computer);
        _connected = computer;
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<DiveComputerFrame> ReadFramesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_connected is null)
        {
            throw new InvalidOperationException("Not connected — call ConnectAsync before reading frames.");
        }

        await Task.Yield();
        foreach (var frame in ShearwaterFixtures.BuildFrames())
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return frame;
        }
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        _connected = null;
        return Task.CompletedTask;
    }
}
