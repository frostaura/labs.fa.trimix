using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Trimix.Api.Analytics;
using Trimix.Api.Auth;
using Trimix.Api.Data;
using Trimix.Contracts;
using Trimix.Contracts.Dtos;

namespace Trimix.Api.Endpoints;

/// <summary>
/// POST /auth/register, POST /auth/login (architecture overview §8): email+password via ASP.NET
/// Core PasswordHasher (PBKDF2), HS256 JWT (30-day, no refresh tokens). Registration server-logs
/// the frozen <c>account_created</c> funnel event.
/// </summary>
public static class AuthEndpoints
{
    private const int MinPasswordLength = 8;

    /// <summary><c>auth_provider</c> value for email+password accounts (the slice's only provider).</summary>
    public const string EmailAuthProvider = "email";

    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup(ApiRoutes.Auth.Group);

        group.MapPost(ApiRoutes.Auth.Register, async (
            RegisterRequest request,
            AppDbContext db,
            IPasswordHasher<User> passwordHasher,
            TokenService tokenService,
            ServerAnalytics serverAnalytics,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var email = request.Email?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(email) || !email.Contains('@') || email.Length > 320)
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, detail: "A valid email is required.");
            }

            if (string.IsNullOrEmpty(request.Password) || request.Password.Length < MinPasswordLength)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    detail: $"Password must be at least {MinPasswordLength} characters.");
            }

            if (await db.Users.AnyAsync(u => u.Email == email, cancellationToken))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    detail: "An account with this email already exists.");
            }

            var user = new User
            {
                Id = Guid.NewGuid(),
                Email = email,
                PasswordHash = string.Empty,
                Role = UserRoles.User,
                CreatedAtUtc = DateTime.UtcNow,
                Verified = false,
            };
            user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
            db.Users.Add(user);

            serverAnalytics.Emit(
                FunnelEvents.AccountCreated,
                new Dictionary<string, string> { [FunnelEvents.Props.AuthProvider] = EmailAuthProvider },
                ServerAnalytics.ResolvePseudoId(httpContext, user.Id),
                user.Id);

            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Two concurrent registrations for the same email both passed the AnyAsync check; the
                // unique-index violation becomes the designed 409 instead of a 500.
                if (await db.Users.AnyAsync(u => u.Email == email, cancellationToken))
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status409Conflict,
                        detail: "An account with this email already exists.");
                }

                throw;
            }

            return Results.Ok(new AuthResponse(tokenService.IssueToken(user), ToDto(user)));
        });

        group.MapPost(ApiRoutes.Auth.Login, async (
            LoginRequest request,
            AppDbContext db,
            IPasswordHasher<User> passwordHasher,
            TokenService tokenService,
            CancellationToken cancellationToken) =>
        {
            var email = request.Email?.Trim().ToLowerInvariant();
            var user = string.IsNullOrWhiteSpace(email)
                ? null
                : await db.Users.SingleOrDefaultAsync(u => u.Email == email, cancellationToken);
            if (user is null || string.IsNullOrEmpty(user.PasswordHash))
            {
                // No account, or a passkey-only account that has no password to verify against.
                return Results.Unauthorized();
            }

            var result = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password ?? string.Empty);
            if (result == PasswordVerificationResult.Failed)
            {
                return Results.Unauthorized();
            }

            return Results.Ok(new AuthResponse(tokenService.IssueToken(user), ToDto(user)));
        }).RequireRateLimiting(RateLimitPolicies.Login);

        return api;
    }

    private static UserDto ToDto(User user) => new(user.Id, user.Email, user.Role, user.Verified, user.CreatedAtUtc);
}
