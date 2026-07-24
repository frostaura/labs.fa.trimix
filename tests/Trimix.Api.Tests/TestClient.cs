using System.Net.Http.Headers;
using System.Net.Http.Json;
using Trimix.Api.Analytics;
using Trimix.Api.Data;
using Trimix.Contracts;
using Trimix.Contracts.Dtos;
using Xunit;

namespace Trimix.Api.Tests;

/// <summary>Client-side conveniences shared by the integration tests.</summary>
public static class TestClient
{
    public const string DefaultPassword = "Correct-Horse-9!";
    public const string TestPlatform = "android";
    public const string TestAppVersion = "0.1.0-test";

    /// <summary>Creates an anonymous client, optionally declaring the analytics pseudo-id header.</summary>
    public static HttpClient CreateAnonymousClient(this TrimixApiFactory factory, string? pseudoId = null)
    {
        var client = factory.CreateClient();
        if (pseudoId is not null)
        {
            client.DefaultRequestHeaders.Add(Trimix.Contracts.TrimixHeaders.UserPseudoId, pseudoId);
        }

        return client;
    }

    /// <summary>Registers a fresh account and returns an authenticated client.</summary>
    public static async Task<(HttpClient Client, AuthResponse Auth)> RegisterAsync(
        this TrimixApiFactory factory, string email, string? pseudoId = null, string password = DefaultPassword)
    {
        var client = factory.CreateAnonymousClient(pseudoId);
        var auth = await client.RegisterAsync(email, password);
        return (client, auth);
    }

    /// <summary>Registers on an existing client (keeps its headers) and attaches the bearer token.</summary>
    public static async Task<AuthResponse> RegisterAsync(
        this HttpClient client, string email, string password = DefaultPassword)
    {
        var response = await client.PostAsJsonAsync(ApiRoutes.Auth.RegisterFull, new RegisterRequest(email, password));
        response.EnsureSuccessStatusCode();
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(auth);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        return auth;
    }

    /// <summary>Logs in on an existing client (keeps its headers) and attaches the bearer token.</summary>
    public static async Task<AuthResponse> LoginAsync(
        this HttpClient client, string email, string password = DefaultPassword)
    {
        var response = await client.PostAsJsonAsync(ApiRoutes.Auth.LoginFull, new LoginRequest(email, password));
        response.EnsureSuccessStatusCode();
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(auth);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        return auth;
    }

    /// <summary>Logs in as the dev-seeded moderator.</summary>
    public static async Task<HttpClient> ModeratorClientAsync(this TrimixApiFactory factory)
    {
        var client = factory.CreateClient();
        await client.LoginAsync(DevSeeder.ModeratorEmail, DevSeeder.ModeratorPassword);
        return client;
    }

    /// <summary>Posts a batch of client-emitted funnel events and asserts acceptance.</summary>
    public static async Task PostEventsAsync(
        this HttpClient client, string pseudoId, params (string Name, Dictionary<string, string> Props)[] events)
    {
        var batch = new AnalyticsBatchRequest(events
            .Select(e => new AnalyticsEventDto(e.Name, e.Props, pseudoId, DateTime.UtcNow, TestPlatform, TestAppVersion))
            .ToList());
        var response = await client.PostAsJsonAsync(ApiRoutes.Analytics.EventsFull, batch);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Validates a purchase receipt against the server.</summary>
    public static async Task<HttpResponseMessage> ValidatePurchaseAsync(
        this HttpClient client, string platform, string skuId, string token) =>
        await client.PostAsJsonAsync(ApiRoutes.Purchases.ValidateFull, new ValidatePurchaseRequest(platform, skuId, token));

    /// <summary>The server's authoritative tier for the authenticated client.</summary>
    public static async Task<EntitlementTier> GetTierAsync(this HttpClient client)
    {
        var response = await client.GetAsync(ApiRoutes.Entitlements.MeFull);
        response.EnsureSuccessStatusCode();
        var entitlement = await response.Content.ReadFromJsonAsync<EntitlementResponse>();
        Assert.NotNull(entitlement);
        return entitlement.Tier;
    }

    /// <summary>
    /// Ordered event names recorded for one pseudo-user, ordered by the event's real emit time
    /// (<c>ClientTimestampUtc</c> — stamped in the client viewmodel when the event occurs, and at
    /// server-processing time for server-emitted rows), with DB <c>Id</c> as the tiebreak. This is
    /// the reconciliation ordering the funnel gate must use: it reflects true event chronology and is
    /// therefore reproducible under the shipped queue-then-flush pipeline, where a batch of client
    /// events may be inserted AFTER a server-emitted row it actually preceded.
    /// </summary>
    public static IReadOnlyList<string> EventNamesFor(this TrimixApiFactory factory, string pseudoId) =>
        factory.WithDb(db => db.AnalyticsEvents
            .Where(e => e.UserPseudoId == pseudoId)
            .OrderBy(e => e.ClientTimestampUtc)
            .ThenBy(e => e.Id)
            .Select(e => e.Name)
            .ToList());

    /// <summary>Asserts that <paramref name="spine"/> occurs within <paramref name="actual"/> in order.</summary>
    public static void AssertOrderedSubsequence(IReadOnlyList<string> spine, IReadOnlyList<string> actual)
    {
        var i = 0;
        foreach (var name in actual)
        {
            if (i < spine.Count && name == spine[i])
            {
                i++;
            }
        }

        Assert.True(
            i == spine.Count,
            $"Expected ordered spine [{string.Join(" → ", spine)}] within [{string.Join(" → ", actual)}]; matched {i}/{spine.Count}.");
    }
}
