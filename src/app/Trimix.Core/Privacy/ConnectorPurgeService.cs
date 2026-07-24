using Microsoft.Extensions.Logging;
using Trimix.Core.Abstractions;

namespace Trimix.Core.Privacy;

/// <summary>
/// Privacy purge for connector state on sign-out AND GDPR delete (aggregator architecture §9.4). Closes
/// the real gap that <c>SyncService</c> does not cover: signing out already wipes the local dive store +
/// sync watermark, but a diver who connected a reverse-engineered vendor account (Garmin / Suunto /
/// Oceanic / PADI / SSI) left behind two things that survive sign-out — and, on the iOS Keychain, even
/// survive app uninstall:
/// <list type="bullet">
///   <item>the per-connector <b>consent flag</b> in <see cref="IConnectorConsentStore"/>, and</item>
///   <item>the <b>exchanged access token</b> in <see cref="ISecureTokenStore"/> (secure enclave).</item>
/// </list>
/// Left in place, the next account signed in on the device silently inherits a live vendor link. This
/// service revokes both for <em>every</em> registered connector whenever the session drops to
/// unauthenticated.
///
/// <para>It mirrors <c>SyncService</c>'s existing pattern exactly: subscribe to
/// <see cref="IAuthSession.AuthStateChanged"/> in the constructor and act on the sign-out edge. Because
/// the GDPR-delete flow (<c>SettingsViewModel.DeleteMyDataAsync</c>) ends in
/// <see cref="IAuthSession.SignOutAsync"/>, the same subscription covers delete for free. A public
/// <see cref="PurgeAllAsync"/> is exposed for defense-in-depth / explicit invocation and testability.</para>
///
/// <para>Delegation keeps the token-key logic DRY: <see cref="IConnectorConsentStore.RevokeAsync"/> already
/// deletes the exchanged token under <em>every</em> key shape (via <c>ConnectorTokenKeys.AllFor</c>), so a
/// single <c>RevokeAsync(id)</c> severs both consent and token for that connector — this service never
/// re-derives secure-store keys. Per-connector failures are swallowed + logged so one connector's error
/// cannot abort the rest of the purge; the operation is idempotent (revoking absent state is a no-op).</para>
///
/// <para><b>Startup requirement:</b> like <c>SyncService</c>, the constructor is where the subscription is
/// wired, so the integrator must resolve this singleton once at startup (see §9.4 reported wiring) — an
/// unresolved singleton never subscribes.</para>
///
/// <para><b>Identity-change coverage:</b> beyond the sign-out edge, the constructor also subscribes to
/// <see cref="IAuthSession.IdentityChanged"/> so a shared-device handoff (account→different-account WITHOUT an
/// intervening sign-out) purges the previous identity's connector state too. The guest→account upgrade never
/// raises that event (single-user continuity), so connected vendors carry over as intended. The public
/// <see cref="PurgeAllAsync"/> is additionally AWAITED by the sign-out/GDPR-delete flow and by the
/// unauthenticated-startup reconciliation — the event subscriptions here are the automatic backstop.</para>
/// </summary>
public sealed class ConnectorPurgeService : IConnectorPurgeService
{
    private readonly IReadOnlyList<string> _connectorIds;
    private readonly IConnectorConsentStore _consent;
    private readonly ILogger<ConnectorPurgeService> _logger;

    public ConnectorPurgeService(
        IAuthSession session,
        IEnumerable<IDiveSourceConnector> sources,
        IEnumerable<IDiveDestinationConnector> destinations,
        IConnectorConsentStore consent,
        ILogger<ConnectorPurgeService> logger)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(destinations);
        _consent = consent;
        _logger = logger;

        // Snapshot the connector id set once (it is fixed at DI time). Sources + destinations both hold
        // consent/token state; a connector that appears in both (none today) is de-duplicated.
        _connectorIds =
        [
            .. sources.Select(c => c.Descriptor.ConnectorId)
                .Concat(destinations.Select(c => c.Descriptor.ConnectorId))
                .Distinct(StringComparer.Ordinal),
        ];

        // On sign-out / GDPR delete, revoke every connector's consent + exchanged token so no vendor link
        // leaks into the next account signed in on this device. (Automatic backstop; the sign-out/delete flow
        // additionally AWAITS PurgeAllAsync so completion is guaranteed and any failure is retried at startup.)
        session.AuthStateChanged += (_, isAuthenticated) =>
        {
            if (!isAuthenticated)
            {
                _ = PurgeAllAsync();
            }
        };

        // On a genuine identity change (account→different-account without an intervening sign-out — a shared-
        // device handoff), purge the previous identity's device-global connector state. Never raised on the
        // guest→account upgrade, so single-user continuity is preserved.
        session.IdentityChanged += (_, _) => _ = PurgeAllAsync();
    }

    /// <summary>
    /// Revokes consent + deletes the exchanged token for every registered connector. Idempotent;
    /// per-connector exceptions are logged and swallowed so a single failure never aborts the sweep.
    /// </summary>
    public async Task PurgeAllAsync(CancellationToken cancellationToken = default)
    {
        foreach (var connectorId in _connectorIds)
        {
            try
            {
                // RevokeAsync clears the consent flag AND the exchanged token under every key shape
                // (ConnectorTokenKeys.AllFor) — the single source of truth for connector erasure.
                await _consent.RevokeAsync(connectorId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    ex, "Failed to purge connector consent/token for '{ConnectorId}' on sign-out/delete.", connectorId);
            }
        }
    }
}
