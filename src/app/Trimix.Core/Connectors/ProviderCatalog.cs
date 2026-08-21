namespace Trimix.Core.Connectors;

/// <summary>
/// Display metadata for the seven provider connectors (prototype S5/S6): the short platform name
/// shown on rows and in the sync sheet, the vendor domain rendered in the in-app sign-in sheet's
/// browser chrome, and the marquee label. These are PLATFORM names (where the diver's own account
/// lives), not integration claims — the truthful "personal export" labelling guardrail lives on
/// each connector's <c>DisplayName</c>/<c>Disclosure</c> and is surfaced before consent.
/// </summary>
public static class ProviderCatalog
{
    private static readonly Dictionary<string, (string Name, string Domain, string ShortLabel)> ById = new(StringComparer.Ordinal)
    {
        ["garmin_connect"] = ("Garmin Dive", "garmin.com", "GARMIN"),
        ["suunto_cloud"] = ("Suunto", "suunto.com", "SUUNTO"),
        ["scubapro"] = ("ScubaPro", "scubapro.com", "SCUBAPRO"),
        ["mares"] = ("Mares", "mares.com", "MARES"),
        ["oceanic_cloud"] = ("Oceanic+", "oceanicplus.com", "OCEANIC+"),
        ["shearwater"] = ("Shearwater", "shearwater.com", "SHEARWATER"),
        ["padi_push_personal"] = ("PADI", "padi.com", "PADI"),
        ["ssi_push_personal"] = ("SSI", "divessi.com", "SSI"),
    };

    /// <summary>
    /// Connector ids that are <b>supported</b> — the diver can sign in and sync. Every other entry in
    /// <see cref="ById"/> is reverse-engineered against researched-guess/placeholder endpoints that have never
    /// touched a live server, so it ships "Coming soon": visible but disabled, which keeps the Providers list
    /// honest. Supported does not require full-fidelity data: Garmin + ScubaPro are verified end-to-end
    /// (captured contracts under <c>docs/reverse-engineering/</c>); <b>Suunto</b> has a verified sign-in +
    /// dive-list corridor (Sports Tracker — <c>suunto-sports-tracker-api.md</c>) and imports what that returns,
    /// degrading to a summary-only dive where the depth/temp profile isn't available yet (founder decision
    /// 2026-08-07: a data-completeness gap is still "supported", not "coming soon"). To promote a provider,
    /// add its id here — nothing else in the UI needs to change.
    /// </summary>
    private static readonly HashSet<string> LiveConnectorIds = new(StringComparer.Ordinal)
    {
        "garmin_connect",
        "scubapro",
        "suunto_cloud",
    };

    /// <summary>True when a connector is implemented + verified live (connectable today); false ⇒ "Coming soon".</summary>
    public static bool IsAvailable(string? connectorId) =>
        connectorId is not null && LiveConnectorIds.Contains(connectorId);

    /// <summary>Short platform name for a connector id (falls back to the id itself).</summary>
    public static string NameFor(string connectorId) =>
        ById.TryGetValue(connectorId, out var entry) ? entry.Name : connectorId.Replace('_', ' ');

    /// <summary>
    /// UPPERCASE route-string label (prototype: <c>GARMIN → PADI · SSI</c>) for a connector id OR a
    /// dive's stored <c>SourceVendor</c>. Falls back to the uppercased id so unknown vendors still
    /// render honestly.
    /// </summary>
    public static string ShortLabelFor(string? connectorId) =>
        connectorId is null ? ""
        : ById.TryGetValue(connectorId, out var entry) ? entry.ShortLabel
        : connectorId.Replace('_', ' ').ToUpperInvariant();

    /// <summary>Vendor domain for the sign-in sheet chrome (falls back to a neutral placeholder).</summary>
    public static string DomainFor(string connectorId) =>
        ById.TryGetValue(connectorId, out var entry) ? entry.Domain : "provider.com";

    /// <summary>Single-letter monogram used as the sign-in sheet's provider "logo" placeholder.</summary>
    public static string MonogramFor(string connectorId)
    {
        var name = NameFor(connectorId);
        return name.Length > 0 ? name[..1].ToUpperInvariant() : "?";
    }
}
