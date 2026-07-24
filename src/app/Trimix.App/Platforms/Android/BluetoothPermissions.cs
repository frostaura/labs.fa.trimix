namespace Trimix.App;

/// <summary>
/// Runtime permission set for BLE dive-computer import (requested just-in-time by
/// <see cref="Services.BlePermissionService"/>):
/// API 31+ → BLUETOOTH_SCAN (declared neverForLocation in the manifest) + BLUETOOTH_CONNECT;
/// API ≤ 30 → ACCESS_FINE_LOCATION, the OS-mandated legacy BLE-scan permission.
/// </summary>
public sealed class BluetoothPermissions : Permissions.BasePlatformPermission
{
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
        OperatingSystem.IsAndroidVersionAtLeast(31)
            ?
            [
                (Android.Manifest.Permission.BluetoothScan, true),
                (Android.Manifest.Permission.BluetoothConnect, true),
            ]
            :
            [
                (Android.Manifest.Permission.AccessFineLocation, true),
            ];
}
