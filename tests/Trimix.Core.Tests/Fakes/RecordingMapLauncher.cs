using Trimix.Core.Abstractions;

namespace Trimix.Core.Tests;

/// <summary>Records the coordinate handed to the platform maps app, so the entry-point tap can be asserted.</summary>
public sealed class RecordingMapLauncher : IMapLauncher
{
    public List<(double Latitude, double Longitude, string? Label)> Opened { get; } = [];

    public Task OpenAsync(double latitude, double longitude, string? label = null, CancellationToken ct = default)
    {
        Opened.Add((latitude, longitude, label));
        return Task.CompletedTask;
    }
}
