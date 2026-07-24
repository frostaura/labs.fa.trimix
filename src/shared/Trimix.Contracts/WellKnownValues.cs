namespace Trimix.Contracts;

/// <summary>Receipt platforms understood by the API's receipt verifiers (architecture overview §3).</summary>
public static class ReceiptPlatforms
{
    /// <summary>HMAC-signed sandbox receipts — the slice's live purchase path (default in dev).</summary>
    public const string Sandbox = "sandbox";

    /// <summary>Documented stub until production wiring (post-slice).</summary>
    public const string GooglePlay = "google_play";

    /// <summary>Documented stub until production wiring (post-slice).</summary>
    public const string AppStore = "app_store";
}

/// <summary>Moderation lifecycle of a site contribution.</summary>
public static class ContributionStatus
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
}

/// <summary>How a dive entered the logbook.</summary>
public static class DiveSources
{
    public const string Manual = "manual";
    public const string Import = "import";
}

/// <summary>Server roles (architecture overview §8).</summary>
public static class UserRoles
{
    public const string User = "user";
    public const string Moderator = "moderator";
}
