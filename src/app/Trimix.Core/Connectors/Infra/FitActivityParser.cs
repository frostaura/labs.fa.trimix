using Trimix.Core.Abstractions;
using Trimix.Core.Connectors.Fit;

namespace Trimix.Core.Connectors.Infra;

/// <summary>
/// The real <see cref="IFitActivityParser"/>: adapts the file-import branch's <see cref="GarminFitParser"/>
/// (the dependency-free FIT decoder behind <see cref="FitReader"/>) to the byte-level port the cloud
/// connectors (Garmin, Suunto) depend on. A cloud connector downloads the ORIGINAL <c>.FIT</c> bytes and
/// hands them here; this wraps them in a synthetic <see cref="PickedFile"/> and reuses the exact same
/// spec-correct decoder the file-import path uses — no second FIT implementation. Replaces the
/// <c>PendingFitActivityParser</c> stub so the gated cloud connectors resolve and (once enabled) actually work.
/// </summary>
public sealed class FitActivityParser : IFitActivityParser
{
    private readonly GarminFitParser _parser = new();

    public IReadOnlyList<ImportedDive> Parse(ReadOnlyMemory<byte> fitBytes) =>
        _parser.Parse(new PickedFile("activity.fit", fitBytes.ToArray()));
}
