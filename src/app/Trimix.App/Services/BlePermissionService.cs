#if IOS
using CoreBluetooth;
using CoreFoundation;
#endif
using Trimix.Core.Abstractions;

namespace Trimix.App.Services;

/// <summary>
/// Just-in-time BLE runtime permission (PRD Flow B step 1, privacy posture: request only at the
/// moment of use, preceded by an in-app rationale). Android 12+: BLUETOOTH_SCAN (neverForLocation)
/// + BLUETOOTH_CONNECT; Android ≤11: ACCESS_FINE_LOCATION (the legacy BLE-scan requirement) — see
/// <c>Platforms/Android/BluetoothPermissions.cs</c>.
///
/// FUNNEL NOTE: the frozen <c>ble_permission_requested / granted / denied</c> events are emitted
/// by Core's ImportViewModel AROUND this call (the binding contract's single emission point).
/// This service must NOT track them too — that would double-count the funnel.
/// </summary>
public sealed class BlePermissionService(IUserDialogs dialogs) : Trimix.Core.Abstractions.IPermissions
{
    public const string RationaleTitle = "Bluetooth permission";

    public const string RationaleMessage =
        "Trimix uses Bluetooth only while you import, to find your dive computer and read its " +
        "dive log. It never scans in the background and never uses Bluetooth to infer your location.";

    public async Task<bool> RequestBluetoothAsync(CancellationToken cancellationToken = default)
    {
#if ANDROID
        var status = await MainThread.InvokeOnMainThreadAsync(
            Microsoft.Maui.ApplicationModel.Permissions.CheckStatusAsync<BluetoothPermissions>);
        if (status == PermissionStatus.Granted)
        {
            return true;
        }

        // Just-in-time rationale before the OS prompt (always on first ask is fine; the OS flag
        // additionally forces it after a prior denial).
        await dialogs.AlertAsync(RationaleTitle, RationaleMessage, "Continue");

        status = await MainThread.InvokeOnMainThreadAsync(
            Microsoft.Maui.ApplicationModel.Permissions.RequestAsync<BluetoothPermissions>);
        return status == PermissionStatus.Granted;
#elif IOS
        // iOS: CoreBluetooth owns the permission gate. Authorization is app-wide (CBManager.Authorization)
        // and the OS raises its own system prompt the first time a CBCentralManager powers up
        // (NSBluetoothAlwaysUsageDescription, declared in Info.plist).
        if (CBManager.Authorization == CBManagerAuthorization.AllowedAlways)
        {
            return true;
        }

        // Already decided against us — the OS will not re-prompt; nothing an in-app rationale can do.
        if (CBManager.Authorization is CBManagerAuthorization.Restricted or CBManagerAuthorization.Denied)
        {
            return false;
        }

        // NotDetermined: show the just-in-time rationale, then bring up a CBCentralManager to trigger the
        // system prompt and await the authorization decision via its state callback.
        await dialogs.AlertAsync(RationaleTitle, RationaleMessage, "Continue");

        var decided = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        CBCentralManager? manager = null;
        void OnState(object? sender, EventArgs args)
        {
            // Wait until the user has actually answered the prompt (state leaves Unknown).
            if (CBManager.Authorization != CBManagerAuthorization.NotDetermined)
            {
                decided.TrySetResult(CBManager.Authorization == CBManagerAuthorization.AllowedAlways);
            }
        }

        manager = new CBCentralManager((ICBCentralManagerDelegate?)null, DispatchQueue.MainQueue);
        manager.UpdatedState += OnState;
        try
        {
            // In case authorization resolved before the handler was attached.
            OnState(manager, EventArgs.Empty);
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
            using var registration = timeout.Token.Register(() => decided.TrySetResult(false));
            return await decided.Task.ConfigureAwait(false);
        }
        finally
        {
            manager.UpdatedState -= OnState;
        }
#else
        // Other platforms: CoreBluetooth/Android permission gates do not apply here.
        await dialogs.AlertAsync(RationaleTitle, RationaleMessage, "Continue");
        return true;
#endif
    }
}
