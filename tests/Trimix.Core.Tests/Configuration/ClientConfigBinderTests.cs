using Trimix.Core.Configuration;
using Xunit;

namespace Trimix.Core.Tests.Configuration;

/// <summary>
/// The client-config parser (aggregator architecture §9.4): full/partial/empty/malformed JSON binds to
/// the right values with safe defaults, unknown keys are ignored, comments + trailing commas are tolerated,
/// and NO parse path throws — a wrong or absent config asset must never brick boot.
/// </summary>
public class ClientConfigBinderTests
{
    [Fact]
    public void Full_json_binds_every_field()
    {
        const string json = """
        {
          "Api": { "BaseUrl": "https://api.example.test" },
          "Connectors": {
            "Padi": { "AuthUrl": "https://padi.test/auth", "PushUrl": "https://padi.test/push" },
            "Ssi":  { "AuthUrl": "https://ssi.test/auth",  "PushUrl": "https://ssi.test/push" },
            "Cloud": {
              "Garmin":  { "AuthUrl": "https://g/auth", "TokenUrl": "https://g/tok", "ListUrl": "https://g/list", "DownloadUrl": "https://g/dl" },
              "Suunto":  { "AuthUrl": "https://s/auth" },
              "Oceanic": { "DownloadUrl": "https://o/dl" }
            }
          },
          "Ble": {
            "Shearwater": { "ServiceUuid": "svc", "WriteCharUuid": "w", "NotifyCharUuid": "n", "HandshakeCommandsHex": "a5c2" }
          },
          "Features": { "EnableCloudConnectors": true, "EnablePushConnectors": true }
        }
        """;

        var config = ClientConfigBinder.Parse(json);

        Assert.Equal("https://api.example.test", config.Api.BaseUrl);

        Assert.Equal("https://padi.test/auth", config.Connectors.Padi.AuthUrl);
        Assert.Equal("https://padi.test/push", config.Connectors.Padi.PushUrl);
        Assert.Equal("https://ssi.test/auth", config.Connectors.Ssi.AuthUrl);
        Assert.Equal("https://ssi.test/push", config.Connectors.Ssi.PushUrl);

        Assert.Equal("https://g/auth", config.Connectors.Cloud.Garmin.AuthUrl);
        Assert.Equal("https://g/tok", config.Connectors.Cloud.Garmin.TokenUrl);
        Assert.Equal("https://g/list", config.Connectors.Cloud.Garmin.ListUrl);
        Assert.Equal("https://g/dl", config.Connectors.Cloud.Garmin.DownloadUrl);
        Assert.Equal("https://s/auth", config.Connectors.Cloud.Suunto.AuthUrl);
        Assert.Null(config.Connectors.Cloud.Suunto.DownloadUrl);
        Assert.Equal("https://o/dl", config.Connectors.Cloud.Oceanic.DownloadUrl);

        var ble = Assert.Contains("Shearwater", config.Ble);
        Assert.Equal("svc", ble.ServiceUuid);
        Assert.Equal("w", ble.WriteCharUuid);
        Assert.Equal("n", ble.NotifyCharUuid);
        Assert.Equal("a5c2", ble.HandshakeCommandsHex);

        Assert.True(config.Features.EnableCloudConnectors);
        Assert.True(config.Features.EnablePushConnectors);
    }

    [Fact]
    public void Partial_json_keeps_defaults_for_missing_keys()
    {
        // Only the API base is set; every other subtree must fall back to its safe default.
        var config = ClientConfigBinder.Parse("""{ "Api": { "BaseUrl": "https://only-api.test" } }""");

        Assert.Equal("https://only-api.test", config.Api.BaseUrl);
        Assert.NotNull(config.Connectors);
        Assert.Null(config.Connectors.Padi.AuthUrl);
        Assert.Null(config.Connectors.Padi.PushUrl);
        Assert.Null(config.Connectors.Cloud.Garmin.AuthUrl);
        Assert.Empty(config.Ble);
        Assert.False(config.Features.EnableCloudConnectors);
        Assert.False(config.Features.EnablePushConnectors);
    }

    [Fact]
    public void Empty_object_yields_all_defaults()
    {
        var config = ClientConfigBinder.Parse("{}");

        Assert.Null(config.Api.BaseUrl);
        Assert.Null(config.Connectors.Ssi.PushUrl);
        Assert.Empty(config.Ble);
        Assert.False(config.Features.EnableCloudConnectors);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Null_or_blank_yields_defaults_without_throwing(string? json)
    {
        var config = ClientConfigBinder.Parse(json);

        Assert.NotNull(config);
        Assert.Null(config.Api.BaseUrl);
        Assert.Empty(config.Ble);
        Assert.False(config.Features.EnablePushConnectors);
    }

    [Fact]
    public void Malformed_json_degrades_to_defaults_and_never_throws()
    {
        var config = ClientConfigBinder.Parse("{ \"Api\": { \"BaseUrl\": ");

        Assert.Null(config.Api.BaseUrl);
        Assert.Empty(config.Ble);
    }

    [Fact]
    public void Unknown_keys_are_ignored()
    {
        const string json = """
        {
          "Api": { "BaseUrl": "https://api.test", "SomethingUnknown": 42 },
          "TotallyUnknownSection": { "x": true }
        }
        """;

        var config = ClientConfigBinder.Parse(json);

        Assert.Equal("https://api.test", config.Api.BaseUrl);
    }

    [Fact]
    public void Comments_and_trailing_commas_are_tolerated()
    {
        // The shipped Resources/Raw/appsettings.json documents deploy inputs with // comments — the parser
        // must accept them (and trailing commas) so the real asset round-trips.
        const string json = """
        {
          // deploy input
          "Api": { "BaseUrl": "https://commented.test" }, // note
          "Features": { "EnableCloudConnectors": true, }
        }
        """;

        var config = ClientConfigBinder.Parse(json);

        Assert.Equal("https://commented.test", config.Api.BaseUrl);
        Assert.True(config.Features.EnableCloudConnectors);
    }

    [Fact]
    public void Property_names_are_case_insensitive()
    {
        var config = ClientConfigBinder.Parse("""{ "api": { "baseUrl": "https://lower.test" } }""");

        Assert.Equal("https://lower.test", config.Api.BaseUrl);
    }
}
