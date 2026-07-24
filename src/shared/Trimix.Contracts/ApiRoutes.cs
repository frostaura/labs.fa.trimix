namespace Trimix.Contracts;

/// <summary>
/// API route constants for every route in the architecture doc's API table (all under <c>/api/v1</c>).
/// Relative constants (no leading slash unless group-root) compose with the API's MapGroup structure;
/// <c>*Full</c> constants / helpers are for clients and integration tests.
/// </summary>
public static class ApiRoutes
{
    public const string Root = "/api/v1";

    /// <summary>Liveness + db check. Relative to <see cref="Root"/>.</summary>
    public const string Healthz = "healthz";
    public const string HealthzFull = $"{Root}/{Healthz}";

    public static class Auth
    {
        public const string Group = "auth";
        public const string Register = "register";
        public const string Login = "login";
        public const string RegisterFull = $"{Root}/{Group}/{Register}";
        public const string LoginFull = $"{Root}/{Group}/{Login}";

        // Passkey (WebAuthn/FIDO2) — the only account sign-in method. Two-step begin→finish per flow.
        public const string PasskeyRegisterBegin = "passkey/register/begin";
        public const string PasskeyRegisterFinish = "passkey/register/finish";
        public const string PasskeyLoginBegin = "passkey/login/begin";
        public const string PasskeyLoginFinish = "passkey/login/finish";
        public const string PasskeyRegisterBeginFull = $"{Root}/{Group}/{PasskeyRegisterBegin}";
        public const string PasskeyRegisterFinishFull = $"{Root}/{Group}/{PasskeyRegisterFinish}";
        public const string PasskeyLoginBeginFull = $"{Root}/{Group}/{PasskeyLoginBegin}";
        public const string PasskeyLoginFinishFull = $"{Root}/{Group}/{PasskeyLoginFinish}";
    }

    public static class Entitlements
    {
        public const string Group = "entitlements";
        public const string Me = "me";
        public const string MeFull = $"{Root}/{Group}/{Me}";
    }

    public static class Purchases
    {
        public const string Group = "purchases";
        public const string Validate = "validate";
        public const string ValidateFull = $"{Root}/{Group}/{Validate}";
    }

    public static class Dives
    {
        public const string Group = "dives";
        public const string Sync = "sync";
        public const string SyncFull = $"{Root}/{Group}/{Sync}";
    }

    public static class Sites
    {
        public const string Group = "sites";

        /// <summary>GET group root: <c>/sites?query=&amp;lat=&amp;lng=&amp;limit=</c>.</summary>
        public const string Search = "/";
        public const string ById = "{id}";
        public const string Contributions = "contributions";

        /// <summary>GET the calling user's own contributions: <c>/sites/contributions/mine</c>.</summary>
        public const string ContributionsMine = "contributions/mine";

        public const string SearchFull = $"{Root}/{Group}";
        public const string ByIdTemplateFull = $"{Root}/{Group}/{ById}";
        public const string ContributionsFull = $"{Root}/{Group}/{Contributions}";
        public const string ContributionsMineFull = $"{Root}/{Group}/{ContributionsMine}";

        public static string ByIdFull(Guid id) => $"{Root}/{Group}/{id}";
    }

    /// <summary>
    /// Privacy self-service (GDPR/CCPA data export + erasure). Additive contract amendment;
    /// endpoints to be wired server-side. Export is idempotent GET; delete is a POST because
    /// it mutates (erases synced dives + analytics rows for the caller).
    /// </summary>
    public static class Privacy
    {
        public const string Group = "privacy";
        public const string Export = "export";
        public const string Delete = "delete";
        public const string ExportFull = $"{Root}/{Group}/{Export}";
        public const string DeleteFull = $"{Root}/{Group}/{Delete}";
    }

    public static class Moderation
    {
        public const string Group = "moderation";
        public const string Contributions = "contributions";
        public const string Resolve = "contributions/{id}/resolve";

        public const string ContributionsFull = $"{Root}/{Group}/{Contributions}";
        public const string ResolveTemplateFull = $"{Root}/{Group}/{Resolve}";

        public static string ResolveFull(Guid id) => $"{Root}/{Group}/{Contributions}/{id}/resolve";
    }

    public static class Analytics
    {
        public const string Group = "analytics";
        public const string Events = "events";
        public const string EventsFull = $"{Root}/{Group}/{Events}";
    }
}
