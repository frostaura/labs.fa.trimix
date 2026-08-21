using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using Trimix.Core.Abstractions;
using Trimix.Core.Connectors.Cloud;

namespace Trimix.Core.Connectors.Infra;

/// <summary>
/// The four public identifiers that pin a Firebase-backed vendor. All of them are embedded in the vendor's
/// app binary and are <b>not secrets</b> — the Web <see cref="ApiKey"/> is bundle-restricted, which is why
/// <see cref="BundleId"/> and <see cref="GmpAppId"/> must be presented as headers on the keyed auth calls.
///
/// <para>This record is the entire per-vendor surface: point it at a different project and the same
/// <see cref="FirebaseCloudClient"/> serves any Firebase email/password + Firestore vendor. The Google
/// endpoint hosts default to the real ones and exist as overrides only so a Google-side change can be
/// absorbed by deploy config rather than a release (mirroring <see cref="CloudSourceOptions"/> for Garmin).</para>
/// </summary>
public sealed record FirebaseAppConfig(
    string ProjectId,
    string ApiKey,
    string GmpAppId,
    string BundleId,
    string? SignInUrl = null,
    string? SecureTokenUrl = null,
    string? FirestoreBaseUrl = null)
{
    // Observed verbatim from LogTRAK 2 v1.13.1 (iOS 27). The app uses the legacy relyingparty surface;
    // it is interchangeable with the modern accounts:signInWithPassword. These are the REAL Google hosts —
    // the overrides above exist so a Google-side change is a config change, never a guess.
    public string ResolvedSignInUrl =>
        SignInUrl ?? $"https://www.googleapis.com/identitytoolkit/v3/relyingparty/verifyPassword?key={ApiKey}";

    public string ResolvedSecureTokenUrl =>
        SecureTokenUrl ?? $"https://securetoken.googleapis.com/v1/token?key={ApiKey}";

    public string ResolvedFirestoreDocuments =>
        (FirestoreBaseUrl ?? $"https://firestore.googleapis.com/v1/projects/{ProjectId}/databases/(default)/documents")
            .TrimEnd('/');
}

/// <summary>A signed-in Firebase session: the 1-hour bearer id token, the long-lived refresh token, the
/// absolute expiry, and the Firestore <c>userId</c> every owned-document query filters on.</summary>
public sealed record FirebaseSession(string IdToken, string? RefreshToken, DateTimeOffset ExpiresAt, string Uid);

/// <summary>One Firestore document: its short id (the last <c>name</c> path segment) and its <c>fields</c>
/// object, detached from the response document so it stays valid after parsing.</summary>
public sealed record FirestoreDocument(string Id, JsonElement Fields);

/// <summary>
/// Reusable reverse-engineered <b>read</b> client for any Firebase (email/password Auth + Cloud Firestore)
/// vendor. Introduced for ScubaPro/Uwatec LogTRAK (project <c>logtrak2-production</c>) — see
/// <c>docs/reverse-engineering/scubapro-logtrak-api.md</c>, the contract this implements — and deliberately
/// vendor-agnostic so a second Firebase provider is a new <see cref="FirebaseAppConfig"/>, not a new client.
///
/// <para><b>Why a direct REST client rather than a proxy capture.</b> The native Firestore SDK speaks gRPC,
/// which ignores the system HTTP proxy, so dive traffic never appears on the wire; the Firestore REST surface
/// used here is the stable, documented equivalent. All I/O goes through <see cref="IHttpTransport"/>, so the
/// whole sign-in → refresh → query flow is offline-testable and consent-gating stays enforceable by the
/// calling connector.</para>
///
/// <para><b>Token discipline.</b> Stateless about credentials: <see cref="SignInAsync"/> performs the one-shot
/// password exchange and returns a <see cref="FirebaseSession"/>; persistence is the calling connector's job
/// (its <c>GatedCloudSourceConnector</c> base writes only the token envelope, never the password).</para>
/// </summary>
public sealed class FirebaseCloudClient(IHttpTransport http, FirebaseAppConfig config)
{
    /// <summary>
    /// Signs in with email + password. The bundle/GMP headers are mandatory: the Web API key is
    /// app-restricted and a call without them returns <c>403 API_KEY_IOS_APP_BLOCKED</c>.
    /// </summary>
    public async Task<FirebaseSession> SignInAsync(string email, string password, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new
        {
            email,
            password,
            returnSecureToken = true,
            clientType = "CLIENT_TYPE_IOS",
        });

        var response = await http.SendAsync(new HttpExchangeRequest(
            "POST", config.ResolvedSignInUrl, KeyedAuthHeaders(), payload), ct).ConfigureAwait(false);

        if (!response.IsSuccess)
        {
            // Distinguish a wrong password (which the diver can fix) from throttling or an outage (which they
            // cannot), so we never send someone to reset a password that was never the problem.
            throw new ConnectorAuthException(response.StatusCode switch
            {
                400 or 401 => "Sign-in was rejected — check your LogTRAK (ScubaPro) email and password.",
                403 => "Sign-in was blocked by the provider. This usually clears on its own; try again shortly.",
                429 => "The provider is temporarily rate-limiting sign-in attempts. Wait a moment and try again.",
                >= 500 => $"The provider's sign-in service is unavailable right now (HTTP {response.StatusCode}).",
                _ => $"Sign-in failed (HTTP {response.StatusCode}).",
            });
        }

        using var doc = ParseJson(response.Body, "sign-in");
        var root = doc.RootElement;

        var idToken = ReadString(root, "idToken");
        var uid = ReadString(root, "localId");
        if (string.IsNullOrEmpty(idToken) || string.IsNullOrEmpty(uid))
        {
            throw new ConnectorAuthException("Sign-in returned no session token (the login flow may have changed).");
        }

