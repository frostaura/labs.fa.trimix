using System.Net;
using System.Text;
using System.Text.Json;

namespace Trimix.Harness;

/// <summary>
/// A live probe of Garmin's private Dive API, reverse-engineered from an iOS Garmin Dive 3.6.1 capture
/// (mitmproxy, 2026-07-30). This is the discovery instrument: it replays the observed flow verbatim so the
/// real request/response shapes can be recorded, then folded into
/// <c>Trimix.Core/Connectors/Cloud/GarminCloudSourceConnector.cs</c>.
///
/// <para><b>The observed flow.</b> Two halves, deliberately separated:</para>
/// <list type="number">
///   <item><description><b>Ticket acquisition (Cloudflare-guarded).</b> The real app loads
///   <c>sso.garmin.com/mobile/sso/…</c> in a WebView; Cloudflare mints <c>cf_clearance</c> by executing a JS
///   challenge, and only then does <c>POST /mobile/api/login</c> succeed, returning a single-use service
///   ticket (<c>ST-…-sso</c>). A bare HttpClient cannot mint <c>cf_clearance</c>, so
///   <see cref="TryRawLoginAsync"/> is expected to fail — it exists to establish *whether* it fails, since
///   Cloudflare's aggressiveness varies. In the app this half belongs to a WebView.</description></item>
///   <item><description><b>Everything after the ticket</b> is plain HTTP with no bot protection:
///   <see cref="ExchangeTicketAsync"/> then <see cref="GetAsync"/>. This is the half the connector owns.</description></item>
/// </list>
/// </summary>
public sealed class GarminDiveProbe : IDisposable
{
    // Captured verbatim from Garmin Dive 3.6.1 (iOS 27.0, iPhone18,4). The client ids differ per leg —
    // DIVE_IOS_DARK for the SSO web page, DIVE_MOBILE_IOS_DI for the token exchange.
    private const string SsoClientId = "DIVE_IOS_DARK";
    private const string OAuthClientId = "DIVE_MOBILE_IOS_DI";
    private const string ServiceUrl = "https://mobile.integration.garmin.com/dive/ios";

    private const string SsoBase = "https://sso.garmin.com";
    private const string TokenUrl = "https://diauth.garmin.com/di-oauth2-service/oauth/token";
    private const string GcsBase = "https://gcs.garmin.com";

    /// <summary>The grant_type is itself a URL — Garmin's private service-ticket grant.</summary>
    private const string TicketGrantType =
        "https://connectapi.garmin.com/di-oauth2-service/oauth/grant/service_ticket";

    // The WebView's UA for the SSO leg; the native app's UA for the API legs. Sending the wrong one on the
    // wrong leg is a plausible bot-detection trigger, so they are kept distinct.
    private const string WebViewUserAgent =
        "Mozilla/5.0 (iPhone; CPU iPhone OS 18_7 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Mobile/15E148";
    private const string NativeUserAgent = "DIVE-iOS-3.6.1.1";
    private const string AlamofireUserAgent =
        "Dive/3.6.1 (com.garmin.Dive; build:1; iOS 27.0.0) Alamofire/5.9.1";
    private const string GarminUserAgent =
        "com.garmin.Dive/3.6.1.1;;Apple/iPhone18,4/;iOS/27.0;CFNetwork/1.0(Darwin/27.0.0)";

    private readonly HttpClient _http;
    private readonly CookieContainer _cookies = new();

    public GarminDiveProbe(string? proxy = null)
    {
        var handler = new HttpClientHandler
        {
            CookieContainer = _cookies,
            UseCookies = true,
            // Garmin gzips almost everything. Without this the body arrives compressed and any
            // UTF-8 decode produces mojibake (the exact bug the capture addon first shipped with).
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true,
        };

        if (!string.IsNullOrWhiteSpace(proxy))
        {
            handler.Proxy = new WebProxy(proxy);
            handler.UseProxy = true;
            // Only relevant when routing through mitmproxy for observation.
            handler.ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }

        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
    }

    private static string SignInPageUrl =>
        $"{SsoBase}/mobile/sso/en/sign-in?clientId={SsoClientId}&service={Uri.EscapeDataString(ServiceUrl)}";

    private static string Referer =>
        $"{SsoBase}/mobile/sso/en-US/sign-in?clientId={SsoClientId}&service={ServiceUrl}";

