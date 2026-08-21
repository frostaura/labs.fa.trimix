namespace Trimix.Core.Abstractions;

/// <summary>
/// Hands a coordinate to the device's own maps application — Apple Maps on iOS, Google Maps on Android.
///
/// <para>A port rather than a direct call because <c>Trimix.Core</c> has no MAUI dependency and must keep it
/// that way. The App supplies the Essentials-backed implementation; Core ships a no-op default so tests and any
/// headless host resolve cleanly (the same shape as <see cref="ILocationService"/>).</para>
///
/// <para>Deliberately the OS maps app rather than an embedded Android map: an embedded Google map would require
/// a Maps SDK API key shipped inside the APK, a billable Google Cloud project, and a secret to manage in a repo
/// that has neither. Handing off costs nothing and is still a native map.</para>
/// </summary>
public interface IMapLauncher
{
    /// <summary>
    /// Opens the platform maps app at the given coordinate. <paramref name="label"/> names the dropped pin.
    /// Failures are swallowed by the implementation — being unable to open a map must never break the page.
    /// </summary>
    Task OpenAsync(double latitude, double longitude, string? label = null, CancellationToken ct = default);
}
