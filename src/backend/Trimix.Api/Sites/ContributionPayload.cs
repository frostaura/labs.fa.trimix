using System.Text.Json;

namespace Trimix.Api.Sites;

/// <summary>
/// Server-defined shape of <c>SiteContributionRequest.PayloadJson</c>. All fields optional; on
/// moderator approval every non-null field is applied to the target site (or a new site is created
/// when the contribution proposes one — Name/Lat/Lng required then). <see cref="CommunityNote"/>
/// becomes an approved community note on the site's detail view.
/// </summary>
public sealed record ContributionPayload(
    string? Name,
    double? Lat,
    double? Lng,
    double? DepthMinM,
    double? DepthMaxM,
    string? Type,
    string? HazardsCsv,
    string? MarineLifeCsv,
    string? AccessNotes,
    string? CommunityNote)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Parses payload JSON; returns false when the JSON is malformed.</summary>
    public static bool TryParse(string payloadJson, out ContributionPayload? payload)
    {
        payload = null;
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return false;
        }

        try
        {
            payload = JsonSerializer.Deserialize<ContributionPayload>(payloadJson, JsonOptions);
            return payload is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
