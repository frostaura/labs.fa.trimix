using System.Buffers.Binary;
using System.Buffers.Text;
using System.Formats.Cbor;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Trimix.Api.Endpoints;
using Trimix.Contracts;
using Trimix.Contracts.Dtos;
using Xunit;

namespace Trimix.Api.Tests;

/// <summary>
/// Exercises the server-side WebAuthn/FIDO2 relying party end-to-end against a SOFTWARE authenticator
/// (an in-process ES256 key that produces the exact attestation/assertion JSON the platform would).
/// This is the honest, hermetic substitute for a real device: it proves the server's begin→finish
/// round-trips, the WebAuthn JSON contract, the frozen <c>account_created</c> funnel emission on the
/// passkey register path, and clone-detection counter advance — none of which a simulator/emulator
/// against localhost could ever run (real passkeys require an associated HTTPS relying-party domain).
/// </summary>
public sealed class PasskeyTests : IDisposable
{
    private readonly TrimixApiFactory _factory = new();

    // The default relying party (Program.cs / PasskeyOptions): RP id "localhost", origin "https://localhost".
    private const string Origin = "https://localhost";

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Register_then_login_roundtrip_and_account_created_funnel_via_passkey()
    {
        const string pseudoId = "pseudo-passkey-device";
        var client = _factory.CreateAnonymousClient(pseudoId);
        var authenticator = new SoftwareAuthenticator(Origin);

        // ---------- register/begin → software attestation → register/finish ----------
        var beginResponse = await client.PostAsJsonAsync(
            ApiRoutes.Auth.PasskeyRegisterBeginFull, new PasskeyRegisterBeginRequest("Reef Diver"));
        beginResponse.EnsureSuccessStatusCode();
        var begin = await beginResponse.Content.ReadFromJsonAsync<PasskeyRegisterBeginResponse>();
        Assert.NotNull(begin);

        var attestationJson = authenticator.CreateAttestation(begin.CreationOptionsJson);
        var finishResponse = await client.PostAsJsonAsync(
            ApiRoutes.Auth.PasskeyRegisterFinishFull, new PasskeyRegisterFinishRequest(begin.ChallengeId, attestationJson));
        finishResponse.EnsureSuccessStatusCode();
        var registered = await finishResponse.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(registered);
        Assert.False(string.IsNullOrWhiteSpace(registered.Token));
        Assert.Equal(UserRoles.User, registered.User.Role);

        // The passkey register path emitted the FROZEN account_created funnel event (auth_provider=passkey).
        var events = _factory.EventNamesFor(pseudoId);
        Assert.Contains(FunnelEvents.AccountCreated, events);
        var authProvider = _factory.WithDb(db => db.AnalyticsEvents
            .Where(e => e.Name == FunnelEvents.AccountCreated)
            .Select(e => e.PropsJson)
            .Single());
        Assert.Contains(PasskeyEndpoints.PasskeyAuthProvider, authProvider);

        // The freshly-issued token authenticates against a protected endpoint.
        var meResponse = await AuthorizedGet(registered.Token, ApiRoutes.Entitlements.MeFull);
        meResponse.EnsureSuccessStatusCode();

        // ---------- login/begin → software assertion → login/finish (usernameless) ----------
        var loginBeginResponse = await client.PostAsync(ApiRoutes.Auth.PasskeyLoginBeginFull, content: null);
        loginBeginResponse.EnsureSuccessStatusCode();
        var loginBegin = await loginBeginResponse.Content.ReadFromJsonAsync<PasskeyLoginBeginResponse>();
        Assert.NotNull(loginBegin);

        var assertionJson = authenticator.CreateAssertion(loginBegin.RequestOptionsJson);
        var loginFinishResponse = await client.PostAsJsonAsync(
            ApiRoutes.Auth.PasskeyLoginFinishFull, new PasskeyLoginFinishRequest(loginBegin.ChallengeId, assertionJson));
        loginFinishResponse.EnsureSuccessStatusCode();
        var loggedIn = await loginFinishResponse.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(loggedIn);

        // Usernameless login resolved the SAME account the passkey was registered to.
        Assert.Equal(registered.User.Id, loggedIn.User.Id);
        var loginMe = await AuthorizedGet(loggedIn.Token, ApiRoutes.Entitlements.MeFull);
        loginMe.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Login_finish_with_unknown_credential_is_unauthorized()
    {
        var client = _factory.CreateAnonymousClient();

        // A brand-new authenticator that was never registered on the server.
        var authenticator = new SoftwareAuthenticator(Origin);
        authenticator.SeedUnregisteredHandle();

        var loginBegin = await (await client.PostAsync(ApiRoutes.Auth.PasskeyLoginBeginFull, content: null))
            .Content.ReadFromJsonAsync<PasskeyLoginBeginResponse>();
        Assert.NotNull(loginBegin);

        var assertionJson = authenticator.CreateAssertion(loginBegin.RequestOptionsJson);
        var response = await client.PostAsJsonAsync(
            ApiRoutes.Auth.PasskeyLoginFinishFull, new PasskeyLoginFinishRequest(loginBegin.ChallengeId, assertionJson));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Finish_with_unknown_challenge_is_rejected()
    {
        var client = _factory.CreateAnonymousClient();
        var response = await client.PostAsJsonAsync(
            ApiRoutes.Auth.PasskeyRegisterFinishFull,
            new PasskeyRegisterFinishRequest(Guid.NewGuid().ToString("N"), "{}"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Well_known_documents_are_served_from_the_site_root()
    {
        var client = _factory.CreateClient();

        var aasa = await client.GetAsync("/.well-known/apple-app-site-association");
        aasa.EnsureSuccessStatusCode();
        Assert.Contains("webcredentials", await aasa.Content.ReadAsStringAsync());

        var assetlinks = await client.GetAsync("/.well-known/assetlinks.json");
        assetlinks.EnsureSuccessStatusCode();
        Assert.Contains("get_login_creds", await assetlinks.Content.ReadAsStringAsync());
    }

    private async Task<HttpResponseMessage> AuthorizedGet(string token, string route)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, route);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return await _factory.CreateClient().SendAsync(request);
    }

    /// <summary>
    /// A minimal in-process WebAuthn authenticator: one ES256 (P-256) credential that emits the standard
    /// attestation ("none" format) and assertion JSON. Enough to satisfy Fido2NetLib's verification so
    /// the server relying party can be tested without a real platform authenticator.
    /// </summary>
    private sealed class SoftwareAuthenticator(string origin)
    {
        private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        private byte[] _credentialId = RandomNumberGenerator.GetBytes(32);
        private byte[] _userHandle = [];
        private string _rpId = "localhost";

        /// <summary>Gives the authenticator a user handle for negative tests that never register.</summary>
        public void SeedUnregisteredHandle() => _userHandle = Guid.NewGuid().ToByteArray();

        public string CreateAttestation(string creationOptionsJson)
        {
            var options = Fido2NetLib.CredentialCreateOptions.FromJson(creationOptionsJson);
            _rpId = options.Rp.Id;
            _userHandle = options.User.Id;

            var clientDataJson = ClientData("webauthn.create", options.Challenge);
            var authData = BuildAuthenticatorData(includeAttestedCredential: true, signCount: 0);

            // attestationObject = CBOR { fmt: "none", attStmt: {}, authData: <bytes> }.
            var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
            writer.WriteStartMap(3);
            writer.WriteTextString("fmt");
            writer.WriteTextString("none");
            writer.WriteTextString("attStmt");
            writer.WriteStartMap(0);
            writer.WriteEndMap();
            writer.WriteTextString("authData");
            writer.WriteByteString(authData);
            writer.WriteEndMap();
            var attestationObject = writer.Encode();

            return JsonSerializer.Serialize(new
            {
                id = B64(_credentialId),
                rawId = B64(_credentialId),
                type = "public-key",
                response = new
                {
                    clientDataJSON = B64(clientDataJson),
                    attestationObject = B64(attestationObject),
                },
            });
        }

        public string CreateAssertion(string requestOptionsJson)
        {
            var options = Fido2NetLib.AssertionOptions.FromJson(requestOptionsJson);
            _rpId = options.RpId ?? _rpId;

            var clientDataJson = ClientData("webauthn.get", options.Challenge);
            var authData = BuildAuthenticatorData(includeAttestedCredential: false, signCount: 1);

            var signedPayload = authData.Concat(SHA256.HashData(clientDataJson)).ToArray();
            var signature = _key.SignData(signedPayload, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

            return JsonSerializer.Serialize(new
            {
                id = B64(_credentialId),
                rawId = B64(_credentialId),
                type = "public-key",
                response = new
                {
                    clientDataJSON = B64(clientDataJson),
                    authenticatorData = B64(authData),
                    signature = B64(signature),
                    userHandle = B64(_userHandle),
                },
            });
        }

        private byte[] ClientData(string type, byte[] challenge) =>
            JsonSerializer.SerializeToUtf8Bytes(new
            {
                type,
                challenge = B64(challenge),
                origin,
                crossOrigin = false,
            });

        private byte[] BuildAuthenticatorData(bool includeAttestedCredential, uint signCount)
        {
            var data = new List<byte>();
            data.AddRange(SHA256.HashData(Encoding.UTF8.GetBytes(_rpId))); // rpIdHash (32).

            // Flags: UP (0x01) | UV (0x04) [| AT (0x40) when attested credential data is present].
            var flags = (byte)(0x01 | 0x04 | (includeAttestedCredential ? 0x40 : 0x00));
            data.Add(flags);

            Span<byte> counter = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(counter, signCount);
            data.AddRange(counter.ToArray());

            if (includeAttestedCredential)
            {
                data.AddRange(new byte[16]); // aaguid (all zero).
                Span<byte> credLen = stackalloc byte[2];
                BinaryPrimitives.WriteUInt16BigEndian(credLen, (ushort)_credentialId.Length);
                data.AddRange(credLen.ToArray());
                data.AddRange(_credentialId);
                data.AddRange(CosePublicKey());
            }

            return data.ToArray();
        }

        /// <summary>The ES256 public key as a COSE_Key CBOR map (kty EC2, alg ES256, crv P-256, x, y).</summary>
        private byte[] CosePublicKey()
        {
            var p = _key.ExportParameters(false);
            var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
            writer.WriteStartMap(5);
            writer.WriteInt32(1);   // kty
            writer.WriteInt32(2);   //   EC2
            writer.WriteInt32(3);   // alg
            writer.WriteInt32(-7);  //   ES256
            writer.WriteInt32(-1);  // crv
            writer.WriteInt32(1);   //   P-256
            writer.WriteInt32(-2);  // x
            writer.WriteByteString(p.Q.X!);
            writer.WriteInt32(-3);  // y
            writer.WriteByteString(p.Q.Y!);
            writer.WriteEndMap();
            return writer.Encode();
        }

        private static string B64(byte[] bytes) => Base64Url.EncodeToString(bytes);
    }
}
