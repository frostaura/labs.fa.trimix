using System.Collections.Concurrent;
using Trimix.Core.Abstractions;

namespace Trimix.Harness;

/// <summary>
/// Process-lifetime token store standing in for the iOS Keychain / Android Keystore. Deliberately
/// non-persistent: a dev harness must never leave a real vendor token on disk.
/// </summary>
public sealed class InMemoryTokenStore : ISecureTokenStore
{
    private readonly ConcurrentDictionary<string, string> _values = new();

    public Task<string?> GetAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(_values.TryGetValue(key, out var value) ? value : null);

    public Task SetAsync(string key, string token, CancellationToken ct = default)
    {
        _values[key] = token;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken ct = default)
    {
        _values.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}

/// <summary>
/// File-backed token store for the harness only. It exists because Garmin <b>rate-limits repeated
/// sign-ins</b> (observed: HTTP 429 after four logins in a few minutes), so re-authenticating on every dev
/// run is both slow and antisocial. With a cached envelope the connector short-circuits authentication and
/// goes straight to reading dives.
///
/// <para>Never use this in the app: the real store is the platform secure enclave. Point it at a scratch
/// path, not the repo — it holds a live vendor token.</para>
/// </summary>
public sealed class FileTokenStore(string path) : ISecureTokenStore
{
    private Dictionary<string, string> Load()
    {
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? [];
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or IOException)
        {
            return [];
        }
    }

    private void Save(Dictionary<string, string> values)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(values));
    }

    public Task<string?> GetAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(Load().TryGetValue(key, out var value) ? value : null);

    public Task SetAsync(string key, string token, CancellationToken ct = default)
    {
        var values = Load();
        values[key] = token;
        Save(values);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken ct = default)
    {
        var values = Load();
        values.Remove(key);
        Save(values);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Consent ledger for the harness. It starts <b>empty on purpose</b> so the harness exercises the real
/// guardrail — a gated connector must return <c>NeedsInteraction</c> and make zero network calls until
/// consent is explicitly granted, exactly as it would behind the in-app disclosure.
/// </summary>
public sealed class InMemoryConsentStore : IConnectorConsentStore
{
    private readonly HashSet<string> _granted = [];

    public bool HasConsent(string connectorId) => _granted.Contains(connectorId);

    public Task GrantAsync(string connectorId, CancellationToken ct = default)
    {
        _granted.Add(connectorId);
        return Task.CompletedTask;
    }

    public Task RevokeAsync(string connectorId, CancellationToken ct = default)
    {
        _granted.Remove(connectorId);
        return Task.CompletedTask;
    }
}