    /// <summary>
    /// Attempts the whole ticket-acquisition half with a bare HttpClient: load the SSO page for cookies,
    /// call <c>checkLogin</c>, then POST credentials. Returns the raw login response so the ticket field
    /// name can be read off it. Expected to be rejected by Cloudflare — we want the evidence either way.
    /// </summary>
    public async Task<ProbeStep> TryRawLoginAsync(string username, string password, CancellationToken ct = default)
    {
        // 1. GET the sign-in page — this is what normally sets SESSION / __cf_bm / __cflb.
        var page = await SendAsync(
            HttpMethod.Get, SignInPageUrl,
            new() { ["accept"] = "text/html,application/xhtml+xml", ["user-agent"] = WebViewUserAgent },
            body: null, contentType: null, ct).ConfigureAwait(false);
        if (!page.Ok)
        {
            return page with { Note = "SSO sign-in page rejected — Cloudflare blocked before login was even attempted." };
        }

        // 2. checkLogin — the app always calls this before POSTing credentials.
        var checkUrl = $"{SsoBase}/mobile/api/checkLogin?clientId={SsoClientId}&locale=en-US"
                     + $"&service={Uri.EscapeDataString(ServiceUrl)}";
        await SendAsync(
            HttpMethod.Get, checkUrl,
            new()
            {
                ["accept"] = "application/json, text/plain, */*",
                ["user-agent"] = WebViewUserAgent,
                ["referer"] = Referer,
                ["origin"] = SsoBase,
            },
            body: null, contentType: null, ct).ConfigureAwait(false);

        // 3. POST the credentials as JSON (NOT a form — the old connector's form+CSRF guess was wrong).
        var loginUrl = $"{SsoBase}/mobile/api/login?clientId={SsoClientId}&locale=en-US"
                     + $"&service={Uri.EscapeDataString(ServiceUrl)}";
        var payload = JsonSerializer.Serialize(new
        {
            username,
            password,
            rememberMe = false,
            captchaToken = "",
        });

        var login = await SendAsync(
            HttpMethod.Post, loginUrl,
            new()
            {
                ["accept"] = "application/json, text/plain, */*",
                ["user-agent"] = WebViewUserAgent,
                ["referer"] = Referer,
                ["origin"] = SsoBase,
            },
            payload, "application/json", ct).ConfigureAwait(false);

        return login with
        {
            Note = login.Ok
                ? "Raw login SUCCEEDED — Cloudflare did not challenge. A WebView may not be strictly required."
                : "Raw login rejected. Expected: cf_clearance is minted by a JS challenge a WebView must run.",
        };
    }

    /// <summary>
    /// Exchanges a single-use service ticket (<c>ST-…-sso</c>) for the RS256 bearer JWT that authorizes
    /// every <c>gcs.garmin.com/diving</c> call. This leg has no bot protection — it is the connector's real entry point.
    /// </summary>
    public async Task<ProbeStep> ExchangeTicketAsync(string serviceTicket, CancellationToken ct = default)
    {
        var form = $"grant_type={Uri.EscapeDataString(TicketGrantType)}"
                 + $"&service_ticket={Uri.EscapeDataString(serviceTicket)}"
                 + $"&service_url={Uri.EscapeDataString(ServiceUrl)}"
                 + $"&client_id={OAuthClientId}";

        // Basic auth is the client id with an EMPTY secret: base64("DIVE_MOBILE_IOS_DI:").
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{OAuthClientId}:"));

        return await SendAsync(
            HttpMethod.Post, TokenUrl,
            new()
            {
                ["accept"] = "*/*",
                ["authorization"] = $"Basic {basic}",
                ["user-agent"] = NativeUserAgent,
                ["x-garmin-user-agent"] = GarminUserAgent,
                ["garmin-client-platform"] = "iOS",
                ["garmin-client-platform-version"] = "27.0",
                ["x-app-ver"] = "3.6.1",
                ["x-lang"] = "en",
            },
            form, "application/x-www-form-urlencoded", ct).ConfigureAwait(false);
    }

    /// <summary>
    /// GETs any <c>gcs.garmin.com</c> diving path with the bearer JWT, mimicking the app's Alamofire client.
    /// <paramref name="path"/> is relative, e.g. <c>/diving/v1/dive/summary?requestedPage=0&amp;resultsPerPage=100</c>.
    /// </summary>
    public Task<ProbeStep> GetAsync(string path, string jwt, CancellationToken ct = default) =>
        SendAsync(
            HttpMethod.Get, $"{GcsBase}{path}",
            new()
            {
                ["accept"] = "application/json",
                // Lowercase "bearer" is what the app sends; Garmin accepts it, so stay faithful.
                ["authorization"] = $"bearer {jwt}",
                ["user-agent"] = AlamofireUserAgent,
                ["x-app-ver"] = "3.6.1",
                ["x-lang"] = "en",
            },
            body: null, contentType: null, ct);

    private async Task<ProbeStep> SendAsync(
        HttpMethod method,
        string url,
        Dictionary<string, string> headers,
        string? body,
        string? contentType,
        CancellationToken ct)
    {
        using var msg = new HttpRequestMessage(method, url);
        foreach (var (k, v) in headers)
        {
            msg.Headers.TryAddWithoutValidation(k, v);
        }

        if (body is not null)
        {
            msg.Content = new StringContent(body, Encoding.UTF8);
            msg.Content.Headers.Remove("Content-Type");
            msg.Content.Headers.TryAddWithoutValidation("Content-Type", contentType ?? "application/json");
        }

        using var res = await _http.SendAsync(msg, ct).ConfigureAwait(false);
        var text = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return new ProbeStep($"{method} {url}", (int)res.StatusCode, text);
    }

    public void Dispose() => _http.Dispose();
}

/// <summary>One probed exchange: what was called, the status, and the raw body for shape discovery.</summary>
public sealed record ProbeStep(string What, int Status, string Body, string? Note = null)
{
    public bool Ok => Status is >= 200 and < 300;

    /// <summary>Pulls a value out of a JSON body without binding a model we have not yet designed.</summary>
    public string? JsonString(params string[] candidateNames)
    {
        if (string.IsNullOrWhiteSpace(Body))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(Body);
            foreach (var name in candidateNames)
            {
                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty(name, out var el)
                    && el.ValueKind == JsonValueKind.String)
                {
                    return el.GetString();
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }
}
