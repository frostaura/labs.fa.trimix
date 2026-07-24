namespace Trimix.Core.Connectors.FileImport;

/// <summary>
/// A file the diver picked from their device (share-sheet / document-picker). <paramref name="FileName"/>
/// carries the original name so the sniffer can use its extension; <paramref name="Contents"/> is the
/// raw bytes (text formats are UTF-8, FIT is binary).
/// </summary>
public sealed record PickedFile(string FileName, byte[] Contents);

/// <summary>
/// Hexagonal port for the platform file picker (architecture overview §2, file-import is the only clean
/// Garmin path and a universal fallback for every closed vendor). Core depends only on this interface;
/// the real MAUI <c>FilePicker</c> implementation lives in the App (<c>Microsoft.Maui.Storage</c>) and is
/// registered before <c>AddTrimixCore</c>. A <see langword="null"/> result means the diver cancelled.
/// </summary>
public interface IFilePicker
{
    Task<PickedFile?> PickDiveFileAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Dev/test fallback so Core resolves without the App's real picker (mirrors
/// <c>UnsupportedPasskeyAuthService</c>). Returns null — "no file picked" — on every platform.
/// </summary>
public sealed class UnsupportedFilePicker : IFilePicker
{
    public Task<PickedFile?> PickDiveFileAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<PickedFile?>(null);
}
