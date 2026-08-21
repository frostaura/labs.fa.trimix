using Trimix.Contracts.Dtos;
using Trimix.Core.Abstractions;
using Trimix.Core.Stats;

namespace Trimix.Core.Connectors;

/// <summary>
/// <see cref="IProviderPriority"/> backed by <see cref="IPreferences"/> (a CSV of connector ids under
/// <see cref="PreferenceKeys.ProviderPriority"/>). The default order is the registration order of the
/// injected source connectors — the same order the Providers list shows — so before the diver reorders
/// anything, priority already mirrors what they see.
/// </summary>
public sealed class ProviderPriorityService : IProviderPriority
{
    // Weight that makes priority dominate the composite delegate. FidelityScore maxes out around a dozen,
    // so any non-zero rank difference outranks every fidelity difference.
    private const int PriorityWeight = 1000;

    private readonly IPreferences _preferences;
    private readonly IReadOnlyList<string> _defaultOrder;

    public ProviderPriorityService(IPreferences preferences, IEnumerable<IDiveSourceConnector> sources)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        ArgumentNullException.ThrowIfNull(sources);
        _preferences = preferences;
        _defaultOrder = sources.Select(s => s.Descriptor.ConnectorId).Distinct(StringComparer.Ordinal).ToList();
    }

    public IReadOnlyList<string> Order() => BuildOrder();

    private List<string> BuildOrder()
    {
        var known = new HashSet<string>(_defaultOrder, StringComparer.Ordinal);
        var stored = _preferences.Get(PreferenceKeys.ProviderPriority);

        var result = new List<string>(_defaultOrder.Count);
        if (!string.IsNullOrWhiteSpace(stored))
        {
            foreach (var id in stored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                // Keep only ids that are still registered, and de-dupe.
                if (known.Contains(id) && !result.Contains(id, StringComparer.Ordinal))
                {
                    result.Add(id);
                }
            }
        }

        // Append any registered provider the stored order didn't mention (new/unseen connector).
        foreach (var id in _defaultOrder)
        {
            if (!result.Contains(id, StringComparer.Ordinal))
            {
                result.Add(id);
            }
        }

        return result;
    }

    public int RankOf(string? sourceVendor)
    {
        if (string.IsNullOrEmpty(sourceVendor))
        {
            return 0;
        }

        var order = BuildOrder();
        var index = order.IndexOf(sourceVendor);
        // Earlier in the order = higher rank. Unknown vendor (not a registered source) ranks 0.
        return index < 0 ? 0 : order.Count - index;
    }

    public Func<DiveDto, int> FidelityDelegate()
    {
        // Snapshot the order once so a single Merge pass ranks consistently.
        var order = BuildOrder();
        int Rank(string? vendor)
        {
            if (string.IsNullOrEmpty(vendor))
            {
                return 0;
            }

            var index = order.IndexOf(vendor);
            return index < 0 ? 0 : order.Count - index;
        }

        return dive => Rank(dive.SourceVendor) * PriorityWeight + DiveMergePolicy.FidelityScore(dive);
    }

    public void Set(IReadOnlyList<string> order)
    {
        ArgumentNullException.ThrowIfNull(order);
        var known = new HashSet<string>(_defaultOrder, StringComparer.Ordinal);
        var cleaned = new List<string>(order.Count);
        foreach (var id in order)
        {
            if (known.Contains(id) && !cleaned.Contains(id, StringComparer.Ordinal))
            {
                cleaned.Add(id);
            }
        }

        _preferences.Set(PreferenceKeys.ProviderPriority, string.Join(',', cleaned));
    }

    public void Move(string connectorId, int newIndex)
    {
        ArgumentException.ThrowIfNullOrEmpty(connectorId);
        var order = Order().ToList();
        var from = order.IndexOf(connectorId);
        if (from < 0)
        {
            return;
        }

        order.RemoveAt(from);
        var clamped = Math.Clamp(newIndex, 0, order.Count);
        order.Insert(clamped, connectorId);
        Set(order);
    }
}
