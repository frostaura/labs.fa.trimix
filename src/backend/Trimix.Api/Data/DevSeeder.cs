using Microsoft.AspNetCore.Identity;
using Trimix.Contracts;

namespace Trimix.Api.Data;

/// <summary>Development-only seed data (never applied outside the Development environment).</summary>
public static class DevSeeder
{
    /// <summary>Dev moderator credentials (architecture overview §8: seeded moderator account in dev).</summary>
    public const string ModeratorEmail = "moderator@trimix.dev";
    public const string ModeratorPassword = "TrimixDev!2026";

    /// <summary>Idempotently seeds the dev moderator account.</summary>
    public static void SeedModerator(AppDbContext db, IPasswordHasher<User> passwordHasher)
    {
        if (db.Users.Any(u => u.Email == ModeratorEmail))
        {
            return;
        }

        var moderator = new User
        {
            Id = Guid.NewGuid(),
            Email = ModeratorEmail,
            PasswordHash = string.Empty,
            Role = UserRoles.Moderator,
            CreatedAtUtc = DateTime.UtcNow,
            Verified = true,
        };
        moderator.PasswordHash = passwordHasher.HashPassword(moderator, ModeratorPassword);
        db.Users.Add(moderator);
        db.SaveChanges();
    }
}
