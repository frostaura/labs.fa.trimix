namespace Trimix.Core.Abstractions;

/// <summary>
/// Just-in-time runtime permissions (PRD privacy posture: request only at the moment of use,
/// always preceded by an in-app rationale). Implemented by the App layer per platform.
/// </summary>
public interface IPermissions
{
    /// <summary>Requests BLE scan/connect permission. Returns true when granted.</summary>
    Task<bool> RequestBluetoothAsync(CancellationToken cancellationToken = default);
}
