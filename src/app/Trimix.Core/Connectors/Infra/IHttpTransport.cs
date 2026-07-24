namespace Trimix.Core.Abstractions;

/// <summary>
/// A recorded HTTP request/response port (aggregator infra, owned by the cloud-connector group). Every
/// cloud / push connector talks to the network only through this port, which makes the whole
/// login → token → list → download flow fully offline-testable: the real <c>HttpClientTransport</c> wraps
/// one <see cref="System.Net.Http.HttpClient"/>; tests supply a <c>RecordedHttpTransport</c> that returns
/// canned <see cref="HttpExchangeResponse"/>s and records every <see cref="HttpExchangeRequest"/> for
/// assertion (including the load-bearing "zero requests before consent").
///
/// <para>
/// Binary bodies (e.g. a downloaded <c>.FIT</c>) travel through the string <see cref="HttpExchangeResponse.Body"/>
/// as Base64 — <c>HttpClientTransport</c> Base64-encodes any non-textual response and connectors decode with
/// their <c>BinaryPayload.Decode</c> helper. This keeps the port a pure string contract while staying byte-exact.
/// </para>
///
/// <para><b>Port lives in Abstractions</b> (with the other connector ports) so every connector group resolves it
/// via <c>using Trimix.Core.Abstractions;</c>; the default implementation lives under <c>Connectors/Infra</c>.</para>
/// </summary>
public interface IHttpTransport
{
    Task<HttpExchangeResponse> SendAsync(HttpExchangeRequest req, CancellationToken ct = default);
}

/// <summary>One outbound HTTP call. <paramref name="Body"/> is the request payload (form/JSON) or null for GET.</summary>
public sealed record HttpExchangeRequest(
    string Method,
    string Url,
    IReadOnlyDictionary<string, string> Headers,
    string? Body = null);

/// <summary>
/// One HTTP response. <paramref name="Body"/> is the decoded text for textual content types and Base64 for
/// binary content types (see <see cref="IHttpTransport"/>).
/// </summary>
public sealed record HttpExchangeResponse(
    int StatusCode,
    IReadOnlyDictionary<string, string> Headers,
    string Body)
{
    /// <summary>True for 2xx.</summary>
    public bool IsSuccess => StatusCode is >= 200 and < 300;
}
