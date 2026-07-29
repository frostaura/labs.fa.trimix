using Microsoft.Extensions.Logging;
using Trimix.Core.Abstractions;

namespace Trimix.Core.Privacy;

/// <summary>
/// Privacy purge for connector state (aggregator architecture §9.4). A diver who connected a
/// reverse-engineered vendor account (Garmin / Suunto / Oceanic / PADI / SSI) leaves behind two things
/// that survive a local data wipe — and, on the iOS Keychain, even survive app uninstall:
/// <list type="bullet">
///   <item>the per-connector <b>consent flag</b> in <see cref="IConnectorConsentStore"/>, and</item>
///   <item>the <b>exchanged access token</b> in <see cref="ISecureTokenStore"/> (secure enclave).</item>
/// </list>
/// This service revokes both for <em>every</em> registered connector. Trimix has no accounts, so there is
/// no sign-out / identity-change edge to react to: erasure is explicit only, driven by the Settings
/// "Delete my data" and "Reset app" flows, which AWAIT <see cref="PurgeAllAsync"/>.
///
/// <para>Delegation keeps the token-key logic DRY: <see cref="IConnectorConsentStore.RevokeAsync"/> already
/// deletes the exchanged token under <em>every</em> key shape (via <c>ConnectorTokenKeys.AllFor</c>), so a
/// single <c>RevokeAsync(id)</c> severs both consent and token for that connector — this service never
/// re-derives secure-store keys. Per-connector failures are swallowed + logged so one connector's error
/// cannot abort the rest of the purge; the operation is idempotent (revoking absent state is a no-op).</para>
/// </summary>
public sealed class ConnectorPurgeService : IConnectorPurgeService
{
    private readonly IReadOnlyList<string> _connectorIds;
    private readonly IConnectorConsentStore _consent;
    private readonly ILogger<ConnectorPurgeService> _logger;

    public ConnectorPurgeService(
        IEnumerable<IDiveSourceConnector> sources,
        IEnumerable<IDiveDestinationConnector> destinations,
        IConnectorConsentStore consent,
        ILogger<ConnectorPurgeService> logger)
    {
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
