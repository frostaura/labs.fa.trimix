namespace Trimix.Contracts;

/// <summary>
/// Custom request headers understood by the Trimix API, shared by client and server so neither
/// side hard-codes the string. Canonical home for header names (additive contract amendment).
/// </summary>
public static class TrimixHeaders
{
    /// <summary>
    /// The client's analytics pseudo-id. Sent by the app on API calls that cause server-emitted
    /// funnel events (<c>account_created</c>, <c>entitlement_granted</c>, <c>entitlement_restored</c>)
    /// so those rows reconcile into the same per-pseudo-user funnel as client-emitted events.
    /// The <c>Trimix.Api.Analytics.TrimixHeaders</c> copy should reference this constant.
    /// </summary>
    public const string UserPseudoId = "X-Trimix-User-Pseudo-Id";
}
