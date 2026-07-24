using System.Runtime.CompilerServices;
using System.Threading.Channels;
using CoreBluetooth;
using CoreFoundation;
using Foundation;
using Microsoft.Extensions.Logging;
using Trimix.Core.Abstractions;
using Trimix.Core.DiveComputer;

namespace Trimix.App.Ble;

/// <summary>
/// iOS BLE <see cref="IDiveComputerTransport"/> (architecture overview §4, docs §9.3), the CoreBluetooth
/// twin of the Android <c>BleTransport</c>: same namespace + class name so DI wiring is uniform. Registered
/// only behind the DI flag (<c>UseSimulatorTransport == false</c>, i.e. Release); SimulatorTransport stays
/// the Debug default. Compiled always.
///
/// <para><b>Pipeline.</b> Scan → identify the device's <see cref="DiveComputerVendor"/> from its advertised
/// service UUIDs + name via <see cref="BleGattProfiles"/> → connect → discover services → resolve the write
/// and notify characteristics (explicit profile UUID if supplied, else by GATT property — first
/// <c>Write</c>/<c>WriteWithoutResponse</c> and first <c>Notify</c>/<c>Indicate</c>, exactly as Subsurface's
/// <c>qt-ble.cpp</c>) → enable notifications → write the profile's download handshake → surface notification
/// bytes as <see cref="DiveComputerFrame"/>s into the vendor parser.</para>
///
/// <para><b>Honest status (M3 kill-line).</b> Only the Shearwater <b>service</b> UUID is a pinned public fact
/// (libdivecomputer / Subsurface). Characteristic UUIDs resolve by property; the real per-vendor download
/// handshake bytes and any undocumented characteristic UUIDs are DEPLOY INPUTS supplied via
/// <see cref="BleGattOverrides"/> — never guessed.</para>
///
/// <para>The iOS Simulator has NO Bluetooth radio: <see cref="CBCentralManager"/> reports
/// <see cref="CBManagerState.Unsupported"/>, so this class throws a clear
/// <see cref="DiveComputerProtocolException"/> there. Real CoreBluetooth validation requires a physical
/// device and is scheduled M3 work. Callers must hold the BLE runtime permission before use
/// (BlePermissionService) — NSBluetoothAlwaysUsageDescription is declared in Info.plist.</para>
/// </summary>
public sealed class BleTransport(ILogger<BleTransport> logger) : IDiveComputerTransport
{
    private readonly Dictionary<string, CBPeripheral> _discovered = new(StringComparer.Ordinal);
    private CBCentralManager? _manager;
    private CBPeripheral? _peripheral;
    private Channel<DiveComputerFrame>? _frames;
    private TaskCompletionSource<bool>? _connected;

    // Per-session connect state, driven by the vendor profile (reset each ConnectAsync).
    private VendorGattProfile? _profile;
    private CBCharacteristic? _writeCharacteristic;
    private CBCharacteristicWriteType _writeType = CBCharacteristicWriteType.WithResponse;
    private readonly Queue<byte[]> _handshake = new();

    public async IAsyncEnumerable<DiscoveredComputer> ScanAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var manager = EnsureManager();
        await WaitForPoweredOnAsync(manager, cancellationToken).ConfigureAwait(false);

