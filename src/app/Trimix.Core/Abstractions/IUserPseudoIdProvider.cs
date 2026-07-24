namespace Trimix.Core.Abstractions;

/// <summary>
/// Single source of truth for the stable per-install analytics pseudo-id. Both the analytics pipe
/// (which stamps it onto every event) and <see cref="Trimix.Core.Api.ApiClient"/> (which sends it as
/// the <see cref="Trimix.Contracts.TrimixHeaders.UserPseudoId"/> header so server-emitted funnel
/// events stitch to the same device funnel) resolve it from here — never generating it twice.
/// </summary>
public interface IUserPseudoIdProvider
{
    /// <summary>Stable per-install pseudo id (created + persisted on first use).</summary>
    string UserPseudoId { get; }
}
