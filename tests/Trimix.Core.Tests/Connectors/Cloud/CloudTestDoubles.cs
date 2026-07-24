using System.Collections.Concurrent;
using Trimix.Contracts.Dtos;
using Trimix.Core.Abstractions;

namespace Trimix.Core.Tests.Connectors.Cloud;

/// <summary>
/// Offline <see cref="IHttpTransport"/> test double: returns pre-scripted responses in FIFO order and records
/// every request for assertion. The load-bearing assertion "<c>Requests.Count == 0</c> before consent" is a
/// direct read of <see cref="Requests"/>. Enqueue exactly the responses the flow expects; an unscripted call
/// throws, which surfaces an unexpected network hit as a failing test.
/// </summary>
public sealed class RecordedHttpTransport : IHttpTransport
{
    private readonly Queue<HttpExchangeResponse> _responses = new();

    public List<HttpExchangeRequest> Requests { get; } = [];

    public RecordedHttpTransport Enqueue(HttpExchangeResponse response)
    {
        _responses.Enqueue(response);
        return this;
    }

    /// <summary>Enqueue a textual (JSON/HTML) response.</summary>
    public RecordedHttpTransport EnqueueText(string body, int status = 200) =>
        Enqueue(new HttpExchangeResponse(status, EmptyHeaders, body));

    /// <summary>Enqueue a binary response as Base64 (the transport contract for non-textual bodies).</summary>
    public RecordedHttpTransport EnqueueBinary(byte[] bytes, int status = 200) =>
        Enqueue(new HttpExchangeResponse(status, EmptyHeaders, Convert.ToBase64String(bytes)));

    public Task<HttpExchangeResponse> SendAsync(HttpExchangeRequest req, CancellationToken ct = default)
    {
        Requests.Add(req);
        if (_responses.Count == 0)
        {
            throw new InvalidOperationException($"Unscripted HTTP call to {req.Method} {req.Url}.");
        }

        return Task.FromResult(_responses.Dequeue());
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyHeaders =
        new Dictionary<string, string>();
}

/// <summary>In-memory <see cref="ISecureTokenStore"/> with an inspectable backing map (asserts what was stored).</summary>
public sealed class InMemorySecureTokenStore : ISecureTokenStore
{
    public ConcurrentDictionary<string, string> Values { get; } = new();

    public Task<string?> GetAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(Values.TryGetValue(key, out var v) ? v : null);

    public Task SetAsync(string key, string token, CancellationToken ct = default)
    {
        Values[key] = token;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken ct = default)
    {
        Values.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}

/// <summary>In-memory <see cref="IConnectorConsentStore"/> (starts denied; tests grant explicitly).</summary>
public sealed class InMemoryConnectorConsentStore : IConnectorConsentStore
{
    private readonly HashSet<string> _granted = [];

    public bool HasConsent(string connectorId) => _granted.Contains(connectorId);

    public Task GrantAsync(string connectorId, CancellationToken ct = default)
    {
        _granted.Add(connectorId);
        return Task.CompletedTask;
    }

    public Task RevokeAsync(string connectorId, CancellationToken ct = default)
    {
        _granted.Remove(connectorId);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Fake <see cref="IFitActivityParser"/>: records the bytes it was handed (so the test can prove the connector
/// Base64-decoded the download correctly) and returns a canned dive. Stands in for the file-import FIT reader
/// so the cloud connectors' HTTP flow is tested without reimplementing FIT decoding.
/// </summary>
public sealed class FakeFitActivityParser : IFitActivityParser
{
    private readonly IReadOnlyList<ImportedDive> _result;

    public FakeFitActivityParser(IReadOnlyList<ImportedDive>? result = null)
    {
        _result = result ??
        [
            new ImportedDive(
                new DateTime(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc), 2400, 28.5, 16.0, 21.0, "EAN32",
                [new ProfileSampleDto(0, 0, null, null), new ProfileSampleDto(60, 28.5, 21.0, null, Ndl: 12)]),
        ];
    }

    public List<byte[]> ParsedPayloads { get; } = [];

    public IReadOnlyList<ImportedDive> Parse(ReadOnlyMemory<byte> fitBytes)
    {
        ParsedPayloads.Add(fitBytes.ToArray());
        return _result;
    }
}
