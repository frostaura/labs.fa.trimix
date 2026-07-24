namespace Trimix.Core.Abstractions;

/// <summary>
/// Secure storage for connector access tokens (aggregator infra). <b>Guardrail:</b> this store holds only
/// <em>exchanged tokens</em> — an OAuth/bearer access (or refresh) token minted from a credential exchange —
/// and <em>never a raw vendor password</em>. The password is used once, in-memory, to obtain the token; only
/// the token is written here. The real implementation is the platform secure enclave (Keychain / Keystore);
/// the default <c>SecureStoreTokenStore</c> routes to the existing <see cref="ISecureStore"/> so no new
/// platform code is required, and tests use an in-memory double.
/// </summary>
public interface ISecureTokenStore
{
    Task<string?> GetAsync(string key, CancellationToken ct = default);

    Task SetAsync(string key, string token, CancellationToken ct = default);

    Task RemoveAsync(string key, CancellationToken ct = default);
}
