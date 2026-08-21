using System.Net.Http.Headers;
using System.Text;
using Trimix.Core.Abstractions;

namespace Trimix.Core.Connectors.Infra;

/// <summary>
/// The production <see cref="IHttpTransport"/>: a thin wrapper over one long-lived
/// <see cref="HttpClient"/>. Connectors pass absolute URLs (they hit vendor hosts, not the Trimix API),
/// so this client carries no base address. Textual responses (JSON/text/form) are returned decoded;
/// non-textual responses (a downloaded <c>.FIT</c>) are Base64-encoded into
/// <see cref="HttpExchangeResponse.Body"/> so the port stays a pure string contract while remaining byte-exact.
/// </summary>
public sealed class HttpClientTransport(HttpClient? httpClient = null) : IHttpTransport, IDisposable
{
    private readonly HttpClient _http = httpClient ?? CreateDefaultClient();
    private readonly bool _ownsClient = httpClient is null;

    /// <summary>
    /// The default client vendor connectors need. Two settings are load-bearing rather than cosmetic:
    /// <b>automatic decompression</b>, because vendors gzip almost everything and a compressed body decoded as
    /// UTF-8 becomes mojibake; and a <b>cookie container</b>, because credential sign-in legs (Garmin's SSO,
    /// MySSI) carry session and bot-protection cookies across consecutive calls.
    /// </summary>
    private static HttpClient CreateDefaultClient() =>
        new(new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            CookieContainer = new System.Net.CookieContainer(),
            UseCookies = true,
        });

    public async Task<HttpExchangeResponse> SendAsync(HttpExchangeRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);

        using var message = new HttpRequestMessage(new HttpMethod(req.Method), req.Url);

        string? contentType = null;
        foreach (var (key, value) in req.Headers)
        {
            if (string.Equals(key, "Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                contentType = value;
                continue;
            }

            message.Headers.TryAddWithoutValidation(key, value);
        }

        if (req.Body is not null)
        {
            message.Content = new StringContent(req.Body, Encoding.UTF8);
            if (contentType is not null)
            {
                message.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
            }
        }

        using var response = await _http.SendAsync(message, ct).ConfigureAwait(false);
        var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        var body = IsTextual(mediaType)
            ? Encoding.UTF8.GetString(bytes)
            : Convert.ToBase64String(bytes);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in response.Headers)
        {
            // Set-Cookie is the one repeated header a comma cannot join: cookie Expires values contain
            // commas themselves, so comma-joining several cookies is unparseable. Cookies are handled by the
            // handler's CookieContainer anyway; newline-join keeps the surfaced value at least separable.
            var separator = header.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase) ? "\n" : ",";
            headers[header.Key] = string.Join(separator, header.Value);
        }

        foreach (var header in response.Content.Headers)
        {
            headers[header.Key] = string.Join(",", header.Value);
        }

        return new HttpExchangeResponse((int)response.StatusCode, headers, body);
    }

    private static bool IsTextual(string? mediaType)
    {
        if (string.IsNullOrEmpty(mediaType))
        {
            return true; // Absent content type: assume text; connectors that expect binary opt in explicitly.
        }

        return mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
            || mediaType.Contains("json", StringComparison.OrdinalIgnoreCase)
            || mediaType.Contains("xml", StringComparison.OrdinalIgnoreCase)
            || mediaType.Contains("x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _http.Dispose();
        }
    }
}
