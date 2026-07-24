using System.Collections.Generic;
using Trimix.Core.Abstractions;
using Trimix.Core.Connectors.Infra;

namespace Trimix.Core.Connectors.Cloud;

/// <summary>
/// Base for every reverse-engineered cloud-pull source connector (Garmin, Suunto, Oceanic+). It makes the
/// founder-directed legal guardrails <b>structural</b>, not per-connector discipline:
///
/// <list type="number">
///   <item><b>(iii) Consent-gated, zero I/O before opt-in.</b> <see cref="AuthenticateAsync"/> returns
///     <c>NeedsInteraction</c> + the plain-language ToS-risk <see cref="Disclosure"/> and performs no network
///     call until <c>IConnectorConsentStore.HasConsent(id)</c> is true.</item>
///   <item><b>(ii) No plaintext password persisted.</b> Credentials are supplied transiently via
///     <see cref="ProvideCredentials"/>, used once by <see cref="ExchangeCredentialsForTokenAsync"/>, then
///     cleared. Only the returned token is written to <c>ISecureTokenStore</c>.</item>
///   <item><b>(i) Ships dark.</b> The concrete <see cref="Descriptor"/> is <c>ToSRiskReverseEng</c> +
///     <c>EnabledByDefault=false</c>.</item>
///   <item><b>(iv) Truthful labelling.</b> <see cref="DisplayName"/> says "personal export", never
///     "official &lt;Vendor&gt;".</item>
/// </list>
/// </summary>
public abstract class GatedCloudSourceConnector(
    IHttpTransport http,
    ISecureTokenStore tokens,
    IConnectorConsentStore consent) : IDiveSourceConnector
{
    private ConnectorCredentials? _pendingCredentials;

    /// <summary>The HTTP port; every network call routes through it so the whole flow is offline-testable.</summary>
    protected IHttpTransport Http { get; } = http;

    public abstract ConnectorDescriptor Descriptor { get; }

    /// <summary>Truthful, user-facing name for the (later) opt-in UI — "personal export", never "official".</summary>
    public abstract string DisplayName { get; }

    /// <summary>Plain-language ToS-risk disclosure shown before opt-in; returned on <c>NeedsInteraction</c>.</summary>
    protected abstract string Disclosure { get; }

    /// <summary>Secure-store key for this connector's exchanged access token (never a password).</summary>
    protected string TokenKey => ConnectorTokenKeys.CloudSourceToken(Descriptor.ConnectorId);

    /// <summary>
    /// Supplies credentials for the next authenticate. Held only transiently and cleared after the exchange —
    /// the raw password never reaches <c>ISecureTokenStore</c> nor any persistent surface.
    /// </summary>
    public void ProvideCredentials(string username, string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(username);
        ArgumentException.ThrowIfNullOrEmpty(password);
        _pendingCredentials = new ConnectorCredentials(username, password);
    }

    public async Task<ConnectorAuthResult> AuthenticateAsync(CancellationToken ct = default)
    {
        // Guardrail (ii): snapshot + clear the pending credentials up front so the plaintext password never
        // lingers on this long-lived singleton beyond the single authenticate that consumes it — including the
        // early-return paths below (no consent / a token already exists), which previously left it resident.
        var credentials = _pendingCredentials;
        _pendingCredentials = null;

        // Guardrail (iii): absolutely no network I/O until the user has opted in behind the disclosure.
        if (!consent.HasConsent(Descriptor.ConnectorId))
        {
            return new ConnectorAuthResult(ConnectorAuthStatus.NeedsInteraction, Disclosure);
        }

        // Reuse a token from a prior exchange (survives across sessions in the secure enclave).
        var existing = await tokens.GetAsync(TokenKey, ct).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(existing))
        {
            return new ConnectorAuthResult(ConnectorAuthStatus.Granted);
        }

        if (credentials is null)
        {
            return new ConnectorAuthResult(
                ConnectorAuthStatus.NeedsInteraction,
                $"Sign in to your {DisplayName} account to import your dives.");
        }

        try
        {
            var token = await ExchangeCredentialsForTokenAsync(credentials, ct).ConfigureAwait(false);
            // Guardrail (ii): only the exchanged token is persisted — the password is never written.
            await tokens.SetAsync(TokenKey, token, ct).ConfigureAwait(false);
            return new ConnectorAuthResult(ConnectorAuthStatus.Granted);
        }
        catch (ConnectorAuthException ex)
        {
            return new ConnectorAuthResult(ConnectorAuthStatus.Denied, ex.Message);
        }
    }

    public abstract Task<IReadOnlyList<DiveRef>> ListDivesSinceAsync(SyncCursor cursor, CancellationToken ct = default);

    public abstract Task<ImportedDive> FetchProfileAsync(DiveRef diveRef, CancellationToken ct = default);

    /// <summary>The credential→token exchange, using the researched per-vendor flow. Throws
    /// <see cref="ConnectorAuthException"/> on failure. Receives credentials by value; must not persist them.</summary>
    protected abstract Task<string> ExchangeCredentialsForTokenAsync(ConnectorCredentials credentials, CancellationToken ct);

    /// <summary>Loads the stored access token or throws if the caller skipped a Granted <see cref="AuthenticateAsync"/>.</summary>
    protected async Task<string> RequireTokenAsync(CancellationToken ct)
    {
        var token = await tokens.GetAsync(TokenKey, ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(token))
        {
            throw new InvalidOperationException(
                $"{Descriptor.ConnectorId}: AuthenticateAsync must return Granted before syncing dives.");
        }

        return token;
    }

    /// <summary>Bearer-auth header dictionary for token-authenticated calls.</summary>
    protected static Dictionary<string, string> Bearer(string token) => new()
    {
        ["Authorization"] = $"Bearer {token}",
        ["Accept"] = "application/json",
    };
}
