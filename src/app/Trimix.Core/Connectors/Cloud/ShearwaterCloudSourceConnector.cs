using System.Collections.Generic;
using Trimix.Core.Abstractions;
using Trimix.Core.Connectors.Infra;

namespace Trimix.Core.Connectors.Cloud;

/// <summary>
/// Shearwater — reserved as a <b>"Coming soon"</b> provider. Shearwater's own sync product (Shearwater Cloud)
/// is a desktop application that pulls dives off the computer over Bluetooth; there is <b>no phone-reachable
/// personal-export cloud</b> to read today, so no corridor has been captured and none is guessed. This
/// connector exists only so Shearwater appears in the Providers list as a disabled "Coming soon" row — it is
/// deliberately NOT in <c>ProviderCatalog.LiveConnectorIds</c>, so the row is never connectable and none of
/// the methods below are reached. It still ships dark (<c>ToSRiskReverseEng</c> + <c>EnabledByDefault=false</c>)
/// so the consent + dark-descriptor guardrails apply structurally if/when a real corridor is added.
/// </summary>
public sealed class ShearwaterCloudSourceConnector(
    IHttpTransport http,
    ISecureTokenStore tokens,
    IConnectorConsentStore consent) : GatedCloudSourceConnector(http, tokens, consent)
{
    public const string Id = "shearwater";

    public override ConnectorDescriptor Descriptor { get; } = new(
        ConnectorId: Id,
        Kind: ConnectorKind.CloudApi,
        Legal: LegalPosture.ToSRiskReverseEng,
        Fidelity: ProfileFidelity.FullProfile,
        Auth: AuthModel.UserCredential,
        Platforms: Platforms.Both,
        EnabledByDefault: false);

    public override string DisplayName => "Shearwater Cloud (personal export)";

    protected override string Disclosure =>
        "Shearwater import is coming soon. Shearwater Cloud currently transfers your dives over Bluetooth to a " +
        "desktop app, with no phone-reachable cloud to read yet — so there is nothing to connect for now.";

    // Coming soon: no corridor exists. These are unreachable (the row isn't connectable) and stay inert until
    // a real Shearwater personal-export path is captured.
    protected override Task<string> ExchangeCredentialsForTokenAsync(ConnectorCredentials credentials, CancellationToken ct) =>
        throw new ConnectorAuthException("Shearwater sync is coming soon.");

    public override Task<IReadOnlyList<DiveRef>> ListDivesSinceAsync(SyncCursor cursor, CancellationToken ct = default) =>
        throw new NotSupportedException("Shearwater sync is coming soon.");

    public override Task<ImportedDive> FetchProfileAsync(DiveRef diveRef, CancellationToken ct = default) =>
        throw new NotSupportedException("Shearwater sync is coming soon.");
}
