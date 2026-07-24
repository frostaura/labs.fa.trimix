namespace Trimix.Contracts.Dtos;

/// <summary><c>POST /auth/register</c> request.</summary>
public sealed record RegisterRequest(string Email, string Password);

/// <summary><c>POST /auth/login</c> request.</summary>
public sealed record LoginRequest(string Email, string Password);

/// <summary>Public shape of a user account.</summary>
public sealed record UserDto(
    Guid Id,
    string Email,
    string Role,
    bool Verified,
    DateTime CreatedAtUtc);

/// <summary><c>POST /auth/register</c> and <c>POST /auth/login</c> response: JWT bearer token + user.</summary>
public sealed record AuthResponse(string Token, UserDto User);
