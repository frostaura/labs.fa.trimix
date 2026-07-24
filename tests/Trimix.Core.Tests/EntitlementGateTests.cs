using Trimix.Contracts;
using Trimix.Core.Entitlements;
using Xunit;

namespace Trimix.Core.Tests;

/// <summary>Free-tier caps (architecture overview §9) — pure logic, fully covered.</summary>
public class EntitlementGateTests
{
    [Theory]
    [InlineData(EntitlementTier.Free, false)]
    [InlineData(EntitlementTier.Pro, true)]
    [InlineData(EntitlementTier.Lifetime, true)]
    public void IsPaid_matches_tier(EntitlementTier tier, bool expected) =>
        Assert.Equal(expected, EntitlementGate.IsPaid(tier));

    [Fact]
    public void Free_import_session_allowance_is_the_contract_cap()
    {
        Assert.Equal(FreeTier.FreeImportDivesPerSession, EntitlementGate.ImportSessionAllowance(EntitlementTier.Free));
        Assert.Equal(int.MaxValue, EntitlementGate.ImportSessionAllowance(EntitlementTier.Pro));
        Assert.Equal(int.MaxValue, EntitlementGate.ImportSessionAllowance(EntitlementTier.Lifetime));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(10, false)]
    public void Free_can_import_until_session_cap(int imported, bool expected) =>
        Assert.Equal(expected, EntitlementGate.CanImportMore(EntitlementTier.Free, imported));

    [Fact]
    public void Paid_tiers_always_can_import()
    {
        Assert.True(EntitlementGate.CanImportMore(EntitlementTier.Pro, 10_000));
        Assert.True(EntitlementGate.CanImportMore(EntitlementTier.Lifetime, 10_000));
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(2, 1)]
    [InlineData(3, 0)]
    [InlineData(99, 0)]
    public void Remaining_imports_never_negative(int imported, int expected) =>
        Assert.Equal(expected, EntitlementGate.RemainingImports(EntitlementTier.Free, imported));

    [Theory]
    [InlineData(EntitlementTier.Free, 0, 0, 0)]
    [InlineData(EntitlementTier.Free, 10, 10, 0)]
    [InlineData(EntitlementTier.Free, 25, 25, 0)]
    [InlineData(EntitlementTier.Free, 30, 25, 5)]
    [InlineData(EntitlementTier.Pro, 30, 30, 0)]
    [InlineData(EntitlementTier.Lifetime, 500, 500, 0)]
    public void Logbook_visibility_cap(EntitlementTier tier, int total, int visible, int hidden)
    {
        Assert.Equal(visible, EntitlementGate.LogbookVisibleCount(tier, total));
        Assert.Equal(hidden, EntitlementGate.LogbookHiddenCount(tier, total));
    }

    [Theory]
    [InlineData(EntitlementTier.Free, false)]
    [InlineData(EntitlementTier.Pro, true)]
    [InlineData(EntitlementTier.Lifetime, true)]
    public void Site_contribution_requires_paid_tier(EntitlementTier tier, bool expected) =>
        Assert.Equal(expected, EntitlementGate.CanContributeSites(tier));
}
