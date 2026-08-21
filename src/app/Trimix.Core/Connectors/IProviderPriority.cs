using Trimix.Contracts.Dtos;

namespace Trimix.Core.Connectors;

/// <summary>
/// The user-defined priority order over dive-source providers, used to decide which provider wins when
/// overlapping dives (the same physical dive logged on two computers) are merged. The order is persisted
/// locally (a CSV of connector ids in <see cref="Abstractions.IPreferences"/>) and drives the
/// <see cref="Stats.DiveMergePolicy.Merge"/> primary pick via <see cref="FidelityDelegate"/> — a
/// priority-dominant composite that leaves fidelity as the secondary tie-break, so the primary's stats
/// lead and any field it lacks fills from the next provider in priority order.
///
/// <para>Keyed by connector id, which equals a synced dive's <see cref="DiveDto.SourceVendor"/>, so no
/// mapping table is needed. Only source (readable) providers participate; push targets (PADI/SSI) never
/// carry dive rows and are ignored.</para>
/// </summary>
public interface IProviderPriority
{
    /// <summary>The current priority order, highest first. Persisted ids first, then any registered
    /// provider not yet ordered appended in default (registration) order; stale ids are dropped.</summary>
    IReadOnlyList<string> Order();

    /// <summary>Priority rank for a dive's <see cref="DiveDto.SourceVendor"/> — higher wins. An unknown or
    /// null vendor ranks lowest (0), below every ordered provider.</summary>
    int RankOf(string? sourceVendor);

    /// <summary>The composite delegate for <see cref="Stats.DiveMergePolicy.Merge"/>: priority dominates,
    /// fidelity breaks ties within one provider. Higher = preferred primary.</summary>
    Func<DiveDto, int> FidelityDelegate();

    /// <summary>Persist a new full order (highest first). Unknown ids are ignored on read.</summary>
    void Set(IReadOnlyList<string> order);

    /// <summary>Move one provider to a new index within the current order and persist.</summary>
    void Move(string connectorId, int newIndex);
}
