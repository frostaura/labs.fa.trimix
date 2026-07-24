using Microsoft.Maui.Storage;
using Trimix.Core.Connectors.FileImport;

namespace Trimix.App.Services;

/// <summary>
/// Real platform file picker for the file-import source connector (Core <see cref="IFilePicker"/> port).
/// Cross-platform via <c>Microsoft.Maui.Storage.FilePicker</c> — Android Storage Access Framework, iOS
/// <c>UIDocumentPicker</c>. Register in MauiProgram BEFORE <c>AddTrimixCore</c> so Core's
/// <c>UnsupportedFilePicker</c> fallback never engages. Returns null when the diver cancels.
///
/// Note: dive-log files (.fit/.uddf/.dl7/.json) have no universal UTI/MIME; the slice picks with the default
/// (all files) filter and lets the connector's format sniffer reject non-dive files. A per-platform
/// <c>FilePickerFileType</c> allow-list is a post-slice polish item.
/// </summary>
public sealed class MauiFilePicker : Trimix.Core.Connectors.FileImport.IFilePicker
{
    public async Task<PickedFile?> PickDiveFileAsync(CancellationToken cancellationToken = default)
    {
        var result = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "Select a dive-log file",
        });

        if (result is null)
        {
            return null;
        }

        await using var stream = await result.OpenReadAsync();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);
        return new PickedFile(result.FileName, memory.ToArray());
    }
}
