namespace Trimix.Core.Abstractions;

/// <summary>Secure key/value storage (platform SecureStorage in App): JWT, cached entitlement tier.</summary>
public interface ISecureStore
{
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    Task SetAsync(string key, string value, CancellationToken cancellationToken = default);

    void Remove(string key);
}
