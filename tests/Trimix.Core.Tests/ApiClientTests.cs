using System.Net;
using System.Text;
using Trimix.Contracts;
using Trimix.Contracts.Dtos;
using Trimix.Core.Analytics;
using Trimix.Core.Api;
using Trimix.Core.Platform;
using Xunit;

namespace Trimix.Core.Tests;

/// <summary>ApiClient behaviors: bearer attach, retry-once on transient failure, typed error envelope, route shape.</summary>
public class ApiClientTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, int, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(responder(request, Requests.Count));
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private readonly InMemorySecureStore _secureStore = new();
    private readonly UserPseudoIdProvider _pseudoIds = new(new InMemoryPreferences());

    private ApiClient CreateClient(StubHandler handler) => new(
        new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5210") },
        _secureStore,
        _pseudoIds);

    [Fact]
    public async Task Attaches_bearer_token_when_signed_in()
    {
        await _secureStore.SetAsync(SecureStoreKeys.AuthToken, "jwt-token-123");
        var handler = new StubHandler((_, _) => Json(HttpStatusCode.OK, """{"tier":"pro"}"""));
        var client = CreateClient(handler);

        var response = await client.GetMyEntitlementAsync();

        Assert.Equal(EntitlementTier.Pro, response.Tier);
        var auth = Assert.Single(handler.Requests).Headers.Authorization;
        Assert.Equal("Bearer", auth!.Scheme);
        Assert.Equal("jwt-token-123", auth.Parameter);
    }

    [Fact]
    public async Task Reads_enum_tier_serialized_as_number_too()
    {
        var handler = new StubHandler((_, _) => Json(HttpStatusCode.OK, """{"tier":2}"""));
        var response = await CreateClient(handler).GetMyEntitlementAsync();
        Assert.Equal(EntitlementTier.Lifetime, response.Tier);
    }

    [Fact]
    public async Task No_token_sends_anonymous_request()
    {
        var handler = new StubHandler((_, _) => Json(HttpStatusCode.OK, """{"sites":[]}"""));
        await CreateClient(handler).SearchSitesAsync(null, null, null, null);
        Assert.Null(Assert.Single(handler.Requests).Headers.Authorization);
    }

    [Fact]
    public async Task Stamps_the_device_pseudo_id_header_on_every_request()
    {
        // Server-emitted funnel events (entitlement_granted, ...) must reconcile to the device
        // pseudo-id; ServerAnalytics reads it from this header, so the real client must always send it.
        var handler = new StubHandler((_, _) => Json(HttpStatusCode.OK, """{"tier":"free"}"""));
        var client = CreateClient(handler);

        await client.GetMyEntitlementAsync();

        var header = Assert.Single(handler.Requests).Headers.GetValues(TrimixHeaders.UserPseudoId).Single();
        Assert.Equal(_pseudoIds.UserPseudoId, header);
        Assert.False(string.IsNullOrEmpty(header));
    }

    [Fact]
    public async Task Retries_exactly_once_on_5xx_then_succeeds()
    {
        var handler = new StubHandler((_, attempt) => attempt == 1
            ? Json(HttpStatusCode.InternalServerError, "{}")
            : Json(HttpStatusCode.OK, """{"tier":"free"}"""));
        var client = CreateClient(handler);

        var response = await client.GetMyEntitlementAsync();

        Assert.Equal(EntitlementTier.Free, response.Tier);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Persistent_5xx_fails_after_the_single_retry()
    {
        var handler = new StubHandler((_, _) => Json(HttpStatusCode.ServiceUnavailable, "{}"));
        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<ApiException>(() => client.GetMyEntitlementAsync());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
        Assert.Equal(2, handler.Requests.Count); // one retry, not more
    }

    [Fact]
    public async Task Non_transient_error_is_not_retried_and_carries_the_typed_envelope()
    {
        var handler = new StubHandler((_, _) =>
            Json(HttpStatusCode.Unauthorized, """{"code":"invalid_credentials","message":"Wrong email or password."}"""));
        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<ApiException>(
            () => client.LoginAsync(new LoginRequest("d@x.com", "nope")));

        Assert.Single(handler.Requests);
        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
        Assert.Equal("invalid_credentials", ex.Error.Code);
        Assert.Equal("Wrong email or password.", ex.Error.Message);
    }

    [Fact]
    public async Task Problem_details_bodies_map_into_the_envelope()
    {
        var handler = new StubHandler((_, _) =>
            Json(HttpStatusCode.BadRequest, """{"title":"Bad Request","detail":"The email is already registered."}"""));
        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<ApiException>(
            () => client.RegisterAsync(new RegisterRequest("d@x.com", "pw")));

        Assert.Equal("Bad Request", ex.Error.Code);
        Assert.Equal("The email is already registered.", ex.Error.Message);
    }

    [Fact]
    public async Task Non_json_error_bodies_fall_back_to_the_http_status()
    {
        var handler = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("<html>nope</html>", Encoding.UTF8, "text/html"),
        });

        var ex = await Assert.ThrowsAsync<ApiException>(
            () => CreateClient(handler).GetSiteAsync(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
        Assert.Equal("404", ex.Error.Code);
    }

    [Fact]
    public async Task Site_search_builds_the_documented_query_string()
    {
        var handler = new StubHandler((_, _) => Json(HttpStatusCode.OK, """{"sites":[]}"""));
        var client = CreateClient(handler);

        await client.SearchSitesAsync("blue hole", -34.5, 18.25, 5);

        var uri = Assert.Single(handler.Requests).RequestUri!;
        Assert.Equal(ApiRoutes.Sites.SearchFull, uri.AbsolutePath);
        Assert.Equal("?query=blue%20hole&lat=-34.5&lng=18.25&limit=5", uri.Query);
    }

    [Fact]
    public async Task Routes_come_from_the_contracts_constants()
    {
        var handler = new StubHandler((_, _) => Json(HttpStatusCode.OK, """{"accepted":0}"""));
        var client = CreateClient(handler);

        await client.SendAnalyticsAsync(new AnalyticsBatchRequest([]));

        Assert.Equal(ApiRoutes.Analytics.EventsFull, Assert.Single(handler.Requests).RequestUri!.AbsolutePath);
    }
}
