namespace Trimix.Contracts;

/// <summary>
/// Free-tier caps (architecture overview §9). Enforced client-side by Core's EntitlementGate
/// (unit-testable) and authoritatively by the server for site contributions.
/// </summary>
public static class FreeTier
{
    /// <summary>Max dives imported over BLE per import session on the free tier.</summary>
    public const int FreeImportDivesPerSession = 3;

    /// <summary>Max dives visible in the logbook on the free tier.</summary>
    public const int FreeLogbookVisibleDives = 25;
}
