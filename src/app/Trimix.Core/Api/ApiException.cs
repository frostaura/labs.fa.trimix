using System.Net;

namespace Trimix.Core.Api;

/// <summary>Typed error envelope extracted from a non-success API response.</summary>
public sealed record ApiError(string Code, string Message);

/// <summary>
/// A non-success response from the Trimix API. <see cref="Error"/> is parsed leniently from the
/// body: <c>{code,message}</c> envelopes and RFC 7807 problem details (<c>{title,detail}</c>) are
/// both understood; anything else falls back to the HTTP status.
/// </summary>
public sealed class ApiException(HttpStatusCode statusCode, ApiError error) : Exception(error.Message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;

    public ApiError Error { get; } = error;
}
