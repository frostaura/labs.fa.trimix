using Trimix.Contracts;

namespace Trimix.Api.Data;

/// <summary>
/// An account. Email+password accounts hash the password with ASP.NET Core's PasswordHasher (PBKDF2);
/// passkey-only accounts (WebAuthn/FIDO2) carry a null <see cref="PasswordHash"/> — the credential
/// lives in <see cref="PasskeyCredential"/> — and a generated placeholder <see cref="Email"/> handle.
/// </summary>
public class User
{
    public Guid Id { get; set; }
    public required string Email { get; set; }

    /// <summary>
    /// PBKDF2 hash for email+password accounts; null for passkey-only accounts (no password exists to
    /// verify). Login by password must treat a null/empty hash as an unauthenticated account.
    /// </summary>
    public string? PasswordHash { get; set; }

    public string Role { get; set; } = UserRoles.User;
    public DateTime CreatedAtUtc { get; set; }
    public bool Verified { get; set; }
}

/// <summary>
/// A registered WebAuthn/FIDO2 passkey credential bound to a <see cref="User"/>. Discoverable
/// (resident) credentials, so login is usernameless: the authenticator returns the credential id we
/// look up here. <see cref="SignatureCounter"/> is advanced on every assertion for clone detection.
/// </summary>
public class PasskeyCredential
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    /// <summary>The authenticator-assigned credential id (raw bytes); unique across all users.</summary>
    public required byte[] CredentialId { get; set; }

    /// <summary>The COSE-encoded public key used to verify assertions.</summary>
    public required byte[] PublicKey { get; set; }

    /// <summary>Last signature counter seen; a non-increasing counter signals a cloned authenticator.</summary>
    public uint SignatureCounter { get; set; }

    /// <summary>The authenticator model identifier (all-zero when the authenticator withholds it).</summary>
    public Guid AaGuid { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>
/// Server-authoritative entitlement (one per user). Never derived client-side; upserted only by
/// verified receipts. Lifetime supersedes Pro; restores never downgrade.
/// </summary>
public class Entitlement
{
    public Guid UserId { get; set; }
    public EntitlementTier Tier { get; set; }
    public Guid? SourceReceiptId { get; set; }
    public required string Platform { get; set; }
    public DateTime GrantedAtUtc { get; set; }
}

/// <summary>
/// A verified receipt. The same receipt can only ever bind to one user, enforced by a unique index on
/// <see cref="TokenSha256"/> (a fixed-length hash) rather than on <see cref="Token"/> itself: a real Apple
/// StoreKit bundle receipt is a multi-kilobyte base64 blob, which overflows a bounded/indexed text column
/// (varchar length limit; btree index-row-size limit). <see cref="Token"/> is therefore stored as unbounded
/// text and the uniqueness/lookup key is the deterministic <see cref="TokenSha256"/> hash of it.
/// </summary>
public class ReceiptRecord
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string Platform { get; set; }
    public required string SkuId { get; set; }

    /// <summary>The raw store receipt (unbounded — Apple bundle receipts are multi-KB). Not indexed.</summary>
    public required string Token { get; set; }

    /// <summary>Lower-hex SHA-256 of <see cref="Token"/> (64 chars). Carries the unique index / dedup key.</summary>
    public required string TokenSha256 { get; set; }

    public DateTime FirstSeenUtc { get; set; }
}

/// <summary>A dive. Ids are client-generated GUIDs; sync is last-write-wins on <see cref="UpdatedAtUtc"/>.</summary>
public class Dive
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public DateTime StartUtc { get; set; }
    public int DurationSec { get; set; }
    public double MaxDepthM { get; set; }
    public double? AvgDepthM { get; set; }
    public double? MinTempC { get; set; }
    public string? GasMix { get; set; }
    public string Source { get; set; } = DiveSources.Manual;
    public string? SourceVendor { get; set; }
    public Guid? SiteId { get; set; }
    public string? ConditionsJson { get; set; }
    public string? GearCsv { get; set; }
    public string? BuddiesCsv { get; set; }
    public string? Notes { get; set; }
    public int? Rating { get; set; }

    // Typed dive-condition channels (aggregator foundation). Additive and nullable; ConditionsJson is
    // retained above for back-compat, so a legacy dive carries its conditions in the JSON blob and a
    // richer source populates these strongly-typed columns. No data migration between them this phase.
    public string? Visibility { get; set; }
    public string? Current { get; set; }
    public string? Weather { get; set; }
    public string? EntryType { get; set; }
    public string? WaterType { get; set; }
    public string? ExposureSuit { get; set; }
    public double? WeightKg { get; set; }

    /// <summary>Client edit time — the last-write-wins conflict key. Trusts the client's clock.</summary>
    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>
    /// Server-stamped modification time (set to the server's UtcNow on every upsert/delete). The sync
    /// cursor filters and returns on THIS column, never the client-supplied <see cref="UpdatedAtUtc"/>,
    /// so a device whose clock lags the server can never write a change other devices' pulls skip.
    /// </summary>
    public DateTime ServerUpdatedAtUtc { get; set; }

    public bool Deleted { get; set; }

    public List<ProfileSample> ProfileSamples { get; set; } = [];

    /// <summary>Gas cylinders used on this dive (aggregator foundation). Empty when the source reports none.</summary>
    public List<Cylinder> Cylinders { get; set; } = [];
}

