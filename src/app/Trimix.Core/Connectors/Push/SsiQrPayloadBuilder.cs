using System.Globalization;
using Trimix.Contracts.Dtos;

namespace Trimix.Core.Connectors.Push;

/// <summary>
/// Builds the client-side, diver-mediated MySSI QR-import payload (research §"SSI", destReports). This is
/// the LOWEST-risk SSI path: it contacts no SSI server and handles no credentials — the diver scans the
/// resulting QR in their own logged-in MySSI app. The payload is a reverse-engineered, semicolon-delimited
/// <c>key:value</c> blob and is <b>summary-only</b>: date/time, duration (minutes) and max depth (metres).
///
/// SSI-internal ids (<c>site</c>, <c>user_master_id</c>) are unobtainable by a third party and are
/// deliberately OMITTED — the pushed dive lands without a linked site and needs manual correction in MySSI.
/// The format is undocumented and SSI can break it at any time; surface a lossy/summary-only disclosure in
/// the UI. Pure — no I/O, fully unit-testable.
/// </summary>
public sealed class SsiQrPayloadBuilder
{
    public string Build(DiveDto dive)
    {
        ArgumentNullException.ThrowIfNull(dive);

        // Only keys attested in the reverse-engineered example payload are emitted.
        var parts = new List<string>
        {
            "dive",
            "noid",
            "dive_type:0",
            "datetime:" + dive.StartUtc.ToUniversalTime().ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture),
            "divetime:" + Round(dive.DurationSec / 60.0),
            "depth_m:" + Round(dive.MaxDepthM),
        };

        return string.Join(';', parts);
    }

    private static string Round(double value) =>
        ((int)Math.Round(value, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);
}
