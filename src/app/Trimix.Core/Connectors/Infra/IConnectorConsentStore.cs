namespace Trimix.Core.Abstractions;

/// <summary>
/// Per-connector opt-in ledger (aggregator infra). <b>Guardrail:</b> any connector whose
/// <see cref="LegalPosture.ToSRiskReverseEng"/> descriptor ships dark MUST return
/// <c>ConnectorAuthResult(NeedsInteraction, "&lt;disclosure&gt;")</c> from <c>AuthenticateAsync</c> and perform
/// <em>zero</em> network I/O until <see cref="HasConsent"/> is true for its id. Consent is recorded only after
/// the user reads the plain-language ToS-risk disclosure and explicitly opts in. Revocation clears it.
/// The default <c>PreferencesConnectorConsentStore</c> persists to <see cref="IPreferences"/>.
/// </summary>
public interface IConnectorConsentStore
{
    bool HasConsent(string connectorId);

    Task GrantAsync(string connectorId, CancellationToken ct = default);

    Task RevokeAsync(string connectorId, CancellationToken ct = default);
}
