namespace Trimix.Core.Privacy;

/// <summary>
/// Revokes every registered connector's consent flag + exchanged token (the device-global vendor link that
/// survives sign-out and, on the iOS Keychain, even app uninstall). Injected where the sign-out / GDPR-delete
/// flow and the unauthenticated-startup reconciliation must AWAIT the purge to completion rather than leave it
/// to a fire-and-forget event continuation that an app teardown could interrupt.
/// </summary>
public interface IConnectorPurgeService
{
    /// <summary>
    /// Revokes consent + deletes the exchanged token for every registered connector. Idempotent;
    /// per-connector exceptions are logged and swallowed so a single failure never aborts the sweep.
    /// </summary>
    Task PurgeAllAsync(CancellationToken cancellationToken = default);
}
