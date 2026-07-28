namespace Trimix.Contracts;

/// <summary>
/// Free-tier caps, aligned to the prototype's monetization moment: the free tier keeps the diver's
/// last FIVE dives, and the sixth dive arriving via sync is the purchase moment
/// (<c>paywall_shown(trigger=cloud_sync_cap)</c>). Enforced client-side by Core's EntitlementGate
/// (serverless — there is no server to enforce anything). Copy binds to these constants (never a
/// hardcoded "5") so enforcement and copy cannot drift.
/// </summary>
public static class FreeTier
{
    /// <summary>
    /// The free dive ceiling for import: the allowance for a sync is this minus the dives already
    /// stored, so "free keeps your last 5" holds across sessions — the 6th dive, whenever it
    /// arrives, is the paywall moment.
    /// </summary>
    public const int FreeImportDivesPerSession = 5;

    /// <summary>Max dives visible in the logbook on the free tier ("Keeps your last 5 dives").</summary>
    public const int FreeLogbookVisibleDives = 5;
}
