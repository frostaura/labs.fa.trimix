using Microsoft.Extensions.Hosting;
using Trimix.Contracts;

namespace Trimix.Api.Billing;

/// <summary>Outcome of verifying one receipt.</summary>
public enum ReceiptVerificationStatus
{
    /// <summary>Authentic receipt for the given SKU.</summary>
    Valid = 0,

    /// <summary>Malformed, tampered, or mismatched receipt — must never grant.</summary>
    Invalid = 1,

    /// <summary>The platform's verifier exists but has no production wiring yet.</summary>
    NotConfigured = 2,
}

/// <summary>Result of <see cref="IReceiptVerifier.VerifyAsync"/>.</summary>
public sealed record ReceiptVerification(ReceiptVerificationStatus Status, string? Error = null)
{
    public static readonly ReceiptVerification Valid = new(ReceiptVerificationStatus.Valid);
    public static ReceiptVerification Invalid(string error) => new(ReceiptVerificationStatus.Invalid, error);
    public static ReceiptVerification NotConfigured(string error) => new(ReceiptVerificationStatus.NotConfigured, error);
}

/// <summary>
/// Per-platform receipt verifier (architecture overview §3). Registered once per
/// <see cref="ReceiptPlatforms"/> value; <c>POST /purchases/validate</c> dispatches on
/// <see cref="Platform"/>. The server is the only entitlement authority — a receipt that fails
/// verification grants nothing.
/// </summary>
public interface IReceiptVerifier
{
    /// <summary>The <see cref="ReceiptPlatforms"/> value this verifier handles.</summary>
    string Platform { get; }

    /// <summary>Verifies that <paramref name="token"/> is an authentic receipt for <paramref name="skuId"/>.</summary>
    Task<ReceiptVerification> VerifyAsync(string skuId, string token, CancellationToken cancellationToken = default);
}

/// <summary>
/// Verifies sandbox receipts: HMAC-SHA256 over the token's payload part with the configured
/// sandbox shared secret (<c>Billing:SandboxSecret</c>), token codec shared with Trimix.Core's
/// SandboxBillingService via <see cref="SandboxReceiptCodec"/> in Trimix.Contracts. This is the
/// slice's live purchase path in Development.
/// <para>
/// SECURITY: this verifier mints real entitlements from a shared HMAC secret whose placeholder value
/// is committed to the repo. It is only registered in Development or behind
/// <see cref="EnabledConfigKey"/> (see Program.cs), and it additionally refuses to run when the
/// configured secret is the committed placeholder outside Development — so a deployment that inherits
/// the dev config can never let a user self-mint a Lifetime entitlement.
/// </para>
/// </summary>
public sealed class SandboxReceiptVerifier(IConfiguration configuration, IHostEnvironment environment) : IReceiptVerifier
{
    /// <summary>Configuration key for the sandbox shared secret.</summary>
    public const string SecretConfigKey = "Billing:SandboxSecret";

    /// <summary>Explicit server-side opt-in (bool) to enable the sandbox verifier outside Development.</summary>
    public const string EnabledConfigKey = "Billing:EnableSandbox";

    /// <summary>The dev-only placeholder secret committed to appsettings.Development.json / Trimix.Core.</summary>
    public const string CommittedPlaceholderSecret = "PLACEHOLDER-dev-only-sandbox-receipt-hmac-shared-secret";

    private readonly string? _secret = configuration[SecretConfigKey];

    public string Platform => ReceiptPlatforms.Sandbox;

    public Task<ReceiptVerification> VerifyAsync(string skuId, string token, CancellationToken cancellationToken = default) =>
        Task.FromResult(Verify(skuId, token));

    private ReceiptVerification Verify(string skuId, string token)
    {
        if (string.IsNullOrEmpty(_secret))
        {
            return ReceiptVerification.NotConfigured($"{SecretConfigKey} is not configured.");
        }

        // Fail closed: the publicly known placeholder must never mint entitlements off dev machines.
        if (!environment.IsDevelopment()
            && string.Equals(_secret, CommittedPlaceholderSecret, StringComparison.Ordinal))
        {
            return ReceiptVerification.NotConfigured(
                "Sandbox billing refuses the committed placeholder secret outside Development.");
        }

        if (!SandboxReceiptCodec.TryDecode(token, _secret, out var payload))
        {
            return ReceiptVerification.Invalid("Receipt token is malformed or its signature is invalid.");
        }

        if (!string.Equals(payload.Platform, ReceiptPlatforms.Sandbox, StringComparison.Ordinal))
        {
            return ReceiptVerification.Invalid("Receipt platform mismatch.");
        }

        if (!string.Equals(payload.SkuId, skuId, StringComparison.Ordinal))
        {
            return ReceiptVerification.Invalid("Receipt was issued for a different SKU.");
        }

        return ReceiptVerification.Valid;
    }
}

// Production google_play + app_store verifiers now live in their own files
// (GooglePlayApiReceiptVerifier.cs, AppleAppStoreReceiptVerifier.cs). The former NotConfigured
// stubs (GooglePlayReceiptVerifier / AppStoreReceiptVerifier) were removed when the real,
// config-driven verifiers were wired in Program.cs — both still answer NotConfigured (⇒ endpoint
// 501) when their vendor credentials are absent, preserving the creds-absent dev behavior.
