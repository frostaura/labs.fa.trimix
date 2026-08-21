using Trimix.Core.Abstractions;

namespace Trimix.App.Services;

// MAUI Essentials-backed implementations of Core's platform abstractions
// (architecture overview: "platform impls (SecureStorage, Preferences, ...)").
// Registered in MauiProgram BEFORE AddTrimixCore so Core's dev/test fallbacks never engage.

/// <summary><see cref="ISecureStore"/> over platform SecureStorage: JWT + cached entitlement tier.</summary>
public sealed class MauiSecureStore : ISecureStore
{
    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) =>
        SecureStorage.Default.GetAsync(key);

    public Task SetAsync(string key, string value, CancellationToken cancellationToken = default) =>
        SecureStorage.Default.SetAsync(key, value);

    public void Remove(string key) => SecureStorage.Default.Remove(key);
}

/// <summary>
/// <see cref="IPreferences"/> over platform Preferences. Cleared on uninstall — exactly the
/// semantics the install-once analytics flag and auto-restore-attempted flag rely on.
/// </summary>
public sealed class MauiPreferencesService : Trimix.Core.Abstractions.IPreferences
{
    public string? Get(string key) => Preferences.Default.Get<string?>(key, null);

    public void Set(string key, string value) => Preferences.Default.Set(key, value);

    public void Remove(string key) => Preferences.Default.Remove(key);
}

/// <summary><see cref="IConnectivityService"/> over Essentials Connectivity (drives sync + analytics flushing).</summary>
public sealed class MauiConnectivityService : IConnectivityService, IDisposable
{
    public MauiConnectivityService() =>
        Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;

    public bool IsOnline => Connectivity.Current.NetworkAccess == NetworkAccess.Internet;

    public event EventHandler<bool>? ConnectivityChanged;

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e) =>
        ConnectivityChanged?.Invoke(this, e.NetworkAccess == NetworkAccess.Internet);

    public void Dispose() => Connectivity.Current.ConnectivityChanged -= OnConnectivityChanged;
}

/// <summary>
/// <see cref="IMapLauncher"/> over Essentials <c>Map.OpenAsync</c> — Apple Maps on iOS, Google Maps on Android.
/// No API key, no NuGet, no secret in the APK.
///
/// <para>Failures are swallowed deliberately: a device with no maps app, or a user cancelling the hand-off, must
/// not surface an error on a dive detail page. The coordinates stay visible on screen regardless.</para>
/// </summary>
public sealed class MauiMapLauncher : IMapLauncher
{
    public async Task OpenAsync(
        double latitude, double longitude, string? label = null, CancellationToken ct = default)
    {
        try
        {
            var options = new MapLaunchOptions { Name = string.IsNullOrWhiteSpace(label) ? "Dive site" : label };
            await Microsoft.Maui.ApplicationModel.Map.OpenAsync(latitude, longitude, options);
        }
        catch (Exception ex) when (ex is FeatureNotSupportedException or FeatureNotEnabledException or PermissionException)
        {
            // No maps app / disabled feature — nothing to recover, and nothing worth interrupting the page for.
        }
    }
}

/// <summary>
/// <see cref="ILocationService"/> over Essentials Geolocation. Permission is requested
/// just-in-time, "while using" only, never background (PRD privacy posture); denial returns null
/// and callers degrade gracefully (site search without distance, no auto-match).
/// </summary>
public sealed class MauiLocationService : ILocationService
{
    public async Task<GeoLocation?> GetCurrentLocationAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var status = await MainThread.InvokeOnMainThreadAsync(
                Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>);
            if (status != PermissionStatus.Granted)
            {
                status = await MainThread.InvokeOnMainThreadAsync(
                    Permissions.RequestAsync<Permissions.LocationWhenInUse>);
            }

            if (status != PermissionStatus.Granted)
            {
                return null;
            }

            var location =
                await Geolocation.Default.GetLocationAsync(
                    new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(10)),
                    cancellationToken)
                ?? await Geolocation.Default.GetLastKnownLocationAsync();
            return location is null ? null : new GeoLocation(location.Latitude, location.Longitude);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null; // Location unavailable (no provider, denied, airplane mode) — never fatal.
        }
    }
}

/// <summary><see cref="IAppInfo"/> from device + package facts, stamped onto every analytics event.</summary>
public sealed class MauiAppInfoService : Trimix.Core.Abstractions.IAppInfo
{
    public string Platform =>
        DeviceInfo.Current.Platform == DevicePlatform.Android ? "android"
        : DeviceInfo.Current.Platform == DevicePlatform.iOS ? "ios"
        : DeviceInfo.Current.Platform.ToString().ToLowerInvariant();

    public string AppVersion => Microsoft.Maui.ApplicationModel.AppInfo.Current.VersionString;
}
