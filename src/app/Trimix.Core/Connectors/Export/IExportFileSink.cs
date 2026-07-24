namespace Trimix.Core.Connectors.Export;

/// <summary>
/// Hexagonal port for persisting/sharing an exported artifact (architecture overview §2). The real impl
/// lives in the MAUI App (write to cache dir + share-sheet / document-picker via
/// <c>Microsoft.Maui.Storage</c>) and CANNOT live in Core; Core ships an in-memory fallback for dev/test.
/// </summary>
public interface IExportFileSink
{
    /// <summary>Writes/shares <paramref name="file"/> and returns a path or URI the caller can surface.</summary>
    Task<string> WriteAsync(ExportedDive file, CancellationToken ct = default);
}

/// <summary>Dev/test fallback: captures the last write in memory and returns a synthetic path. No I/O.</summary>
public sealed class InMemoryExportFileSink : IExportFileSink
{
    public ExportedDive? LastWritten { get; private set; }

    public List<ExportedDive> Writes { get; } = [];

    public Task<string> WriteAsync(ExportedDive file, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        LastWritten = file;
        Writes.Add(file);
        return Task.FromResult("memory://" + file.FileName);
    }
}
