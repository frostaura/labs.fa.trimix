namespace Trimix.Core.Connectors.Fit;

/// <summary>
/// A named byte payload handed to a dive-file parser. Originally the platform file-picker result; the
/// file-picker path is removed (serverless / API-sync-only), and this record survives as the input shape
/// of the retained FIT decode chain — the cloud connectors (Garmin, Suunto) download original
/// <c>.FIT</c> bytes and wrap them in a synthetic <see cref="PickedFile"/> via
/// <c>Trimix.Core.Connectors.Infra.FitActivityParser</c>. <paramref name="FileName"/> carries the name so
/// the sniffer can use its extension; <paramref name="Contents"/> is the raw bytes (FIT is binary).
/// </summary>
public sealed record PickedFile(string FileName, byte[] Contents);
