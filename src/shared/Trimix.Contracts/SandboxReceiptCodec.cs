using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Trimix.Contracts;

/// <summary>The signed payload of a sandbox receipt token.</summary>
/// <param name="Platform">Must be <see cref="ReceiptPlatforms.Sandbox"/>.</param>
/// <param name="SkuId">A <see cref="Skus"/> id.</param>
/// <param name="PurchaseId">Unique id of the (sandbox) purchase — the receipt identity.</param>
/// <param name="IssuedAtUtc">When the sandbox store issued the receipt.</param>
public sealed record SandboxReceiptPayload(
    string Platform,
    string SkuId,
    string PurchaseId,
    DateTime IssuedAtUtc);

/// <summary>
/// Shared token codec for the sandbox billing path (architecture overview §2–3). Trimix.Core's
/// <c>SandboxBillingService</c> ENCODES receipts with this codec and the API's
/// <c>SandboxReceiptVerifier</c> DECODES + verifies them — both sides reference this single
/// implementation so the token format can never drift.
///
/// Token format: <c>base64url(json{platform,skuId,purchaseId,issuedAtUtc}) + "." + hex(hmacsha256)</c>
/// where the HMAC-SHA256 is computed over the base64url payload part with the configured
/// sandbox shared secret.
/// </summary>
public static class SandboxReceiptCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Encodes and signs <paramref name="payload"/> into a sandbox receipt token.</summary>
    public static string Encode(SandboxReceiptPayload payload, string secret)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentException.ThrowIfNullOrEmpty(secret);

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var payloadPart = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(json));
        return $"{payloadPart}.{SignatureHex(payloadPart, secret)}";
    }

    /// <summary>
    /// Verifies the HMAC signature of <paramref name="token"/> with <paramref name="secret"/> and, when
    /// authentic, decodes its payload. Returns <c>false</c> for malformed, tampered, or wrongly-signed tokens.
    /// </summary>
    public static bool TryDecode(string? token, string secret, [NotNullWhen(true)] out SandboxReceiptPayload? payload)
    {
        payload = null;
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrEmpty(secret))
        {
            return false;
        }

        var parts = token.Split('.');
        if (parts.Length != 2)
        {
            return false;
        }

        var expected = Encoding.UTF8.GetBytes(SignatureHex(parts[0], secret));
        var actual = Encoding.UTF8.GetBytes(parts[1].ToLowerInvariant());
        if (!CryptographicOperations.FixedTimeEquals(expected, actual))
        {
            return false;
        }

        try
        {
            var json = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(parts[0]));
            var decoded = JsonSerializer.Deserialize<SandboxReceiptPayload>(json, JsonOptions);
            if (decoded is null
                || string.IsNullOrWhiteSpace(decoded.Platform)
                || string.IsNullOrWhiteSpace(decoded.SkuId)
                || string.IsNullOrWhiteSpace(decoded.PurchaseId))
            {
                return false;
            }

            payload = decoded;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string SignatureHex(string payloadPart, string secret)
    {
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payloadPart));
        return Convert.ToHexStringLower(mac);
    }
}
