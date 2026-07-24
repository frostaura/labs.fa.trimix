using Microsoft.Maui.Storage;
using Trimix.Core.Configuration;

namespace Trimix.App.Services;

/// <summary>
/// Thin App-side loader for the external client config (aggregator architecture §9.4). Reads the shipped
/// <c>Resources/Raw/appsettings.json</c> MauiAsset (logical name <c>appsettings.json</c>, covered by the
/// existing <c>Resources\Raw\**</c> glob — no <c>.csproj</c> edit) and delegates parsing to Core's
/// <see cref="ClientConfigBinder"/>. The deployer edits that asset to supply real values (API base URL,
/// legal-cleared connector endpoints, hardware-verified BLE UUID overrides, family switches) with no code
/// change.
///
/// <para><b>Never throws at boot:</b> a missing/unreadable asset yields a fully-defaulted config, so
/// <c>MauiProgram</c> can call <c>AppConfigLoader.LoadAsync().GetAwaiter().GetResult()</c> at the top of
/// <c>CreateMauiApp</c> and feed the result into <c>AddTrimixCore</c> + the connector option objects.</para>
/// </summary>
public static class AppConfigLoader
{
    /// <summary>Logical name of the bundled config asset (see the <c>Resources\Raw\**</c> MauiAsset glob).</summary>
    public const string AssetFileName = "appsettings.json";

    /// <summary>Loads + parses the config asset; missing/unreadable/malformed ⇒ safe defaults, never throws.</summary>
    public static async Task<TrimixClientConfig> LoadAsync()
    {
        try
        {
            using var stream = await FileSystem.OpenAppPackageFileAsync(AssetFileName).ConfigureAwait(false);
            using var reader = new StreamReader(stream);
            var json = await reader.ReadToEndAsync().ConfigureAwait(false);
            return ClientConfigBinder.Parse(json);
        }
        catch (Exception)
        {
            // No asset bundled (or unreadable) → boot on the built-in defaults rather than crashing.
            return new TrimixClientConfig();
        }
    }
}
