namespace Trimix.Core.Connectors.Cloud;

/// <summary>
/// Ephemeral credentials handed to a gated connector for a single credential→token exchange. Held only in a
/// transient field, cleared immediately after the exchange, and <b>never persisted</b> — only the resulting
/// token is written to <c>ISecureTokenStore</c>. This record is the in-memory carrier that guardrail exists to protect.
/// </summary>
public sealed record ConnectorCredentials(string Username, string Password);

/// <summary>Raised when a credential→token exchange fails (bad credentials, throttled, endpoint moved).</summary>
public sealed class ConnectorAuthException(string message) : Exception(message);

/// <summary>Decodes a (possibly binary) HTTP response <c>Body</c> back to bytes for the FIT parser.</summary>
internal static class BinaryPayload
{
    /// <summary>
    /// Download endpoints deliver binary as Base64 in the string <c>Body</c> (see <c>HttpClientTransport</c>).
    /// Falls back to a Latin-1 byte view if the body is not Base64, so an unexpected text body never corrupts.
    /// </summary>
    public static byte[] Decode(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var trimmed = body.Trim();
        try
        {
            return Convert.FromBase64String(trimmed);
        }
        catch (FormatException)
        {
            return System.Text.Encoding.Latin1.GetBytes(body);
        }
    }
}
