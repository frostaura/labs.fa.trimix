using Trimix.Core.Connectors.Cloud;
using Trimix.Core.Connectors.Infra;
using Xunit;

namespace Trimix.Core.Tests.Connectors.Cloud;

/// <summary>
/// The shared reverse-engineered MySSI (DiveSSI) READ client — the single corridor feeding BOTH the Mares
/// and ScubaPro sources. Fully offline against recorded fixtures on <see cref="RecordedHttpTransport"/>:
/// login → token (returned, never stored here — persistence is the calling connector's job), vendor-filterable
/// logbook list, dive detail → <c>ImportedDive</c>, and config-override of the researched-guess endpoints.
/// </summary>
public class MySsiCloudClientTests
{
    private readonly RecordedHttpTransport _http = new();

    private MySsiCloudClient Create(CloudSourceOptions? options = null) => new(_http, options);

    /// <summary>Recorded three-dive logbook: one Mares, one ScubaPro (via LogTRAK), one Shearwater.</summary>
    private const string LogbookJson =
        "{\"dives\":[" +
        "{\"dive_id\":101,\"datetime\":\"2026-06-01T08:00:00Z\",\"divetime\":40,\"brand\":\"Mares\"}," +
        "{\"dive_id\":102,\"datetime\":\"2026-06-02T09:30:00Z\",\"divetime\":35,\"brand\":\"SCUBAPRO (LogTRAK)\"}," +
        "{\"dive_id\":103,\"datetime\":\"2026-06-03T10:15:00Z\",\"divetime\":50,\"brand\":\"Shearwater\"}]}";

    [Fact]
    public async Task Authenticate_returns_the_exchanged_token()
    {
        _http.EnqueueText("{\"token\":\"myssi-tok-1\"}");

        var token = await Create().AuthenticateAsync("diver@example.com", "myssi-pass");

        Assert.Equal("myssi-tok-1", token);
        var request = Assert.Single(_http.Requests);
        // Researched wire shape: the private DiveSSI cloud takes l/p query params on the login endpoint.
        Assert.StartsWith("https://api.divessi.com/1/API/login?", request.Url, StringComparison.Ordinal);
        Assert.Contains("l=diver%40example.com", request.Url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Authenticate_failure_throws_ConnectorAuthException()
    {
        _http.EnqueueText("{\"error\":\"bad credentials\"}", status: 401);

        await Assert.ThrowsAsync<ConnectorAuthException>(
            () => Create().AuthenticateAsync("diver@example.com", "wrong"));
    }

    [Fact]
    public async Task List_parses_the_logbook_and_maps_minutes_to_seconds()
    {
        _http.EnqueueText(LogbookJson);

        var dives = await Create().ListDivesAsync("myssi-tok-1");

        Assert.Equal(3, dives.Count);
        Assert.Equal("101", dives[0].Id);
        Assert.Equal(new DateTime(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc), dives[0].StartUtc);
        Assert.Equal(40 * 60, dives[0].DurationSec); // divetime is minutes (SSI summary convention)
        Assert.Equal("Mares", dives[0].ComputerBrand);
        var request = Assert.Single(_http.Requests);
        Assert.Equal("Bearer myssi-tok-1", request.Headers["Authorization"]);
    }

    [Fact]
    public async Task List_filters_by_vendor_brand_aliases_case_insensitively()
    {
        _http.EnqueueText(LogbookJson);
        var mares = await Create().ListDivesAsync("t", brandAliases: ["mares"]);
        Assert.Equal("101", Assert.Single(mares).Id);

        _http.EnqueueText(LogbookJson);
        var scubapro = await Create().ListDivesAsync("t", brandAliases: ["scubapro", "uwatec", "logtrak"]);
        Assert.Equal("102", Assert.Single(scubapro).Id);

        _http.EnqueueText(LogbookJson);
        var all = await Create().ListDivesAsync("t"); // no filter ⇒ everything
        Assert.Equal(3, all.Count);
    }

    [Fact]
    public async Task List_since_cursor_is_forwarded_as_a_query_parameter()
    {
        _http.EnqueueText("{\"dives\":[]}");
        var since = new DateTime(2026, 6, 2, 0, 0, 0, DateTimeKind.Utc);

        await Create().ListDivesAsync("t", since);

        Assert.Contains("since=2026-06-02T00%3A00%3A00.0000000Z", _http.Requests.Single().Url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Fetch_maps_the_dive_record_to_an_ImportedDive_with_profile_and_cylinders()
    {
        _http.EnqueueText(
            "{\"dive_id\":101,\"datetime\":\"2026-06-01T08:00:00Z\",\"divetime\":40,\"depth_m\":28.5," +
            "\"avg_depth_m\":16.2,\"temp_c\":21.0,\"gasmix\":\"EAN32\",\"brand\":\"Mares\"," +
            "\"profile\":[{\"t\":0,\"depth_m\":0},{\"t\":60,\"depth_m\":12.4,\"temp_c\":22.5},{\"t\":120,\"depth_m\":28.5,\"temp_c\":21.0}]," +
            "\"cylinders\":[{\"o2\":32,\"start_bar\":200,\"end_bar\":70,\"volume_l\":12}]}");

        var dive = await Create().FetchDiveAsync("myssi-tok-1", "101");

        Assert.Equal(new DateTime(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc), dive.StartUtc);
        Assert.Equal(2400, dive.DurationSec);
        Assert.Equal(28.5, dive.MaxDepthM);
        Assert.Equal(16.2, dive.AvgDepthM);
        Assert.Equal(21.0, dive.MinTempC);
        Assert.Equal("EAN32", dive.GasMix);
        Assert.Equal(3, dive.Samples.Count);
        Assert.Equal(12.4, dive.Samples[1].DepthM);
        Assert.Equal(22.5, dive.Samples[1].TempC);
        var cylinder = Assert.Single(dive.Cylinders!);
        Assert.Equal(32, cylinder.O2);
        Assert.Equal(200, cylinder.StartBar);
        Assert.Equal("https://api.divessi.com/1/API/logbook/101", _http.Requests.Single().Url);
    }

    [Fact]
    public async Task Configured_overrides_replace_the_researched_default_endpoints()
    {
        var options = new CloudSourceOptions
        {
            MySsiLoginUrl = "https://override.example/myssi/login",
            MySsiDivesUrl = "https://override.example/myssi/logbook",
        };
        _http.EnqueueText("{\"token\":\"tok\"}").EnqueueText("{\"dives\":[]}");
        var client = Create(options);

        await client.AuthenticateAsync("u", "p");
        await client.ListDivesAsync("tok");

        Assert.StartsWith("https://override.example/myssi/login?", _http.Requests[0].Url, StringComparison.Ordinal);
        Assert.Equal("https://override.example/myssi/logbook", _http.Requests[1].Url);
    }
}
