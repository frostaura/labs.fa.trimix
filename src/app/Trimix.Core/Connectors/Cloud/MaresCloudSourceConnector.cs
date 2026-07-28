using System.Collections.Generic;
using Trimix.Core.Abstractions;
using Trimix.Core.Connectors.Infra;

namespace Trimix.Core.Connectors.Cloud;

/// <summary>
/// Reverse-engineered Mares personal-export source. Mares publishes <b>no cloud API of its own</b>; the
/// researched corridor is Head/SSI — <b>MySSI reads Mares computers directly</b> — so this connector
/// authenticates with the diver's MySSI (SSI) credentials and pulls their own dives through the shared
/// <see cref="MySsiCloudClient"/>, filtered to dives whose source computer is a Mares. This is
/// <see cref="LegalPosture.ToSRiskReverseEng"/> and carries <c>EnabledByDefault=false</c> (the structural
/// dark-descriptor invariant); visibility comes from registration + the per-user consent gate, never from
/// the descriptor flag.
/// </summary>
public sealed class MaresCloudSourceConnector(
    IHttpTransport http,
    ISecureTokenStore tokens,
    IConnectorConsentStore consent,
    MySsiCloudClient mySsi) : GatedCloudSourceConnector(http, tokens, consent)
{
    public const string Id = "mares";

    /// <summary>Case-insensitive brand aliases identifying a Mares source computer in the MySSI logbook.</summary>
    private static readonly string[] BrandAliases = ["mares"];

    public override ConnectorDescriptor Descriptor { get; } = new(
        ConnectorId: Id,
        Kind: ConnectorKind.CloudApi,
        Legal: LegalPosture.ToSRiskReverseEng,
        Fidelity: ProfileFidelity.FullProfile, // MySSI dive records carry the full profile + cylinders.
        Auth: AuthModel.UserCredential,
        Platforms: Platforms.Both,
        EnabledByDefault: false);

    public override string DisplayName => "Mares (personal export)";

    protected override string Disclosure =>
        "This imports your own Mares dives from your personal MySSI (SSI) account — Mares dive computers " +
        "sync into MySSI, and Mares offers no cloud API of its own. It signs in with your MySSI credentials " +
        "against SSI's private cloud, which is NOT an official Mares or SSI integration and may breach SSI's " +
        "Terms of Service. Your password is used once to obtain an access token and is never stored; only the " +
        "token is kept, in secure device storage. You can disconnect at any time.";

    protected override Task<string> ExchangeCredentialsForTokenAsync(ConnectorCredentials credentials, CancellationToken ct) =>
        mySsi.AuthenticateAsync(credentials.Username, credentials.Password, ct);

    public override async Task<IReadOnlyList<DiveRef>> ListDivesSinceAsync(SyncCursor cursor, CancellationToken ct = default)
    {
        var token = await RequireTokenAsync(ct).ConfigureAwait(false);
        var summaries = await mySsi.ListDivesAsync(token, cursor.Since, BrandAliases, ct).ConfigureAwait(false);
        return [.. summaries.Select(s => new DiveRef(Descriptor.ConnectorId, s.Id, s.StartUtc, s.DurationSec, s.ComputerBrand))];
    }

    public override async Task<ImportedDive> FetchProfileAsync(DiveRef diveRef, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(diveRef);
        var token = await RequireTokenAsync(ct).ConfigureAwait(false);
        return await mySsi.FetchDiveAsync(token, diveRef.SourceId, ct).ConfigureAwait(false);
    }
}
