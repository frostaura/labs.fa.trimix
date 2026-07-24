using Trimix.Core.Abstractions;

namespace Trimix.Core.Connectors.Infra;

/// <summary>
/// Default <see cref="IConnectorConsentStore"/> backed by <see cref="IPreferences"/> (platform Preferences in
/// the app, in-memory in dev/tests). Consent is a non-secret boolean flag, so Preferences — not the secure
/// store — is the right home; it is cleared on uninstall, which is the correct "fresh install re-asks" semantic.
///
/// <para><b>Guardrail:</b> revoke ("disconnect") is only truthful if it also severs the vendor link. The
/// disclosures every reverse-eng connector ships ("You can disconnect at any time"; "only the token is kept")
/// promise exactly that, so <see cref="RevokeAsync"/> additionally deletes the connector's EXCHANGED access
/// token from <see cref="ISecureTokenStore"/> (both the cloud-source and push key shapes — the store is keyed
/// only by connector id and cannot know the connector family). Without this, a live token to the user's
/// Garmin/Suunto/Oceanic/SSI account survives disconnect (and, on iOS, app uninstall) and silently resurrects
/// a Granted auth on reconnect.</para>
/// </summary>
public sealed class PreferencesConnectorConsentStore(IPreferences preferences, ISecureTokenStore tokens)
    : IConnectorConsentStore
{
    private static string Key(string connectorId) => $"connector-consent:{connectorId}";

    public bool HasConsent(string connectorId) => preferences.Get(Key(connectorId)) == "1";

    public Task GrantAsync(string connectorId, CancellationToken ct = default)
    {
        preferences.Set(Key(connectorId), "1");
        return Task.CompletedTask;
    }

    public async Task RevokeAsync(string connectorId, CancellationToken ct = default)
    {
        preferences.Remove(Key(connectorId));
        // Also clear the exchanged token so the vendor link is actually severed (see class remarks).
        foreach (var tokenKey in ConnectorTokenKeys.AllFor(connectorId))
        {
            await tokens.RemoveAsync(tokenKey, ct).ConfigureAwait(false);
        }
    }
}
