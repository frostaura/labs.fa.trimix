using System.Text.Json;
using Trimix.Contracts.Dtos;
using Trimix.Core.Abstractions;
using Trimix.Core.Connectors.Infra;

namespace Trimix.Core.Connectors.Push;

/// <summary>
/// Base for the founder-directed, legally-gated cert-agency PUSH connectors (docs/known-gaps.md legal
/// override). These reverse-engineer private, undocumented vendor backends, so the guardrails are
/// structural, not optional:
/// <list type="number">
///   <item><see cref="LegalPosture.ToSRiskReverseEng"/> + <c>EnabledByDefault=false</c> — ships dark.</item>
///   <item>ZERO network I/O until the user has opted in via <see cref="IConnectorConsentStore"/>; before
///     that <see cref="AuthenticateAsync"/> returns <see cref="ConnectorAuthStatus.NeedsInteraction"/>
///     carrying the plain-language ToS-risk <see cref="DisclosureMessage"/>.</item>
///   <item>the raw vendor password is used once for an ephemeral credential->token exchange and NEVER
///     persisted; only the exchanged token is stored, via <see cref="ISecureTokenStore"/>.</item>
///   <item>truthful ids/labels ("…_push_personal" — a user-assisted personal export, never "official").</item>
/// </list>
/// </summary>
public abstract class GatedPushConnectorBase(
    IConnectorConsentStore consent,
    ISecureTokenStore tokens,
    IHttpTransport http) : IDiveDestinationConnector
{
    /// <summary>The HTTP port (G4 infra) — testable offline via a recorded/scripted transport.</summary>
    protected IHttpTransport Http { get; } = http;

    public abstract ConnectorDescriptor Descriptor { get; }

    /// <summary>Plain-language ToS-risk disclosure shown before opt-in (guardrail 2).</summary>
    protected abstract string DisclosureMessage { get; }

    /// <summary>Secure-store key holding the EXCHANGED token (never a password) for this connector.</summary>
    protected string TokenKey => ConnectorTokenKeys.PushToken(Descriptor.ConnectorId);

    /// <summary>
    /// Guardrail 2: no consent => <see cref="ConnectorAuthStatus.NeedsInteraction"/> + disclosure, and
    /// ZERO network I/O. With consent but no stored token => NeedsInteraction (sign-in). Reading the
    /// local secure token store is not network I/O.
    /// </summary>
    public async Task<ConnectorAuthResult> AuthenticateAsync(CancellationToken ct = default)
    {
        if (!consent.HasConsent(Descriptor.ConnectorId))
        {
            return new ConnectorAuthResult(ConnectorAuthStatus.NeedsInteraction, DisclosureMessage);
        }

        var token = await tokens.GetAsync(TokenKey, ct).ConfigureAwait(false);
        return token is null
            ? new ConnectorAuthResult(ConnectorAuthStatus.NeedsInteraction, "Sign in to continue.")
            : new ConnectorAuthResult(ConnectorAuthStatus.Granted);
    }

    /// <summary>
    /// Extra affordance (not on the port): the ephemeral credential->token exchange. Requires prior
    /// consent; on success stores ONLY the token (guardrail 3). Returns <see cref="ConnectorAuthStatus"/>
    /// so the UI can react without exceptions.
    /// </summary>
    public async Task<ConnectorAuthResult> SignInAsync(string username, string password, CancellationToken ct = default)
    {
        if (!consent.HasConsent(Descriptor.ConnectorId))
        {
            return new ConnectorAuthResult(ConnectorAuthStatus.NeedsInteraction, DisclosureMessage);
        }

        var token = await ExchangeCredentialsAsync(username, password, ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(token))
        {
            return new ConnectorAuthResult(
                ConnectorAuthStatus.Unavailable,
                "This personal-export connector has no configured endpoint. Use file export instead.");
        }

        // Only the exchanged token is persisted — the raw password never touches storage.
        await tokens.SetAsync(TokenKey, token, ct).ConfigureAwait(false);
        return new ConnectorAuthResult(ConnectorAuthStatus.Granted);
    }

    public async Task<PushResult> PushDiveAsync(DiveDto dive, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dive);

        // Guardrail 2: consent-gate before any network I/O.
        if (!consent.HasConsent(Descriptor.ConnectorId))
        {
            return new PushResult(PushStatus.Skipped, Descriptor.ConnectorId, DisclosureMessage);
        }

        var token = await tokens.GetAsync(TokenKey, ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(token))
        {
            return new PushResult(PushStatus.Failed, Descriptor.ConnectorId, "Not authenticated — sign in first.");
        }

        return await PushCoreAsync(dive, token, ct).ConfigureAwait(false);
    }

    /// <summary>Exchanges credentials for a token against the (config-driven) vendor endpoint. Null => unconfigured.</summary>
    protected abstract Task<string?> ExchangeCredentialsAsync(string username, string password, CancellationToken ct);

    /// <summary>Pushes the dive with a valid token. Consent + token are already guaranteed by the caller.</summary>
    protected abstract Task<PushResult> PushCoreAsync(DiveDto dive, string token, CancellationToken ct);

    /// <summary>Bearer-authenticated request headers.</summary>
    protected static IReadOnlyDictionary<string, string> BearerHeaders(string token, string contentType) =>
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Authorization"] = "Bearer " + token,
            ["Content-Type"] = contentType,
        };

    protected static bool IsSuccess(int statusCode) => statusCode is >= 200 and < 300;

    /// <summary>Pulls a token from a JSON body (<c>token</c>/<c>access_token</c>) or falls back to the raw body.</summary>
    protected static string? ExtractToken(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        var trimmed = body.Trim();
        if (trimmed.StartsWith('{'))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                foreach (var key in (ReadOnlySpan<string>)["token", "access_token", "accessToken"])
                {
                    if (doc.RootElement.TryGetProperty(key, out var el) && el.ValueKind == JsonValueKind.String)
                    {
                        return el.GetString();
                    }
                }

                return null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        return trimmed;
    }
}
