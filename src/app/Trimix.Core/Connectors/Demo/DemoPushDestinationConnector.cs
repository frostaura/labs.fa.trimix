using Trimix.Contracts.Dtos;
using Trimix.Core.Abstractions;

namespace Trimix.Core.Connectors.Demo;

/// <summary>
/// A DEBUG-only demo push destination (PADI / SSI stand-ins): instant Granted auth and every dive
/// reports <see cref="PushStatus.Pushed"/> — so the sync sheet's "PADI &amp; SSI kept in sync" leg is
/// steppable without a vendor endpoint. Registered only in Debug builds; selected via
/// <see cref="DemoModeState"/> (id prefix <c>demo_</c>).
/// </summary>
public sealed class DemoPushDestinationConnector(string connectorId) : IDiveDestinationConnector, IDemoConnector
{
    public ConnectorDescriptor Descriptor { get; } = new(
        connectorId,
        ConnectorKind.CloudApi,
        LegalPosture.CleanOfficial,
        ProfileFidelity.SummaryOnly,
        AuthModel.None,
        Platforms.Both,
        EnabledByDefault: true);

    public Task<ConnectorAuthResult> AuthenticateAsync(CancellationToken ct = default) =>
        Task.FromResult(new ConnectorAuthResult(ConnectorAuthStatus.Granted));

    public Task<PushResult> PushDiveAsync(DiveDto dive, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dive);
        return Task.FromResult(new PushResult(PushStatus.Pushed, dive.Id.ToString("N")));
    }
}
