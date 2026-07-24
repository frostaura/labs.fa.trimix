namespace Trimix.Core.Connectors.Infra;

/// <summary>
/// Single source of truth for the <see cref="Trimix.Core.Abstractions.ISecureTokenStore"/> keys under which a
/// connector's <em>exchanged</em> access token is stored. Centralised so the disconnect/revoke path
/// (<see cref="PreferencesConnectorConsentStore.RevokeAsync"/>) clears exactly the keys the connector bases
/// (<c>GatedCloudSourceConnector</c>, <c>GatedPushConnectorBase</c>) write — the two must never drift, or
/// "disconnect" silently leaves a live vendor credential-derived token in the secure enclave.
/// </summary>
public static class ConnectorTokenKeys
{
    /// <summary>Key used by the reverse-eng cloud-<em>source</em> connectors (Garmin/Suunto/Oceanic pull).</summary>
    public static string CloudSourceToken(string connectorId) => $"connector:{connectorId}:access_token";

    /// <summary>Key used by the gated cert-agency <em>push</em> connectors (PADI/SSI).</summary>
    public static string PushToken(string connectorId) => connectorId + ".token";

    /// <summary>Every secure-store key a connector of this id could hold a token under (for revoke/erasure).</summary>
    public static IEnumerable<string> AllFor(string connectorId)
    {
        yield return CloudSourceToken(connectorId);
        yield return PushToken(connectorId);
    }
}
