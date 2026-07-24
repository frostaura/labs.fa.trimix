using Trimix.Core.Abstractions;

namespace Trimix.Core.Connectors.Infra;

/// <summary>
/// Default <see cref="ISecureTokenStore"/>: an adapter over the app's <see cref="ISecureStore"/> (platform
/// SecureStorage in the MAUI app, in-memory in dev/tests). Because <see cref="ISecureStore"/> is already the
/// Keychain/Keystore-backed secure surface, connector tokens land in the platform secure enclave with no new
/// platform impl. Keys are namespaced under <c>token:</c> to avoid collision with the JWT/entitlement keys.
/// </summary>
public sealed class SecureStoreTokenStore(ISecureStore secureStore) : ISecureTokenStore
{
    private static string Namespaced(string key) => $"token:{key}";

    public Task<string?> GetAsync(string key, CancellationToken ct = default) =>
        secureStore.GetAsync(Namespaced(key), ct);

    public Task SetAsync(string key, string token, CancellationToken ct = default) =>
        secureStore.SetAsync(Namespaced(key), token, ct);

    public Task RemoveAsync(string key, CancellationToken ct = default)
    {
        secureStore.Remove(Namespaced(key));
        return Task.CompletedTask;
    }
}
