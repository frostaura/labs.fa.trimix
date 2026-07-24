using Trimix.Contracts;

namespace Trimix.Api.Analytics;

/// <summary>
/// Server-side validation against the FROZEN funnel taxonomy (<see cref="FunnelEvents"/>).
/// Ingest rejects unknown event names, missing required properties, drifted enumerated values, and
/// (for client submissions) server-reserved event names; server-emitted rows are validated through
/// the same gate so the server can never drift from its own contract.
/// </summary>
public static class FunnelContract
{
    /// <summary>
    /// Returns an error description, or null when the event satisfies the frozen contract.
    /// </summary>
    /// <param name="clientSubmitted">
    /// True when the event arrived on the unauthenticated ingest endpoint. Client submissions may
    /// never name a <see cref="FunnelEvents.ServerOnly"/> event (spoofing server-authoritative state
    /// such as <c>entitlement_granted</c>); server-side emission passes false.
    /// </param>
    public static string? Validate(string? name, IReadOnlyDictionary<string, string>? props, bool clientSubmitted = false)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Event name is required.";
        }

        if (!FunnelEvents.RequiredProps.TryGetValue(name, out var required))
        {
            return $"Unknown event name '{name}' — not part of the frozen funnel taxonomy.";
        }

        if (clientSubmitted && FunnelEvents.ServerOnly.Contains(name))
        {
            return $"Event '{name}' is server-emitted only and cannot be submitted by a client.";
        }

        foreach (var prop in required)
        {
            if (props is null || !props.TryGetValue(prop, out var value) || string.IsNullOrEmpty(value))
            {
                return $"Event '{name}' is missing required property '{prop}'.";
            }
        }

        // Enumerated-value check is the SHARED contract (Trimix.Contracts.FunnelValidation) so the client and
        // server never diverge — and it now also enforces outcome∈{granted,denied} on the terminal
        // ble-permission events (previously AllowedValues[outcome] was declared but never enforced).
        var valueError = FunnelValidation.ValidateEnumeratedValues(name, props);
        if (valueError is not null)
        {
            return valueError;
        }

        return null;
    }
}
