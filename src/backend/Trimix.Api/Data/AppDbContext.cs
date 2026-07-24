using Microsoft.EntityFrameworkCore;

namespace Trimix.Api.Data;

/// <summary>
/// Provider-portable EF model (architecture overview — Data model). PostgreSQL (Npgsql) is the
/// canonical runtime provider; integration tests run the same model hermetically on SQLite
/// in-memory. No Postgres-only column types: JSON and CSV columns are plain strings.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<PasskeyCredential> PasskeyCredentials => Set<PasskeyCredential>();
    public DbSet<Entitlement> Entitlements => Set<Entitlement>();
    public DbSet<ReceiptRecord> Receipts => Set<ReceiptRecord>();
    public DbSet<Dive> Dives => Set<Dive>();
    public DbSet<ProfileSample> ProfileSamples => Set<ProfileSample>();
    public DbSet<Cylinder> Cylinders => Set<Cylinder>();
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<SiteContribution> SiteContributions => Set<SiteContribution>();
    public DbSet<AnalyticsEvent> AnalyticsEvents => Set<AnalyticsEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(user =>
        {
            user.HasIndex(u => u.Email).IsUnique();
            user.Property(u => u.Email).HasMaxLength(320);
            user.Property(u => u.Role).HasMaxLength(32);
        });

        modelBuilder.Entity<PasskeyCredential>(credential =>
        {
            // Usernameless login resolves the account by the assertion's credential id.
            credential.HasIndex(c => c.CredentialId).IsUnique();
            credential.HasIndex(c => c.UserId);
            // uint has no portable column type; store the counter as a provider-portable integer.
            credential.Property(c => c.SignatureCounter).HasConversion<long>();
            credential.HasOne<User>().WithMany().HasForeignKey(c => c.UserId);
        });

        modelBuilder.Entity<Entitlement>(entitlement =>
        {
            entitlement.HasKey(e => e.UserId);
            entitlement.Property(e => e.Tier).HasConversion<string>().HasMaxLength(16);
            entitlement.Property(e => e.Platform).HasMaxLength(32);
            entitlement.HasOne<User>().WithOne().HasForeignKey<Entitlement>(e => e.UserId);
        });

        modelBuilder.Entity<ReceiptRecord>(receipt =>
        {
            // Uniqueness rides the fixed-length hash, NOT the raw token: an Apple StoreKit bundle receipt
            // is a multi-KB base64 blob that overflows both a bounded varchar and the btree index-row-size
            // limit. Token itself is unbounded text (no length cap, not indexed).
            receipt.HasIndex(r => r.TokenSha256).IsUnique();
            receipt.Property(r => r.Platform).HasMaxLength(32);
            receipt.Property(r => r.SkuId).HasMaxLength(64);
            receipt.Property(r => r.TokenSha256).HasMaxLength(64).IsFixedLength();
            receipt.HasOne<User>().WithMany().HasForeignKey(r => r.UserId);
        });

        modelBuilder.Entity<Dive>(dive =>
        {
            dive.Property(d => d.Id).ValueGeneratedNever(); // Client-generated GUIDs.
            // Sync cursor scans on the server-stamped column (see DivesEndpoints).
            dive.HasIndex(d => new { d.UserId, d.ServerUpdatedAtUtc });
            dive.Property(d => d.Source).HasMaxLength(16);
            dive.Property(d => d.SourceVendor).HasMaxLength(64);
            dive.Property(d => d.GasMix).HasMaxLength(32);
            // Cap user-supplied free text so a client can't push multi-megabyte rows.
            dive.Property(d => d.ConditionsJson).HasMaxLength(DiveConditionsMaxLength);
            dive.Property(d => d.GearCsv).HasMaxLength(DiveTextMaxLength);
            dive.Property(d => d.BuddiesCsv).HasMaxLength(DiveTextMaxLength);
            dive.Property(d => d.Notes).HasMaxLength(DiveNotesMaxLength);
            // Cap the typed condition strings (provider-portable; matches the capped-text pattern above).
            dive.Property(d => d.Visibility).HasMaxLength(DiveConditionMaxLength);
            dive.Property(d => d.Current).HasMaxLength(DiveConditionMaxLength);
            dive.Property(d => d.Weather).HasMaxLength(DiveConditionMaxLength);
            dive.Property(d => d.EntryType).HasMaxLength(DiveConditionMaxLength);
            dive.Property(d => d.WaterType).HasMaxLength(DiveConditionMaxLength);
            dive.Property(d => d.ExposureSuit).HasMaxLength(DiveConditionMaxLength);
            dive.HasMany(d => d.ProfileSamples).WithOne().HasForeignKey(s => s.DiveId);
            dive.HasMany(d => d.Cylinders).WithOne().HasForeignKey(c => c.DiveId);
        });

        modelBuilder.Entity<ProfileSample>(sample =>
        {
            sample.HasKey(s => new { s.DiveId, s.TSeconds });
        });

        modelBuilder.Entity<Cylinder>(cylinder =>
        {
            cylinder.HasKey(c => c.Id);
            cylinder.HasIndex(c => new { c.DiveId, c.Ordinal });
        });

        modelBuilder.Entity<Site>(site =>
        {
            site.Property(s => s.Id).ValueGeneratedNever(); // Seeded / server-assigned GUIDs.
            site.HasIndex(s => s.Name);
            site.HasIndex(s => new { s.Lat, s.Lng });
            site.Property(s => s.Name).HasMaxLength(256);
            site.Property(s => s.Status).HasMaxLength(16);
            site.Property(s => s.Source).HasMaxLength(16);
            site.HasData(SiteSeed.All);
        });

        modelBuilder.Entity<SiteContribution>(contribution =>
        {
            contribution.HasIndex(c => c.Status);
            contribution.HasIndex(c => c.UserId); // GET /sites/contributions/mine.
            contribution.Property(c => c.Status).HasMaxLength(16);
            contribution.Property(c => c.PayloadJson).HasMaxLength(ContributionPayloadMaxLength);
            contribution.Property(c => c.Note).HasMaxLength(2048);
        });

        modelBuilder.Entity<AnalyticsEvent>(analyticsEvent =>
        {
            analyticsEvent.Property(e => e.Id).ValueGeneratedOnAdd(); // Append-only identity: exact ingest order.
            analyticsEvent.HasIndex(e => new { e.UserPseudoId, e.Id });
            // Idempotency lookup for retried batches (dedup enforced in code — Guid.Empty is not unique).
            analyticsEvent.HasIndex(e => new { e.UserPseudoId, e.EventId });
            analyticsEvent.Property(e => e.Name).HasMaxLength(64);
            analyticsEvent.Property(e => e.UserPseudoId).HasMaxLength(128);
            analyticsEvent.Property(e => e.Platform).HasMaxLength(32);
            analyticsEvent.Property(e => e.AppVersion).HasMaxLength(64);
        });
    }

    /// <summary>Max characters accepted for a site contribution payload (~16 KB).</summary>
    public const int ContributionPayloadMaxLength = 16 * 1024;

    /// <summary>Max characters for a dive's free-text note.</summary>
    public const int DiveNotesMaxLength = 4096;

    /// <summary>Max characters for a dive's conditions JSON blob.</summary>
    public const int DiveConditionsMaxLength = 8192;

    /// <summary>Max characters for a dive's CSV free-text lists (gear, buddies).</summary>
    public const int DiveTextMaxLength = 1024;

    /// <summary>Max characters for a dive's typed condition strings (visibility, current, weather, …).</summary>
    public const int DiveConditionMaxLength = 64;
}
