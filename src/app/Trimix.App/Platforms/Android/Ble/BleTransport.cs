using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Android.Bluetooth;
using Android.Bluetooth.LE;
using Microsoft.Extensions.Logging;
using Trimix.Core.Abstractions;
using Trimix.Core.DiveComputer;

namespace Trimix.App.Ble;

/// <summary>
/// Android BLE <see cref="IDiveComputerTransport"/> (architecture overview §4, docs §9.3): the real
/// production transport, registered only behind the DI flag (<c>UseSimulatorTransport == false</c>, i.e.
/// Release) — SimulatorTransport stays the Debug default. Compiled always.
///
/// <para><b>Pipeline.</b> Scan → identify the device's <see cref="DiveComputerVendor"/> from its advertised
/// service UUIDs + name via <see cref="BleGattProfiles"/> → connect → discover services → resolve the write
/// and notify characteristics (explicit profile UUID if supplied, else by GATT property — first
/// <c>Write</c>/<c>WriteNoResponse</c> and first <c>Notify</c>/<c>Indicate</c>, exactly as Subsurface's
/// <c>qt-ble.cpp</c>) → enable notifications (CCCD) → write the profile's download handshake to the write
/// characteristic → surface notification bytes as <see cref="DiveComputerFrame"/>s into the vendor parser.</para>
///
/// <para><b>Honest status (M3 kill-line).</b> Only the Shearwater <b>service</b> UUID is a pinned public fact
/// (libdivecomputer / Subsurface). Characteristic UUIDs resolve by property; the real per-vendor download
/// handshake bytes and any undocumented characteristic UUIDs are DEPLOY INPUTS supplied via
/// <see cref="BleGattOverrides"/> — never guessed. With the default (empty) handshake this behaves like the
/// prior skeleton: enable notify, stream whatever the device pushes. Field validation against real hardware
/// is the M3 gate. Callers must hold the BLE runtime permission before use (BlePermissionService).</para>
/// </summary>
public sealed class BleTransport(ILogger<BleTransport> logger) : IDiveComputerTransport
{
    /// <summary>Standard Client Characteristic Configuration descriptor (enables notifications).</summary>
    private static readonly Java.Util.UUID ClientCharacteristicConfigUuid =
        Java.Util.UUID.FromString("00002902-0000-1000-8000-00805f9b34fb")!;

    private readonly Dictionary<string, BluetoothDevice> _discovered = new(StringComparer.Ordinal);
    private BluetoothGatt? _gatt;
    private Channel<DiveComputerFrame>? _frames;
    private TaskCompletionSource<bool>? _connected;

    // Per-session connect state, driven by the vendor profile (reset each ConnectAsync).
    private VendorGattProfile? _profile;
    private BluetoothGattCharacteristic? _writeCharacteristic;
    private GattWriteType _writeType = GattWriteType.Default;
    private readonly Queue<byte[]> _handshake = new();

    private static BluetoothAdapter? Adapter =>
        (Platform.AppContext.GetSystemService(Android.Content.Context.BluetoothService)
            as BluetoothManager)?.Adapter;

    public async IAsyncEnumerable<DiscoveredComputer> ScanAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var scanner = Adapter?.BluetoothLeScanner
            ?? throw new DiveComputerProtocolException("Bluetooth is unavailable or turned off on this device.");

