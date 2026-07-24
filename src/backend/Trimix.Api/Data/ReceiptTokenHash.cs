using System.Security.Cryptography;
using System.Text;

namespace Trimix.Api.Data;

/// <summary>
/// Deterministic, fixed-length hash of a store receipt token. Used as the uniqueness/dedup key for
/// <see cref="ReceiptRecord"/> so the raw token (a multi-KB Apple bundle receipt for iOS) can be stored
/// as unbounded text while the unique index rides a bounded 64-char column — a raw-token unique btree
/// index would reject a real Apple receipt (varchar length / index-row-size limits) and 500 the purchase.
/// </summary>
public static class ReceiptTokenHash
{
    /// <summary>Lower-hex SHA-256 (64 chars) of the UTF-8 bytes of <paramref name="token"/>.</summary>
    public static string Compute(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}
