namespace Trimix.Core.Connectors.Push;

/// <summary>
/// Encodes a short text payload (the diver-mediated MySSI QR blob from <see cref="SsiQrPayloadBuilder"/>) into
/// a <see cref="QrMatrix"/>. Kept behind a port so a vetted QR NuGet can replace the in-repo
/// <see cref="BasicQrEncoder"/> later via the App's <c>.csproj</c> without touching the viewmodel or the
/// drawable.
/// </summary>
public interface IQrEncoder
{
    /// <summary>Encodes <paramref name="text"/> to a QR module grid. Throws when the payload cannot fit the encoder's supported range.</summary>
    QrMatrix Encode(string text);
}
