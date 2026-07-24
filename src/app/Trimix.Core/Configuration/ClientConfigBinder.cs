using System.Text.Json;

namespace Trimix.Core.Configuration;

/// <summary>
/// Pure, host-independent parser from the raw <c>appsettings.json</c> asset text to a
/// <see cref="TrimixClientConfig"/> (aggregator architecture §9.4). Kept in Core (not the App) so the
/// binding rules are unit-testable without a MAUI host, and so the App's <c>AppConfigLoader</c> stays a
/// thin file-read shim.
///
/// <para>Robustness contract: parsing NEVER throws. Missing keys fall back to the POCO defaults, unknown
/// keys are ignored (web serializer default), JSON <c>//</c> and <c>/* */</c> comments + trailing commas
/// are tolerated so the shipped asset can document its deploy inputs inline, and a null/empty/malformed
/// body yields a fully-defaulted config — a wrong or absent config asset must never brick boot.</para>
/// </summary>
public static class ClientConfigBinder
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Parses config JSON to a <see cref="TrimixClientConfig"/>; null/empty/malformed ⇒ defaults.</summary>
    public static TrimixClientConfig Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new TrimixClientConfig();
        }

        try
        {
            return JsonSerializer.Deserialize<TrimixClientConfig>(json, Options) ?? new TrimixClientConfig();
        }
        catch (JsonException)
        {
            // A malformed shipped asset must degrade to safe defaults, never crash startup.
            return new TrimixClientConfig();
        }
    }
}
