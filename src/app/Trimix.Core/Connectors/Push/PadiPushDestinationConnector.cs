using System.Text;
using Trimix.Contracts.Dtos;
using Trimix.Core.Abstractions;
using Trimix.Core.Connectors.Export;

namespace Trimix.Core.Connectors.Push;

/// <summary>
/// Gated, founder-directed PADI personal-export push (research §2 Bucket-C; docs/known-gaps.md legal
/// override). PADI has no logbook API and only ever ingested DL7, so the payload is a DL7 rendering and
/// the target endpoint is config-supplied (never guessed). Ships DARK
/// (<see cref="LegalPosture.ToSRiskReverseEng"/>, <c>EnabledByDefault=false</c>); all consent/token/label
/// guardrails come from <see cref="GatedPushConnectorBase"/>. Fidelity is summary-only. Truthful id:
/// <c>padi_push_personal</c> — a user-assisted personal export, NOT an official PADI integration.
/// </summary>
public sealed class PadiPushDestinationConnector(
    IConnectorConsentStore consent,
    ISecureTokenStore tokens,
    IHttpTransport http,
    PadiPushOptions options,
    Dl7DiveExporter dl7)
    : GatedPushConnectorBase(consent, tokens, http)
{
    public override ConnectorDescriptor Descriptor { get; } = new(
        ConnectorId: "padi_push_personal",
        Kind: ConnectorKind.CloudApi,
        Legal: LegalPosture.ToSRiskReverseEng,
        Fidelity: ProfileFidelity.SummaryOnly,
        Auth: AuthModel.UserCredential,
        Platforms: Platforms.Both,
        EnabledByDefault: false);

    protected override string DisclosureMessage =>
        "PADI publishes no logbook API. This is a user-assisted personal export to PADI's private sync " +
        "backend — NOT an official PADI integration. It may break without notice and could conflict with " +
        "PADI's terms of service. Your PADI password is used once to obtain a token and is never stored. " +
        "Where possible, prefer the clean UDDF/DL7 file export you upload yourself.";

    protected override async Task<string?> ExchangeCredentialsAsync(string username, string password, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.AuthUrl))
        {
            return null; // No sanctioned/configured endpoint — stay dark.
        }

        var body = $"{{\"username\":{JsonString(username)},\"password\":{JsonString(password)}}}";
        var request = new HttpExchangeRequest(
            "POST",
            options.AuthUrl!,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Content-Type"] = "application/json" },
            body);

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
                "No sanctioned PADI write endpoint. Export a UDDF/DL7 file and upload it to PADI yourself.");
        }

        var dl7Payload = Encoding.ASCII.GetString([.. dl7.Export(dive).Content]);
        var request = new HttpExchangeRequest(
            "POST",
            options.PushUrl!,
            BearerHeaders(token, "application/octet-stream"),
            dl7Payload);

        var response = await Http.SendAsync(request, ct).ConfigureAwait(false);
        return IsSuccess(response.StatusCode)
            ? new PushResult(PushStatus.Pushed, Descriptor.ConnectorId, "Uploaded to PADI (summary fidelity).")
            : new PushResult(PushStatus.Failed, Descriptor.ConnectorId, $"PADI sync returned {response.StatusCode}.");
    }

    private static string JsonString(string value)
    {
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                default:
                    sb.Append(ch);
                    break;
            }
        }

        sb.Append('"');
        return sb.ToString();
    }
}
