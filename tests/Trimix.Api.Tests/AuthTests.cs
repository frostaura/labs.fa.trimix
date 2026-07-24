using System.Net;
using System.Net.Http.Json;
using Trimix.Contracts;
using Trimix.Contracts.Dtos;
using Xunit;

namespace Trimix.Api.Tests;

/// <summary>Auth roundtrip: register → token works → login again; failure modes.</summary>
public sealed class AuthTests : IDisposable
{
    private readonly TrimixApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Register_then_login_roundtrip_yields_working_tokens()
    {
        var (client, registered) = await _factory.RegisterAsync("diver@example.com");

        Assert.Equal("diver@example.com", registered.User.Email);
        Assert.Equal(UserRoles.User, registered.User.Role);
        Assert.False(string.IsNullOrWhiteSpace(registered.Token));

        // The registration token is accepted by an authenticated endpoint.
        Assert.Equal(EntitlementTier.Free, await client.GetTierAsync());

        // Login issues a fresh working token.
        var fresh = _factory.CreateClient();
        var loggedIn = await fresh.LoginAsync("diver@example.com");
        Assert.Equal(registered.User.Id, loggedIn.User.Id);
        Assert.Equal(EntitlementTier.Free, await fresh.GetTierAsync());
    }

    [Fact]
    public async Task Duplicate_email_registration_conflicts()
    {
        await _factory.RegisterAsync("dup@example.com");

        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            ApiRoutes.Auth.RegisterFull, new RegisterRequest("dup@example.com", TestClient.DefaultPassword));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Wrong_password_and_unknown_user_are_unauthorized()
    {
        await _factory.RegisterAsync("locked@example.com");
        var client = _factory.CreateClient();

        var wrongPassword = await client.PostAsJsonAsync(
            ApiRoutes.Auth.LoginFull, new LoginRequest("locked@example.com", "not-the-password-1!"));
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);

        var unknownUser = await client.PostAsJsonAsync(
            ApiRoutes.Auth.LoginFull, new LoginRequest("ghost@example.com", TestClient.DefaultPassword));
        Assert.Equal(HttpStatusCode.Unauthorized, unknownUser.StatusCode);
    }

    [Fact]
    public async Task Weak_password_is_rejected()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            ApiRoutes.Auth.RegisterFull, new RegisterRequest("weak@example.com", "short1!"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Healthz_reports_ok_with_db()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(ApiRoutes.HealthzFull);
        response.EnsureSuccessStatusCode();
        var health = await response.Content.ReadFromJsonAsync<HealthzResponse>();
        Assert.NotNull(health);
        Assert.Equal("ok", health.Status);
        Assert.Equal("ok", health.Db);
    }
}