        var channel = Channel.CreateUnbounded<DiscoveredComputer>();
        void OnDiscovered(object? sender, CBDiscoveredPeripheralEventArgs args)
        {
            var peripheral = args.Peripheral;
            var name = peripheral?.Name;
            if (peripheral is null || string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            if (BleGattProfiles.TryIdentify(ExtractAdvertisedServiceUuids(args.AdvertisementData), name) is not { } vendor)
            {
                return; // Not a known dive computer family — ignore.
            }

            var id = peripheral.Identifier.AsString();
            lock (_discovered)
            {
                if (!_discovered.TryAdd(id, peripheral))
                {
                    return; // Already reported this device during this scan.
                }
            }

            channel.Writer.TryWrite(new DiscoveredComputer(vendor, name, id));
        }

        manager.DiscoveredPeripheral += OnDiscovered;
        manager.ScanForPeripherals(ScanFilterServices());
        try
        {
            while (await channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (channel.Reader.TryRead(out var computer))
                {
                    yield return computer;
                }
            }
        }
        finally
        {
            manager.DiscoveredPeripheral -= OnDiscovered;
            try
            {
                manager.StopScan();
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "StopScan after BLE scan ended failed (adapter off?).");
            }
        }
    }

    /// <summary>
    /// Scan filter: the union of every profile's advertised service UUIDs, UNLESS some vendor is
    /// name-hint-only (no advertised service UUID) — in which case we must scan all peripherals (null)
    /// so those devices aren't filtered out, then identify by name in the callback.
    /// </summary>
    private static CBUUID[]? ScanFilterServices()
    {
        var anyNameOnly = BleGattProfiles.All.Any(p => p.AdvertisedServiceUuids.Count == 0 && p.NameHints.Count > 0);
        if (anyNameOnly)
        {
            return null;
        }

        var uuids = BleGattProfiles.All
            .SelectMany(p => p.AdvertisedServiceUuids)
            .Select(CBUUID.FromString)
            .ToArray();
        return uuids.Length > 0 ? uuids : null;
    }

    private static IReadOnlyList<string>? ExtractAdvertisedServiceUuids(NSDictionary? advertisementData)
    {
        if (advertisementData is null ||
            !advertisementData.TryGetValue(CBAdvertisement.DataServiceUUIDsKey, out var value) ||
            value is not NSArray array)
        {
            return null;
        }

        var result = new List<string>((int)array.Count);
        for (nuint i = 0; i < array.Count; i++)
        {
            if (array.GetItem<CBUUID>(i) is { } uuid)
            {
                result.Add(uuid.ToString());
            }
        }

        return result;
    }

    public async Task ConnectAsync(DiscoveredComputer computer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(computer);

        var manager = EnsureManager();
        await WaitForPoweredOnAsync(manager, cancellationToken).ConfigureAwait(false);

        CBPeripheral? peripheral;
        lock (_discovered)
        {
            _discovered.TryGetValue(computer.Id, out peripheral);
        }

        // Not seen this session? Ask CoreBluetooth to rehydrate it from its stored identifier.
        peripheral ??= manager.RetrievePeripheralsWithIdentifiers(new NSUuid(computer.Id)).FirstOrDefault();
        if (peripheral is null)
        {
            throw new DiveComputerProtocolException($"Dive computer '{computer.Model}' is no longer reachable.");
        }

        _profile = BleGattProfiles.For(computer.Vendor);
        _writeCharacteristic = null;
        _writeType = CBCharacteristicWriteType.WithResponse;
        _handshake.Clear();
        _frames = Channel.CreateUnbounded<DiveComputerFrame>();
        _connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Re-wire from any prior connection to the same peripheral instance, then subscribe fresh.
        if (_peripheral is { } previous)
        {
            UnsubscribePeripheral(previous);
        }

        _peripheral = peripheral;
        SubscribePeripheral(peripheral);

        manager.ConnectPeripheral(peripheral);

        using var registration = cancellationToken.Register(() => _connected?.TrySetCanceled(cancellationToken));
        await _connected.Task.ConfigureAwait(false);
    }

    public async IAsyncEnumerable<DiveComputerFrame> ReadFramesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var frames = _frames
            ?? throw new InvalidOperationException("Not connected — call ConnectAsync before reading frames.");

        while (await frames.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            while (frames.Reader.TryRead(out var frame))
            {
                yield return frame;
            }
        }
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        _frames?.Writer.TryComplete();
        _frames = null;
        _handshake.Clear();
        _writeCharacteristic = null;
        try
        {
            if (_peripheral is { } peripheral)
            {
                UnsubscribePeripheral(peripheral);
                _manager?.CancelPeripheralConnection(peripheral);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "BLE disconnect cleanup failed.");
        }

        _peripheral = null;
        _connected = null;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Lazily creates the <see cref="CBCentralManager"/> on a dedicated dispatch queue and wires the
    /// central-side connection events once. Uses the event API (no delegate subclass), so we must not
    /// pass a delegate to the constructor — a null delegate lets the events fire.
    /// </summary>
    private CBCentralManager EnsureManager()
    {
        if (_manager is not null)
        {
            return _manager;
        }

        _manager = new CBCentralManager((ICBCentralManagerDelegate?)null, new DispatchQueue("net.frostaura.trimix.ble"));
        _manager.ConnectedPeripheral += OnConnectedPeripheral;
        _manager.DisconnectedPeripheral += OnDisconnectedPeripheral;
        _manager.FailedToConnectPeripheral += OnFailedToConnect;
        return _manager;
    }

    /// <summary>
    /// Awaits <see cref="CBManagerState.PoweredOn"/>. Throws a clear
    /// <see cref="DiveComputerProtocolException"/> for Unsupported (the iOS Simulator has no radio),
    /// Unauthorized, or PoweredOff — mirroring the Android "Bluetooth is unavailable" message.
    /// </summary>
    private static async Task WaitForPoweredOnAsync(CBCentralManager manager, CancellationToken cancellationToken)
    {
        var state = manager.State;
        if (state is CBManagerState.Unknown or CBManagerState.Resetting)
        {
            var stateReady = new TaskCompletionSource<CBManagerState>(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnState(object? sender, EventArgs args)
            {
                var current = manager.State;
                if (current is not (CBManagerState.Unknown or CBManagerState.Resetting))
                {
                    stateReady.TrySetResult(current);
                }
            }

            manager.UpdatedState += OnState;
            try
            {
                // Re-read in case the transition fired before we subscribed.
                state = manager.State;
                if (state is CBManagerState.Unknown or CBManagerState.Resetting)
                {
                    using var registration = cancellationToken.Register(() => stateReady.TrySetCanceled(cancellationToken));
                    state = await stateReady.Task.ConfigureAwait(false);
                }
            }
            finally
            {
                manager.UpdatedState -= OnState;
            }
        }

        if (state == CBManagerState.PoweredOn)
        {
            return;
        }

        throw new DiveComputerProtocolException(state switch
        {
            CBManagerState.Unsupported =>
                "Bluetooth is unavailable on this device. The iOS Simulator has no Bluetooth radio — " +
                "real-hardware CoreBluetooth validation is scheduled M3 work.",
            CBManagerState.Unauthorized =>
                "Bluetooth permission has not been granted for Trimix.",
            _ => "Bluetooth is unavailable or turned off on this device.",
        });
    }

    private void OnConnectedPeripheral(object? sender, CBPeripheralEventArgs args)
    {
        logger.LogDebug("Connected to dive computer; discovering services.");

        // Discover only the profile's service(s) when the vendor advertises one; else discover all
        // (name-only vendors, whose service is located by property after discovery).
        var services = ProfileServiceUuids();
        if (services is { Length: > 0 })
        {
            args.Peripheral.DiscoverServices(services);
        }
        else
        {
            args.Peripheral.DiscoverServices();
        }
    }

    private CBUUID[]? ProfileServiceUuids()
    {
        var uuids = _profile?.AdvertisedServiceUuids;
        if (uuids is not { Count: > 0 })
        {
            return null;
        }

        return uuids.Select(CBUUID.FromString).ToArray();
    }

    private void OnDisconnectedPeripheral(object? sender, CBPeripheralErrorEventArgs args)
    {
        _connected?.TrySetException(
            new DiveComputerProtocolException("The dive computer disconnected before setup completed."));
        _frames?.Writer.TryComplete();
    }

    private void OnFailedToConnect(object? sender, CBPeripheralErrorEventArgs args)
    {
        _connected?.TrySetException(new DiveComputerProtocolException(
            $"Could not open a connection to the dive computer ({args.Error?.LocalizedDescription ?? "unknown error"})."));
        _frames?.Writer.TryComplete();
    }

    private void SubscribePeripheral(CBPeripheral peripheral)
    {
        peripheral.DiscoveredService += OnDiscoveredService;
        peripheral.DiscoveredCharacteristics += OnDiscoveredCharacteristics;
        peripheral.UpdatedCharacterteristicValue += OnUpdatedCharacteristicValue;
        peripheral.UpdatedNotificationState += OnUpdatedNotificationState;
        peripheral.WroteCharacteristicValue += OnWroteCharacteristicValue;
    }

    private void UnsubscribePeripheral(CBPeripheral peripheral)
    {
        peripheral.DiscoveredService -= OnDiscoveredService;
        peripheral.DiscoveredCharacteristics -= OnDiscoveredCharacteristics;
        peripheral.UpdatedCharacterteristicValue -= OnUpdatedCharacteristicValue;
        peripheral.UpdatedNotificationState -= OnUpdatedNotificationState;
        peripheral.WroteCharacteristicValue -= OnWroteCharacteristicValue;
    }

    private void OnDiscoveredService(object? sender, NSErrorEventArgs args)
    {
        if (sender is not CBPeripheral peripheral)
        {
            return;
        }

        var candidates = SelectCandidateServices(peripheral);
        if (candidates.Count == 0)
        {
            _connected?.TrySetException(new DiveComputerProtocolException(
                "Connected, but no dive-computer service was found. " +
                "Real-hardware protocol validation is scheduled M3 work."));
            return;
        }

        foreach (var service in candidates)
        {
            peripheral.DiscoverCharacteristics(service);
        }
    }

    /// <summary>Services matching the profile's advertised UUIDs, or all services for name-only vendors.</summary>
    private List<CBService> SelectCandidateServices(CBPeripheral peripheral)
    {
        var all = peripheral.Services?.ToList() ?? [];
        var wanted = _profile?.AdvertisedServiceUuids;
        if (wanted is not { Count: > 0 })
        {
            return all;
        }

        var wantedNormalized = new HashSet<string>(
            wanted.Select(BleGattProfiles.NormalizeUuid).Where(u => u is not null)!,
            StringComparer.OrdinalIgnoreCase);

        var matched = all
            .Where(s => s.UUID is { } uuid && BleGattProfiles.NormalizeUuid(uuid.ToString()) is { } n && wantedNormalized.Contains(n))
            .ToList();

        return matched.Count > 0 ? matched : all;
    }

    private void OnDiscoveredCharacteristics(object? sender, CBServiceEventArgs args)
    {
        if (sender is not CBPeripheral peripheral)
        {
            return;
        }

        var characteristics = args.Service.Characteristics ?? [];

        // Resolve the write characteristic from THIS service and KEEP the first match across the per-service
        // callbacks (CoreBluetooth delivers characteristics one service at a time). Doing this BEFORE the
        // notify early-return below means a split GATT — write in one service, notify in another — still
        // populates the write characteristic instead of dropping a configured download handshake.
        _writeCharacteristic = ResolveWriteCharacteristic(characteristics) ?? _writeCharacteristic;
        if (_writeCharacteristic is not null)
        {
            _writeType = _writeCharacteristic.Properties.HasFlag(CBCharacteristicProperties.Write)
                ? CBCharacteristicWriteType.WithResponse
                : CBCharacteristicWriteType.WithoutResponse;
        }

        var notifyCharacteristic = ResolveNotifyCharacteristic(characteristics);
        if (notifyCharacteristic is null)
        {
            // Another candidate service may still carry the notify characteristic; wait for its callback
            // rather than failing the whole connect here.
            return;
        }

        if (_writeCharacteristic is not null && _profile is { HandshakeCommands.Count: > 0 } profile && _handshake.Count == 0)
        {
            foreach (var command in profile.HandshakeCommands)
            {
                _handshake.Enqueue(command);
            }
        }

        // Enabling notifications is confirmed asynchronously in OnUpdatedNotificationState, which then
        // completes the connect and fires the handshake.
        peripheral.SetNotifyValue(true, notifyCharacteristic);
    }

    private CBCharacteristic? ResolveNotifyCharacteristic(CBCharacteristic[] characteristics)
    {
        var target = BleGattProfiles.NormalizeUuid(_profile?.NotifyCharacteristicUuid);
        foreach (var characteristic in characteristics)
        {
            if (target is not null)
            {
                if (characteristic.UUID is { } uuid && BleGattProfiles.NormalizeUuid(uuid.ToString()) == target)
                {
                    return characteristic;
                }

                continue;
            }

            if (characteristic.Properties.HasFlag(CBCharacteristicProperties.Notify) ||
                characteristic.Properties.HasFlag(CBCharacteristicProperties.Indicate))
            {
                return characteristic;
            }
        }

        return null;
    }

    private CBCharacteristic? ResolveWriteCharacteristic(CBCharacteristic[] characteristics)
    {
        var target = BleGattProfiles.NormalizeUuid(_profile?.WriteCharacteristicUuid);
        foreach (var characteristic in characteristics)
        {
            if (target is not null)
            {
                if (characteristic.UUID is { } uuid && BleGattProfiles.NormalizeUuid(uuid.ToString()) == target)
                {
                    return characteristic;
                }

                continue;
            }

            if (characteristic.Properties.HasFlag(CBCharacteristicProperties.Write) ||
                characteristic.Properties.HasFlag(CBCharacteristicProperties.WriteWithoutResponse))
            {
                return characteristic;
            }
        }

        return null;
    }

    private void OnUpdatedNotificationState(object? sender, CBCharacteristicEventArgs args)
    {
        if (args.Error is { } error)
        {
            logger.LogDebug("Enabling notifications failed: {Error}", error.LocalizedDescription);
            return;
        }

        // Notifications are on: the connection is usable. Complete the connect, then start the (possibly
        // empty) download handshake.
        _connected?.TrySetResult(true);
        WriteNextHandshakeCommand();
    }

    private void OnWroteCharacteristicValue(object? sender, CBCharacteristicEventArgs args) =>
        WriteNextHandshakeCommand();

    private void WriteNextHandshakeCommand()
    {
        if (_peripheral is not { } peripheral || _writeCharacteristic is not { } characteristic || _handshake.Count == 0)
        {
            return;
        }

        var command = _handshake.Dequeue();
        try
        {
            using var data = NSData.FromArray(command);
            peripheral.WriteValue(data, characteristic, _writeType);

            // WithoutResponse writes raise no WroteCharacteristicValue callback, so pump the next command
            // immediately; WithResponse writes chain through OnWroteCharacteristicValue.
            if (_writeType == CBCharacteristicWriteType.WithoutResponse)
            {
                WriteNextHandshakeCommand();
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Writing a BLE download-handshake command failed.");
        }
    }

    private void OnUpdatedCharacteristicValue(object? sender, CBCharacteristicEventArgs args)
    {
        var payload = args.Characteristic.Value?.ToArray();
        if (payload is { Length: > 0 })
        {
            _frames?.Writer.TryWrite(new DiveComputerFrame(payload));
        }
    }
}
