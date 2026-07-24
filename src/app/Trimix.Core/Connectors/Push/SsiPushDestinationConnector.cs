using System.Globalization;
using Trimix.Contracts.Dtos;
using Trimix.Core.Abstractions;

namespace Trimix.Core.Connectors.Push;

/// <summary>
/// Gated, founder-directed SSI (MySSI) personal-export push (research §2 Bucket-C; docs/known-gaps.md
/// legal override). SSI has no partner write API; its private DiveSSI cloud takes credentials as query
/// params, and writes are summary-only. Ships DARK (<see cref="LegalPosture.ToSRiskReverseEng"/>,
/// <c>EnabledByDefault=false</c>) with all consent/token/label guardrails from
/// <see cref="GatedPushConnectorBase"/>. The lowest-risk SSI path is the diver-mediated QR export
/// (<see cref="SsiQrPayloadBuilder"/>); this cloud connector exists only for the legal-override track.
/// Truthful id: <c>ssi_push_personal</c>.
/// </summary>
public sealed class SsiPushDestinationConnector(
    IConnectorConsentStore consent,
    ISecureTokenStore tokens,
    IHttpTransport http,
    SsiPushOptions options)
    : GatedPushConnectorBase(consent, tokens, http)
{
    public override ConnectorDescriptor Descriptor { get; } = new(
        ConnectorId: "ssi_push_personal",
        Kind: ConnectorKind.CloudApi,
        Legal: LegalPosture.ToSRiskReverseEng,
        Fidelity: ProfileFidelity.SummaryOnly,
        Auth: AuthModel.UserCredential,
        Platforms: Platforms.Both,
        EnabledByDefault: false);

    protected override string DisclosureMessage =>
        "SSI (MySSI) publishes no logbook API. This is a user-assisted personal export to SSI's private " +
        "cloud — NOT an official SSI integration. It sends summary data only (no profile, no dive site), " +
        "may break without notice, and could conflict with SSI's terms. Your SSI password is used once to " +
        "obtain a token and is never stored. The lower-risk option is the diver-scanned QR export.";

    protected override async Task<string?> ExchangeCredentialsAsync(string username, string password, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.AuthUrl))
        {
            return null; // No configured endpoint — stay dark.
        }

        // Private DiveSSI cloud takes credentials as l/p query params (research constraint of the vendor's
        // undocumented endpoint — not our choice). EXPOSURE NOTE: a password in the request URL is higher-risk
        // than a body (proxies/edge/access logs capture full URLs), so this connector MUST NOT have any
        // request-URL logging attached to its transport, and it stays dark (AuthUrl null) until a legal-cleared
        // endpoint is configured. Tracked in docs/known-gaps.md; the QR export is the lower-risk SSI path.
        var sep = options.AuthUrl!.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        var url = string.Create(
            CultureInfo.InvariantCulture,
            $"{options.AuthUrl}{sep}l={Uri.EscapeDataString(username)}&p={Uri.EscapeDataString(password)}");

        var request = new HttpExchangeRequest(
            "GET",
            url,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            Body: null);

        var response = await Http.SendAsync(request, ct).ConfigureAwait(false);
        return IsSuccess(response.StatusCode) ? ExtractToken(response.Body) : null;
    }

    protected override async Task<PushResult> PushCoreAsync(DiveDto dive, string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.PushUrl))
        {
            return new PushResult(
                PushStatus.Unsupported,
                Descriptor.ConnectorId,
                "No sanctioned SSI write endpoint. Use the QR export or a UDDF/PDF file instead.");
        }

        // Summary-only payload (research: SSI write carries date/duration/depth/temp; no profile/site).
        var payload = string.Create(
            CultureInfo.InvariantCulture,
            $"{{\"datetime\":\"{dive.StartUtc.ToUniversalTime():yyyyMMddHHmm}\"," +
            $"\"divetime\":{(int)Math.Round(dive.DurationSec / 60.0, MidpointRounding.AwayFromZero)}," +
            $"\"depth_m\":{dive.MaxDepthM.ToString("0.##", CultureInfo.InvariantCulture)}," +
            $"\"temp_c\":{(dive.MinTempC is { } t ? t.ToString("0.##", CultureInfo.InvariantCulture) : "null")}}}");

        var request = new HttpExchangeRequest(
            "POST",
            options.PushUrl!,
            BearerHeaders(token, "application/json"),
            payload);

        var response = await Http.SendAsync(request, ct).ConfigureAwait(false);
        return IsSuccess(response.StatusCode)
            ? new PushResult(PushStatus.Pushed, Descriptor.ConnectorId, "Uploaded to SSI (summary fidelity, no site).")
            : new PushResult(PushStatus.Failed, Descriptor.ConnectorId, $"SSI sync returned {response.StatusCode}.");
    }
}
