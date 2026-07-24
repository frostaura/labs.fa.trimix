using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Storage;
using Trimix.Core.Connectors.Export;

namespace Trimix.App.Services;

/// <summary>
/// Real <see cref="IExportFileSink"/> for the clean file-export destination (UDDF / DL7 / PDF). Writes the
/// rendered bytes to the app cache directory and hands the file to the OS save/share sheet via the
/// cross-platform <c>Microsoft.Maui.ApplicationModel.DataTransfer.Share</c> API — on iOS this surfaces the
/// <c>UIActivityViewController</c> (AirDrop · Save to Files · Mail · …); on Android the <c>ACTION_SEND</c>
/// chooser (Drive · Files/"Save to device" · Gmail · …). No cloud, no credentials, no ToS: the diver owns
/// the file and routes it (<see cref="Trimix.Core.Abstractions.LegalPosture.UserFile"/>).
///
/// <para>Register in MauiProgram BEFORE <c>AddTrimixCore</c> so Core's <see cref="InMemoryExportFileSink"/>
/// <c>TryAdd</c> fallback never engages on device:
/// <c>builder.Services.AddSingleton&lt;IExportFileSink, MauiExportFileSink&gt;();</c></para>
///
/// <para><b>Override hook (labelled, deploy-optional).</b> A "Save directly to a folder I pick" path over
/// Android's Storage Access Framework <c>ACTION_CREATE_DOCUMENT</c> / iOS <c>UIDocumentPicker</c> export
/// mode is a future extension: it needs an Activity / presentation-context result round-trip, so it is
/// intentionally NOT wired here. The share sheet above already exposes "Save to Files" (iOS) and
/// "Save to device"/Drive (Android) on both platforms, which covers the save-to-disk case without that
/// round-trip. When the SAF path is added, it slots in behind this same port with no Core change.</para>
/// </summary>
public sealed class MauiExportFileSink : IExportFileSink
{
    public async Task<string> WriteAsync(ExportedDive file, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(file);

        // Cache dir: OS-managed, exposable to other apps (Android FileProvider content:// URI, iOS
        // file:// URL) and reclaimable — an export is a transient artifact the diver forwards onward,
        // never app state. The deterministic ExportNaming file name (trimix-dive-<utc>.<ext>) is reused.
        var path = Path.Combine(FileSystem.CacheDirectory, file.FileName);
        await System.IO.File.WriteAllBytesAsync(path, [.. file.Content], ct).ConfigureAwait(false);

        // Presenting the share sheet / activity controller must run on the UI thread on both platforms.
        await MainThread.InvokeOnMainThreadAsync(() =>
            Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = file.FileName,
                File = new ShareFile(path, file.ContentType),
            })).ConfigureAwait(false);

        return path;
    }
}
