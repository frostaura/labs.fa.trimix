using System.Globalization;
using Trimix.Contracts.Dtos;
using Trimix.Core.Abstractions;
using Trimix.Core.Connectors.Export;

namespace Trimix.Core.Tests.Connectors.Export;

/// <summary>Canonical <see cref="DiveDto"/> fixtures for the export/push connector tests.</summary>
internal static class DiveFixture
{
    /// <summary>A full-fidelity trimix dive: 3 samples, tank pressure, one cylinder, escapable notes.</summary>
    public static DiveDto Trimix() => new(
        Id: Guid.Parse("11111111-1111-1111-1111-111111111111"),
        StartUtc: new DateTime(2026, 6, 1, 8, 30, 0, DateTimeKind.Utc),
        DurationSec: 3180,
        MaxDepthM: 28.5,
        AvgDepthM: 16.2,
        MinTempC: 12.0,
        GasMix: "21/35",
        Source: "import",
        SourceVendor: "shearwater",
        SiteId: null,
        ConditionsJson: null,
        GearCsv: null,
        BuddiesCsv: "Alice, Bob",
        Notes: "Great (test) dive \\ with (parens)",
        Rating: 4,
        UpdatedAtUtc: new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc),
        Deleted: false,
        ProfileSamples:
        [
            new ProfileSampleDto(0, 0.0, 21.0, null),
            new ProfileSampleDto(60, 15.0, 18.0, null, TankPressureBar: 200),
            new ProfileSampleDto(120, 28.5, 12.0, null, TankPressureBar: 150),
        ],
        Cylinders: [new CylinderDto(O2: 21, He: 35, StartBar: 230, EndBar: 90)]);

    /// <summary>A depth-temp-only air dive (HealthKit-grade fidelity): no cylinders, no tank pressure.</summary>
    public static DiveDto AirNoCylinders() => new(
        Id: Guid.Parse("22222222-2222-2222-2222-222222222222"),
        StartUtc: new DateTime(2026, 6, 2, 9, 0, 0, DateTimeKind.Utc),
        DurationSec: 1800,
        MaxDepthM: 18.0,
        AvgDepthM: null,
        MinTempC: null,
        GasMix: "Air",
        Source: "import",
        SourceVendor: "apple_health",
        SiteId: null,
        ConditionsJson: null,
        GearCsv: null,
        BuddiesCsv: null,
        Notes: null,
        Rating: null,
        UpdatedAtUtc: new DateTime(2026, 6, 2, 10, 0, 0, DateTimeKind.Utc),
        Deleted: false,
        ProfileSamples:
        [
            new ProfileSampleDto(0, 0.0, null, null),
            new ProfileSampleDto(60, 18.0, null, null),
        ]);

    /// <summary>Invariant-culture double parse — the CI locale may use a comma decimal separator.</summary>
    public static double D(string value) => double.Parse(value, CultureInfo.InvariantCulture);

    /// <summary>Invariant-culture int parse.</summary>
    public static int I(string value) => int.Parse(value, CultureInfo.InvariantCulture);
}

/// <summary>Records exported artifacts instead of touching the filesystem.</summary>
internal sealed class RecordingExportFileSink : IExportFileSink
{
    public List<ExportedDive> Writes { get; } = [];

    public Task<string> WriteAsync(ExportedDive file, CancellationToken ct = default)
    {
        Writes.Add(file);
        return Task.FromResult("file://" + file.FileName);
    }
}

/// <summary>
/// Offline <see cref="IHttpTransport"/> test double: records every request and returns scripted responses
/// (default 200/empty). Distinct from any G4-provided recorded transport to keep G5 tests self-contained.
/// </summary>
internal sealed class ScriptedHttpTransport : IHttpTransport
{
    public List<HttpExchangeRequest> Requests { get; } = [];

    public Func<HttpExchangeRequest, HttpExchangeResponse>? Handler { get; set; }

    public Task<HttpExchangeResponse> SendAsync(HttpExchangeRequest req, CancellationToken ct)
    {
        Requests.Add(req);
        var response = Handler?.Invoke(req)
            ?? new HttpExchangeResponse(200, new Dictionary<string, string>(), string.Empty);
        return Task.FromResult(response);
    }

    public static IReadOnlyDictionary<string, string> NoHeaders => new Dictionary<string, string>();
}

/// <summary>In-memory <see cref="ISecureTokenStore"/> — exposes every stored value so tests can assert a password is never persisted.</summary>
internal sealed class FakeSecureTokenStore : ISecureTokenStore
{
    public Dictionary<string, string> Entries { get; } = [];

    public Task<string?> GetAsync(string key, CancellationToken ct) =>
        Task.FromResult(Entries.TryGetValue(key, out var value) ? value : null);

    public Task SetAsync(string key, string token, CancellationToken ct)
    {
        Entries[key] = token;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken ct)
    {
        Entries.Remove(key);
        return Task.CompletedTask;
    }
}

/// <summary>In-memory <see cref="IConnectorConsentStore"/>.</summary>
internal sealed class FakeConsentStore : IConnectorConsentStore
{
    private readonly HashSet<string> _granted = [];

    public bool HasConsent(string connectorId) => _granted.Contains(connectorId);

    public Task GrantAsync(string connectorId, CancellationToken ct)
    {
        _granted.Add(connectorId);
        return Task.CompletedTask;
    }

    public Task RevokeAsync(string connectorId, CancellationToken ct)
    {
        _granted.Remove(connectorId);
        return Task.CompletedTask;
    }
}
