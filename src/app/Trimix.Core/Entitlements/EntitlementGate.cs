using Trimix.Contracts;

namespace Trimix.Core.Entitlements;

/// <summary>
/// Pure free-tier cap logic (architecture overview §9): the import-session cap and the logbook
/// visibility cap. (Site-contribution gating was removed with the Sites capability.)
/// </summary>
public static class EntitlementGate
{
    /// <summary>Pro and Lifetime unlock everything gated here (lifetime ⊇ pro).</summary>
    public static bool IsPaid(EntitlementTier tier) =>
        tier is EntitlementTier.Pro or EntitlementTier.Lifetime;

    /// <summary>Max dives importable in one BLE import session.</summary>
    public static int ImportSessionAllowance(EntitlementTier tier) =>
        IsPaid(tier) ? int.MaxValue : FreeTier.FreeImportDivesPerSession;

    /// <summary>Whether one more dive may be imported this session.</summary>
    public static bool CanImportMore(EntitlementTier tier, int importedThisSession) =>
        importedThisSession < ImportSessionAllowance(tier);

    /// <summary>
    /// Imports left before the free ceiling (never negative; int.MaxValue when unlimited).
    /// <c>ImportService</c> passes the count of dives ALREADY STORED so "free keeps your last
    /// 5" holds across sessions, not just within one batch.
    /// </summary>
    public static int RemainingImports(EntitlementTier tier, int importedThisSession) =>
        IsPaid(tier)
            ? int.MaxValue
            : Math.Max(0, FreeTier.FreeImportDivesPerSession - importedThisSession);

    /// <summary>Max dives visible in the logbook.</summary>
    public static int LogbookVisibleAllowance(EntitlementTier tier) =>
        IsPaid(tier) ? int.MaxValue : FreeTier.FreeLogbookVisibleDives;

    /// <summary>How many of <paramref name="totalDives"/> the logbook shows.</summary>
    public static int LogbookVisibleCount(EntitlementTier tier, int totalDives) =>
        Math.Min(Math.Max(0, totalDives), LogbookVisibleAllowance(tier));

    /// <summary>How many dives the free logbook cap hides.</summary>
    public static int LogbookHiddenCount(EntitlementTier tier, int totalDives) =>
        Math.Max(0, totalDives) - LogbookVisibleCount(tier, totalDives);
}