        var channel = Channel.CreateUnbounded<DiscoveredComputer>();
        var callback = new RelayScanCallback(result =>
        {
            var device = result?.Device;
            var name = device?.Name;
            if (device?.Address is not { } address || string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            // Advertised 128-bit service UUIDs (if any) drive the strong identification signal; name is the fallback.
            var advertisedServiceUuids = result?.ScanRecord?.ServiceUuids?
                .Select(u => u.ToString())
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .ToList();

            if (BleGattProfiles.TryIdentify(advertisedServiceUuids, name) is not { } vendor)
            {
                return; // Not a known dive computer family — ignore.
            }

            lock (_discovered)
            {
                if (!_discovered.TryAdd(address, device))
                {
                    return; // Already reported this device during this scan.
                }
            }

            channel.Writer.TryWrite(new DiscoveredComputer(vendor, name, address));
        });

        scanner.StartScan(callback);
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
            try
            {
                scanner.StopScan(callback);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "StopScan after BLE scan ended failed (adapter off?).");
            }
        }
    }

    public async Task ConnectAsync(DiscoveredComputer computer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(computer);

        BluetoothDevice? device;
        lock (_discovered)
        {
            _discovered.TryGetValue(computer.Id, out device);
        }

        device ??= Adapter?.GetRemoteDevice(computer.Id)
            ?? throw new DiveComputerProtocolException($"Dive computer '{computer.Model}' is no longer reachable.");

        _profile = BleGattProfiles.For(computer.Vendor);
        _writeCharacteristic = null;
        _writeType = GattWriteType.Default;
        _handshake.Clear();
        _frames = Channel.CreateUnbounded<DiveComputerFrame>();
        _connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var callback = new RelayGattCallback(
            onConnectionStateChange: (gatt, status, newState) =>
            {
                if (newState == ProfileState.Connected)
                {
                    gatt?.DiscoverServices();
                }
                else if (newState == ProfileState.Disconnected)
                {
                    _connected?.TrySetException(
                        new DiveComputerProtocolException("The dive computer disconnected before setup completed."));
                    _frames?.Writer.TryComplete();
                }
            },
            onServicesDiscovered: OnServicesDiscovered,
            onDescriptorWrite: (_, _) => CompleteSetupAndStartHandshake(),
            onCharacteristicWrite: (_, _) => WriteNextHandshakeCommand(),
            onCharacteristicChanged: payload =>
            {
                if (payload is { Length: > 0 })
                {
                    _frames?.Writer.TryWrite(new DiveComputerFrame(payload));
                }
            });

        _gatt = device.ConnectGatt(Platform.AppContext, autoConnect: false, callback)
            ?? throw new DiveComputerProtocolException($"Could not open a connection to '{computer.Model}'.");

        using var registration = cancellationToken.Register(() => _connected?.TrySetCanceled(cancellationToken));
        await _connected.Task.ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves the vendor service + write/notify characteristics, enables notifications, and (once the
    /// CCCD write lands) fires the download handshake. Service selection: profile service UUIDs if the
    /// vendor advertises one, else all discovered services (name-only vendors). Characteristic selection:
    /// explicit profile UUID, else by GATT property.
    /// </summary>
    private void OnServicesDiscovered(BluetoothGatt? gatt, GattStatus status)
    {
        if (gatt is null)
        {
            _connected?.TrySetException(new DiveComputerProtocolException("Service discovery returned no GATT handle."));
            return;
        }

        var candidateServices = SelectCandidateServices(gatt);
        var notifyCharacteristic = ResolveNotifyCharacteristic(candidateServices);
        if (notifyCharacteristic is null)
        {
            _connected?.TrySetException(new DiveComputerProtocolException(
                "Connected, but no notify characteristic was found on the dive-computer service. " +
                "Real-hardware protocol validation is scheduled M3 work."));
            return;
        }

        _writeCharacteristic = ResolveWriteCharacteristic(candidateServices);
        if (_writeCharacteristic is not null)
        {
            _writeType = _writeCharacteristic.Properties.HasFlag(GattProperty.Write)
                ? GattWriteType.Default          // write-with-response (acknowledged)
                : GattWriteType.NoResponse;
        }

        // Queue the (possibly-empty) handshake now; it is written after notifications are confirmed on.
        if (_writeCharacteristic is not null && _profile is { HandshakeCommands.Count: > 0 } profile)
        {
            foreach (var command in profile.HandshakeCommands)
            {
                _handshake.Enqueue(command);
            }
        }

        gatt.SetCharacteristicNotification(notifyCharacteristic, true);
        var descriptor = notifyCharacteristic.GetDescriptor(ClientCharacteristicConfigUuid);
        // The CCCD payload MUST match the characteristic's property: an Indicate-only characteristic needs
        // ENABLE_INDICATION_VALUE (0x0200), a Notify one needs ENABLE_NOTIFICATION_VALUE (0x0100). Writing the
        // notification value to an indicate-only characteristic leaves indications disabled at the peripheral,
        // so no frames ever arrive. Prefer Notify when the characteristic supports both (matches qt-ble.cpp).
        var cccdValue = notifyCharacteristic.Properties.HasFlag(GattProperty.Notify)
            ? BluetoothGattDescriptor.EnableNotificationValue
            : notifyCharacteristic.Properties.HasFlag(GattProperty.Indicate)
                ? BluetoothGattDescriptor.EnableIndicationValue
                : BluetoothGattDescriptor.EnableNotificationValue;
        if (descriptor is not null && cccdValue is { } enable)
        {
            // OnDescriptorWrite → CompleteSetupAndStartHandshake() once the peripheral acknowledges the CCCD.
            if (OperatingSystem.IsAndroidVersionAtLeast(33))
            {
                gatt.WriteDescriptor(descriptor, [.. enable]);
            }
            else
            {
#pragma warning disable CA1422 // Legacy descriptor write path for API < 33.
                descriptor.SetValue([.. enable]);
                gatt.WriteDescriptor(descriptor);
#pragma warning restore CA1422
            }
        }
        else
        {
            // No CCCD to write (or platform lacks the enable value): local notification enable is all we
            // can do — proceed straight to handshake/ready.
            CompleteSetupAndStartHandshake();
        }
    }

    /// <summary>Services matching the profile's advertised UUIDs, or all services for name-only vendors.</summary>
    private List<BluetoothGattService> SelectCandidateServices(BluetoothGatt gatt)
    {
        var all = gatt.Services?.ToList() ?? [];
        var wanted = _profile?.AdvertisedServiceUuids;
        if (wanted is not { Count: > 0 })
        {
            return all;
        }

        var wantedNormalized = new HashSet<string>(
            wanted.Select(BleGattProfiles.NormalizeUuid).Where(u => u is not null)!,
            StringComparer.OrdinalIgnoreCase);

        var matched = all
            .Where(s => s.Uuid is { } uuid && BleGattProfiles.NormalizeUuid(uuid.ToString()) is { } n && wantedNormalized.Contains(n))
            .ToList();

        // Fall back to all services if the advertised UUID isn't exposed as a primary service handle.
        return matched.Count > 0 ? matched : all;
    }

    private BluetoothGattCharacteristic? ResolveNotifyCharacteristic(List<BluetoothGattService> services)
    {
        var target = BleGattProfiles.NormalizeUuid(_profile?.NotifyCharacteristicUuid);
        foreach (var service in services)
        {
            foreach (var characteristic in service.Characteristics ?? [])
            {
                if (target is not null)
                {
                    if (characteristic.Uuid is { } uuid && BleGattProfiles.NormalizeUuid(uuid.ToString()) == target)
                    {
                        return characteristic;
                    }

                    continue;
                }

                if (characteristic.Properties.HasFlag(GattProperty.Notify) ||
                    characteristic.Properties.HasFlag(GattProperty.Indicate))
                {
                    return characteristic;
                }
            }
        }

        return null;
    }

    private BluetoothGattCharacteristic? ResolveWriteCharacteristic(List<BluetoothGattService> services)
    {
        var target = BleGattProfiles.NormalizeUuid(_profile?.WriteCharacteristicUuid);
        foreach (var service in services)
        {
            foreach (var characteristic in service.Characteristics ?? [])
            {
                if (target is not null)
                {
                    if (characteristic.Uuid is { } uuid && BleGattProfiles.NormalizeUuid(uuid.ToString()) == target)
                    {
                        return characteristic;
                    }

                    continue;
                }

                if (characteristic.Properties.HasFlag(GattProperty.Write) ||
                    characteristic.Properties.HasFlag(GattProperty.WriteNoResponse))
                {
                    return characteristic;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Notifications are enabled: the connection is usable, so complete <see cref="ConnectAsync"/>, then
    /// kick off the download handshake (no-op when the handshake is empty — the default until a validated
    /// per-vendor sequence is supplied via <see cref="BleGattOverrides"/>).
    /// </summary>
    private void CompleteSetupAndStartHandshake()
    {
        _connected?.TrySetResult(true);
        WriteNextHandshakeCommand();
    }

    private void WriteNextHandshakeCommand()
    {
        if (_gatt is not { } gatt || _writeCharacteristic is not { } characteristic || _handshake.Count == 0)
        {
            return;
        }

        var command = _handshake.Dequeue();
        try
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(33))
            {
                gatt.WriteCharacteristic(characteristic, command, (int)_writeType);
            }
            else
            {
#pragma warning disable CA1422 // Legacy characteristic write path for API < 33.
                characteristic.WriteType = _writeType;
                characteristic.SetValue(command);
                gatt.WriteCharacteristic(characteristic);
#pragma warning restore CA1422
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Writing a BLE download-handshake command failed.");
        }
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
            _gatt?.Disconnect();
            _gatt?.Close();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "BLE disconnect cleanup failed.");
        }

        _gatt = null;
        _connected = null;
        return Task.CompletedTask;
    }

    private sealed class RelayScanCallback(Action<ScanResult?> onResult) : ScanCallback
    {
        public override void OnScanResult(ScanCallbackType callbackType, ScanResult? result) =>
            onResult(result);

        public override void OnBatchScanResults(IList<ScanResult>? results)
        {
            foreach (var result in results ?? [])
            {
                onResult(result);
            }
        }
    }

    private sealed class RelayGattCallback(
        Action<BluetoothGatt?, GattStatus, ProfileState> onConnectionStateChange,
        Action<BluetoothGatt?, GattStatus> onServicesDiscovered,
        Action<BluetoothGatt?, GattStatus> onDescriptorWrite,
        Action<BluetoothGatt?, GattStatus> onCharacteristicWrite,
        Action<byte[]?> onCharacteristicChanged) : BluetoothGattCallback
    {
        public override void OnConnectionStateChange(BluetoothGatt? gatt, GattStatus status, ProfileState newState) =>
            onConnectionStateChange(gatt, status, newState);

        public override void OnServicesDiscovered(BluetoothGatt? gatt, GattStatus status) =>
            onServicesDiscovered(gatt, status);

        public override void OnDescriptorWrite(BluetoothGatt? gatt, BluetoothGattDescriptor? descriptor, GattStatus status) =>
            onDescriptorWrite(gatt, status);

        public override void OnCharacteristicWrite(BluetoothGatt? gatt, BluetoothGattCharacteristic? characteristic, GattStatus status) =>
            onCharacteristicWrite(gatt, status);

        // API 33+ overload (value delivered directly).
        public override void OnCharacteristicChanged(
            BluetoothGatt gatt, BluetoothGattCharacteristic characteristic, byte[] value) =>
            onCharacteristicChanged(value);

        // Legacy overload (API < 33).
        public override void OnCharacteristicChanged(BluetoothGatt? gatt, BluetoothGattCharacteristic? characteristic) =>
#pragma warning disable CA1422 // Legacy value read path for API < 33.
            onCharacteristicChanged(characteristic?.GetValue());
#pragma warning restore CA1422
    }
}
