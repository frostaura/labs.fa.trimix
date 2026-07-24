using Trimix.Contracts;
using Xunit;

namespace Trimix.Core.Tests;

/// <summary>
/// Placeholder anchor tests for the frozen shared truth. Real coverage (viewmodels, protocol
/// parser, caps/gates, analytics contract) lands with the implementations.
/// </summary>
public class FunnelContractTests
{
    [Fact]
    public void Every_frozen_event_has_required_props()
    {
        Assert.Equal(22, FunnelEvents.All.Count);
        Assert.All(FunnelEvents.All, name => Assert.True(
            FunnelEvents.RequiredProps.TryGetValue(name, out var props) && props.Count > 0,
            $"Event '{name}' is missing a RequiredProps entry."));
    }

    [Fact]
    public void Locked_ladder_is_exactly_two_nonconsumables()
    {
        Assert.Equal(2, Skus.All.Count);
        Assert.All(Skus.All, sku => Assert.Equal(SkuType.NonConsumable, sku.Type));
        Assert.Equal(14.99m, Skus.ProSku.PriceUsd);
        Assert.Equal(29.99m, Skus.LifetimeSku.PriceUsd);
    }

    [Fact]
    public void Free_tier_caps_match_contract()
    {
        Assert.Equal(3, FreeTier.FreeImportDivesPerSession);
        Assert.Equal(25, FreeTier.FreeLogbookVisibleDives);
    }
}
