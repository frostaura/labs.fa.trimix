namespace Trimix.Contracts;

/// <summary>
/// The SINGLE, shared enumerated-value check for the frozen funnel taxonomy. Both the client analytics pipe
/// (<c>AnalyticsService.TryValidate</c>) and the server ingest (<c>FunnelContract.Validate</c>) call this, so
/// the two validators can never drift — a value the server rejects batch-fatally is rejected client-side too,
/// before it can enter the durable queue and poison-pill it.
/// </summary>
public static class FunnelValidation
{
    /// <summary>
    /// Rejects drifted values for the frozen enumerated properties. Overloaded props are validated per-event,
    /// not globally: <c>source</c> is frozen to <c>import</c> only on <c>dive_logged_import</c> (a free-text
    /// channel on <c>install</c> / <c>source_connected</c> / <c>dives_synced</c>); <c>outcome</c> is frozen to
    /// <c>{granted,denied}</c> on the TERMINAL ble-permission events, while <c>ble_permission_requested</c>
    /// carries the documented <c>requested</c> lifecycle value and is left free. Returns an error, or null when
    /// every enumerated value is in-contract.
    /// </summary>
    public static string? ValidateEnumeratedValues(string name, IReadOnlyDictionary<string, string>? props)
    {
        if (props is null)
        {
            return null;
        }

        foreach (var (key, allowed) in FunnelEvents.AllowedValues)
        {
            // Overloaded props are validated below, not by the global map.
            if (key is FunnelEvents.Props.Source or FunnelEvents.Props.Outcome)
            {
                continue;
            }

            if (props.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value) && !allowed.Contains(value))
            {
                return $"Event '{name}' has value '{value}' outside the frozen set for property '{key}'.";
            }
        }

        // dive_logged_import.source is frozen to 'import' (install.source is a free-text channel).
        if (name == FunnelEvents.DiveLoggedImport
            && props.TryGetValue(FunnelEvents.Props.Source, out var source)
            && !string.IsNullOrEmpty(source)
            && source != FunnelEvents.Values.SourceImport)
        {
            return $"Event '{name}' requires source='{FunnelEvents.Values.SourceImport}'.";
        }

        // outcome is frozen to {granted,denied} on the terminal ble-permission events; the 'requested' lifecycle
        // value on ble_permission_requested is intentionally not enforced (see FunnelEvents.AllowedValues note).
        if ((name == FunnelEvents.BlePermissionGranted || name == FunnelEvents.BlePermissionDenied)
            && props.TryGetValue(FunnelEvents.Props.Outcome, out var outcome)
            && !string.IsNullOrEmpty(outcome)
            && !FunnelEvents.AllowedValues[FunnelEvents.Props.Outcome].Contains(outcome))
        {
            return $"Event '{name}' has value '{outcome}' outside the frozen set for property '{FunnelEvents.Props.Outcome}'.";
        }

        return null;
    }
}