/// <summary>One point of a dive profile. Composite PK (DiveId, TSeconds).</summary>
public class ProfileSample
{
    public Guid DiveId { get; set; }
    public int TSeconds { get; set; }
    public double DepthM { get; set; }
    public double? TempC { get; set; }
    public double? PpO2 { get; set; }

    // Rich per-sample channels (aggregator foundation); all nullable — a low-fidelity source leaves them null.
    public double? Ndl { get; set; }
    public double? CeilingM { get; set; }
    public double? AscentRateMPerMin { get; set; }
    public double? Cns { get; set; }
    public double? TankPressureBar { get; set; }
    public double? Setpoint { get; set; }
    public double? HeartRate { get; set; }
}

/// <summary>
/// A gas cylinder used on a dive (aggregator foundation). Own PK <see cref="Id"/>; ordered within a dive
/// by <see cref="Ordinal"/>. All measurements nullable.
/// </summary>
public class Cylinder
{
    public Guid Id { get; set; }
    public Guid DiveId { get; set; }
    public int Ordinal { get; set; }
    public double? O2 { get; set; }
    public double? He { get; set; }
    public double? StartBar { get; set; }
    public double? EndBar { get; set; }
    public double? VolumeL { get; set; }
    public double? WorkingPressureBar { get; set; }
}

/// <summary>A shared dive site. Geo is plain lat/lng doubles (provider-portable; PostGIS is post-slice).</summary>
public class Site
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public double Lat { get; set; }
    public double Lng { get; set; }
    public double? DepthMinM { get; set; }
    public double? DepthMaxM { get; set; }
    public string? Type { get; set; }
    public string? HazardsCsv { get; set; }
    public string? MarineLifeCsv { get; set; }
    public string? AccessNotes { get; set; }
    public string Status { get; set; } = SiteStatuses.Live;
    public string Source { get; set; } = SiteSources.Seed;
}

/// <summary>A community contribution to the shared site DB, moderated before it becomes visible.</summary>
public class SiteContribution
{
    public Guid Id { get; set; }
    public Guid? SiteId { get; set; }
    public Guid UserId { get; set; }
    public required string PayloadJson { get; set; }
    public string Status { get; set; } = ContributionStatus.Pending;
    public Guid? ModeratorId { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
    public string? Note { get; set; }
    public DateTime SubmittedAtUtc { get; set; }
}

/// <summary>
/// One append-only analytics row (never updated or deleted). <see cref="Id"/> is a monotonically
/// increasing identity so per-pseudo-user ordering is exact for funnel reconciliation.
/// </summary>
public class AnalyticsEvent
{
    public long Id { get; set; }

    /// <summary>
    /// Client-supplied idempotency key (<see cref="Guid.Empty"/> when absent). Ingest dedups by
    /// (<see cref="UserPseudoId"/>, <see cref="EventId"/>) so a retried batch never double-counts a
    /// funnel event. Server-emitted rows leave this empty (server emission is already exactly-once).
    /// </summary>
    public Guid EventId { get; set; }

    public required string UserPseudoId { get; set; }
    public Guid? UserId { get; set; }
    public required string Name { get; set; }
    public required string PropsJson { get; set; }
    public DateTime ClientTimestampUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; }
    public required string Platform { get; set; }
    public required string AppVersion { get; set; }
}

/// <summary>Lifecycle of a <see cref="Site"/> row.</summary>
public static class SiteStatuses
{
    public const string Live = "live";
}

/// <summary>Provenance of a <see cref="Site"/> row.</summary>
public static class SiteSources
{
    public const string Seed = "seed";
    public const string Contributed = "contributed";
}
