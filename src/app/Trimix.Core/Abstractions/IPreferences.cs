namespace Trimix.Core.Abstractions;

/// <summary>
/// Non-secret key/value preferences (platform Preferences in App; in-memory in tests/dev).
/// Cleared on uninstall — which is exactly the semantics the install-once analytics flag
/// and the auto-restore-attempted flag rely on.
/// </summary>
public interface IPreferences
{
    string? Get(string key);

    void Set(string key, string value);

    void Remove(string key);
}
