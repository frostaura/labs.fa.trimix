using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Trimix.Api.Data;

namespace Trimix.Api.Auth;

/// <summary>JWT options (HS256; architecture overview §8).</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>Minimum HS256 key length in bytes (256-bit). Enforced at startup in Program.cs.</summary>
    public const int MinSecretBytes = 32;

    /// <summary>HS256 signing secret, ≥32 bytes. Dev value in appsettings.Development.json; env-provided in production.</summary>
    public string Secret { get; set; } = string.Empty;

    public string Issuer { get; set; } = "trimix-api";
    public string Audience { get; set; } = "trimix-app";

    /// <summary>30-day access token — slice simplicity, no refresh tokens (architecture overview §8).</summary>
    public int LifetimeDays { get; set; } = 30;
}

/// <summary>Claim type names used in Trimix JWTs (inbound claim mapping is disabled).</summary>
public static class TrimixClaims
{
    public const string Subject = JwtRegisteredClaimNames.Sub;
    public const string Email = JwtRegisteredClaimNames.Email;
    public const string Role = "role";
}

/// <summary>Issues HS256 bearer tokens with 30-day lifetime and roles user/moderator.</summary>
public sealed class TokenService(JwtOptions options)
{
    private readonly SigningCredentials _credentials = new(
        new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Secret)),
        SecurityAlgorithms.HmacSha256);

    public string IssueToken(User user)
    {
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims:
            [
                new Claim(TrimixClaims.Subject, user.Id.ToString()),
                new Claim(TrimixClaims.Email, user.Email),
                new Claim(TrimixClaims.Role, user.Role),
            ],
            notBefore: now,
            expires: now.AddDays(options.LifetimeDays),
            signingCredentials: _credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

/// <summary>Authorization policy names.</summary>
public static class AuthPolicies
{
    public const string Moderator = "moderator";
}

/// <summary>Helpers to read the authenticated user from the request principal.</summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>The authenticated user id, or null when the request is anonymous.</summary>
    public static Guid? UserIdOrNull(this ClaimsPrincipal principal)
    {
        var sub = principal.FindFirstValue(TrimixClaims.Subject);
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    /// <summary>The authenticated user id; throws if the endpoint let an anonymous request through.</summary>
    public static Guid RequireUserId(this ClaimsPrincipal principal) =>
        principal.UserIdOrNull() ?? throw new InvalidOperationException(
            "Endpoint requires authorization but no subject claim is present.");
}