        var refresh = ReadString(root, "refreshToken");
        return new FirebaseSession(idToken, refresh, ExpiryFrom(root, "expiresIn"), uid);
    }

    /// <summary>Exchanges a refresh token for a fresh id token. The refresh token may rotate — the returned
    /// session carries whatever the provider sent back, and the caller must persist it.</summary>
    public async Task<FirebaseSession> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(refreshToken);

        var body = JsonSerializer.Serialize(new { grant_type = "refresh_token", refresh_token = refreshToken });
        var response = await http.SendAsync(new HttpExchangeRequest(
            "POST", config.ResolvedSecureTokenUrl, KeyedAuthHeaders(), body), ct).ConfigureAwait(false);

        if (!response.IsSuccess)
        {
            throw new ConnectorAuthException($"Session refresh failed (HTTP {response.StatusCode}).");
        }

        using var doc = ParseJson(response.Body, "session refresh");
        var root = doc.RootElement;

        // securetoken returns snake_case (id_token / refresh_token / expires_in / user_id).
        var idToken = ReadString(root, "id_token");
        var uid = ReadString(root, "user_id");
        if (string.IsNullOrEmpty(idToken) || string.IsNullOrEmpty(uid))
        {
            throw new ConnectorAuthException("Session refresh returned no token.");
        }

        var refresh = ReadString(root, "refresh_token") ?? refreshToken;
        return new FirebaseSession(idToken, refresh, ExpiryFrom(root, "expires_in"), uid);
    }

    /// <summary>
    /// Runs a Firestore <c>:runQuery</c> over one collection filtered to <c>field == value</c>, newest-first,
    /// and returns each matched document's <c>fields</c> object. This is the load-bearing call: Firestore
    /// security rules forbid an unfiltered collection <em>list</em>, but permit a diver to query their own
    /// rows, so every owned-collection read is expressed as this equality filter.
    /// </summary>
    public async Task<IReadOnlyList<FirestoreDocument>> QueryOwnedAsync(
        string idToken,
        string collectionId,
        string ownerField,
        string ownerValue,
        string? orderByField = null,
        int? limit = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(idToken);

        var query = new Dictionary<string, object?>
        {
            ["from"] = new[] { new { collectionId } },
            ["where"] = new
            {
                fieldFilter = new
                {
                    field = new { fieldPath = ownerField },
                    op = "EQUAL",
                    value = new { stringValue = ownerValue },
                },
            },
        };
        if (orderByField is not null)
        {
            query["orderBy"] = new[]
            {
                new { field = new { fieldPath = orderByField }, direction = "DESCENDING" },
            };
        }

        if (limit is { } l)
        {
            query["limit"] = l;
        }

        var payload = JsonSerializer.Serialize(new { structuredQuery = query });
        var response = await http.SendAsync(new HttpExchangeRequest(
            "POST", $"{config.ResolvedFirestoreDocuments}:runQuery", Bearer(idToken), payload), ct)
            .ConfigureAwait(false);

        if (!response.IsSuccess)
        {
            // Firestore reports the concrete cause (missing index, denied by rules) in the body; surface a
            // snippet so a future breakage is diagnosable without a re-capture.
            var detail = response.Body is { Length: > 0 } body ? $" — {Snippet(body)}" : string.Empty;
            throw new InvalidOperationException(
                $"Firestore query on '{collectionId}' failed (HTTP {response.StatusCode}){detail}.");
        }

        using var doc = ParseJson(response.Body, $"Firestore {collectionId} query");
        var documents = new List<FirestoreDocument>();

        // runQuery streams an array of { document?: {...}, readTime, ... } — skip stream frames with no document.
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var frame in doc.RootElement.EnumerateArray())
            {
                if (frame.TryGetProperty("document", out var document)
                    && document.TryGetProperty("fields", out var fields)
                    && ReadString(document, "name") is { Length: > 0 } name)
                {
                    // Clone() detaches the fields from `doc` so they survive its disposal.
                    documents.Add(new FirestoreDocument(name[(name.LastIndexOf('/') + 1)..], fields.Clone()));
                }
            }
        }

        return documents;
    }

    /// <summary>Headers for the API-key-restricted auth calls — the bundle id is what unblocks the key.</summary>
    private Dictionary<string, string> KeyedAuthHeaders() => new()
    {
        ["Content-Type"] = "application/json",
        ["Accept"] = "application/json",
        ["X-Ios-Bundle-Identifier"] = config.BundleId,
        ["X-Firebase-Gmpid"] = config.GmpAppId,
    };

    private static Dictionary<string, string> Bearer(string idToken) => new()
    {
        ["Authorization"] = $"Bearer {idToken}",
        ["Content-Type"] = "application/json",
        ["Accept"] = "application/json",
    };

    private static DateTimeOffset ExpiryFrom(JsonElement root, string property)
    {
        // Both surfaces send the lifetime as a STRING count of seconds ("3600").
        var seconds = root.TryGetProperty(property, out var el)
            && (el.ValueKind == JsonValueKind.String
                ? long.TryParse(el.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s)
                : el.TryGetInt64(out s))
                ? s
                : 3600;
        return DateTimeOffset.UtcNow.AddSeconds(seconds);
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var el)
        && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    /// <summary>A single-line, length-capped view of a response body for error messages.</summary>
    private static string Snippet(string body)
    {
        var oneLine = body.ReplaceLineEndings(" ").Trim();
        return oneLine.Length > 300 ? oneLine[..300] + "…" : oneLine;
    }

    private static JsonDocument ParseJson(string body, string what)
    {
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new ConnectorAuthException($"{what} returned a response that is not JSON: {ex.Message}");
        }
    }
}
