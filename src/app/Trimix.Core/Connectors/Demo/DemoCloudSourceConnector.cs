using Trimix.Core.Abstractions;

namespace Trimix.Core.Connectors.Demo;

/// <summary>
/// A DEBUG-only demo dive source: instant Granted auth, seeded dives from
/// <see cref="DemoDiveCatalog"/>, zero network. It flows through the REAL
/// <c>ImportService</c>/<c>SyncCoordinator</c> pipeline (cursor, dedup, free-cap truncation, funnel
/// events), so only the vendor I/O is fake — the funnel/cap/paywall spine stays authentic.
/// Registered only in Debug builds; selected via <see cref="DemoModeState"/> (id prefix
/// <c>demo_</c>). <c>LegalPosture.CleanOfficial</c> is truthful here: no vendor is touched.
/// </summary>
public sealed class DemoCloudSourceConnector(string connectorId) : IDiveSourceConnector, IDemoConnector
{
    private readonly IReadOnlyList<ImportedDive> _dives = DemoDiveCatalog.DivesFor(connectorId);

    public ConnectorDescriptor Descriptor { get; } = new(
        connectorId,
        ConnectorKind.CloudApi,
        LegalPosture.CleanOfficial,
        ProfileFidelity.FullProfile,
        AuthModel.None,
        Platforms.Both,
        EnabledByDefault: true);

    public Task<ConnectorAuthResult> AuthenticateAsync(CancellationToken ct = default) =>
        Task.FromResult(new ConnectorAuthResult(ConnectorAuthStatus.Granted));

    public Task<IReadOnlyList<DiveRef>> ListDivesSinceAsync(SyncCursor cursor, CancellationToken ct = default)
    {
        IReadOnlyList<DiveRef> refs =
        [
            .. _dives
                .Where(d => cursor.Since is not { } since || d.StartUtc > since)
                .Select((d, i) => new DiveRef(
                    Descriptor.ConnectorId,
                    SourceIdFor(d),
                    d.StartUtc,
                    d.DurationSec,
                    $"Demo dive {i + 1}")),
        ];
        return Task.FromResult(refs);
    }

    public Task<ImportedDive> FetchProfileAsync(DiveRef diveRef, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(diveRef);
        var dive = _dives.FirstOrDefault(d => SourceIdFor(d) == diveRef.SourceId)
            ?? throw new InvalidOperationException($"Unknown demo dive '{diveRef.SourceId}'.");
        return Task.FromResult(dive);
    }

    private static string SourceIdFor(ImportedDive dive) => dive.StartUtc.ToString("O");
}
