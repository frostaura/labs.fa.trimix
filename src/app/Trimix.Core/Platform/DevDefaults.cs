using Trimix.Core.Abstractions;

namespace Trimix.Core.Platform;

// Dev/test fallbacks TryAdd-ed by AddTrimixCore. The MAUI App registers its platform
// implementations (SecureStorage, Preferences, Connectivity, Geolocation, runtime permissions,
// AppInfo, Shell navigation, dialogs) BEFORE calling AddTrimixCore, which makes these no-ops.

/// <summary>In-memory <see cref="ISecureStore"/> (platform SecureStorage replaces this in the App).</summary>
public sealed class InMemorySecureStore : ISecureStore
{
    private readonly Dictionary<string, string> _values = [];
    private readonly Lock _gate = new();

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_values.TryGetValue(key, out var value) ? value : null);
        }
    }

    public Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _values[key] = value;
        }

        return Task.CompletedTask;
    }

    public void Remove(string key)
    {
        lock (_gate)
        {
            _values.Remove(key);
        }
    }
}

/// <summary>In-memory <see cref="IPreferences"/> (platform Preferences replaces this in the App).</summary>
public sealed class InMemoryPreferences : IPreferences
{
    private readonly Dictionary<string, string> _values = [];
    private readonly Lock _gate = new();

    public string? Get(string key)
    {
        lock (_gate)
        {
            return _values.TryGetValue(key, out var value) ? value : null;
        }
    }

    public void Set(string key, string value)
    {
        lock (_gate)
        {
            _values[key] = value;
        }
    }

    public void Remove(string key)
    {
        lock (_gate)
        {
            _values.Remove(key);
        }
    }
}

/// <summary>Always-online <see cref="IConnectivityService"/> (platform Connectivity replaces this in the App).</summary>
public sealed class AlwaysOnlineConnectivity : IConnectivityService
{
    public bool IsOnline => true;

    public event EventHandler<bool>? ConnectivityChanged
    {
        add { }
        remove { }
    }
}

/// <summary>Location-unavailable <see cref="ILocationService"/> (platform Geolocation replaces this in the App).</summary>
public sealed class NullLocationService : ILocationService
{
    public Task<GeoLocation?> GetCurrentLocationAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<GeoLocation?>(null);
}

/// <summary>Static <see cref="IAppInfo"/> (platform AppInfo replaces this in the App).</summary>
public sealed class DefaultAppInfo : IAppInfo
{
    public string Platform => "dev";

    public string AppVersion => "0.1.0";
}

/// <summary>No-op <see cref="INavigator"/> (the App's Shell navigator replaces this).</summary>
public sealed class NoopNavigator : INavigator
{
    public Task GoToAsync(string route, IReadOnlyDictionary<string, object>? parameters = null) =>
        Task.CompletedTask;

    public Task GoBackAsync() => Task.CompletedTask;
}

/// <summary>No-op <see cref="IUserDialogs"/> (the App's dialog service replaces this).</summary>
public sealed class NoopUserDialogs : IUserDialogs
{
    public Task AlertAsync(string title, string message, string cancel = "OK") => Task.CompletedTask;

    public Task<bool> ConfirmAsync(string title, string message, string accept = "OK", string cancel = "Cancel") =>
        Task.FromResult(true);

    public Task ToastAsync(string message) => Task.CompletedTask;
}
