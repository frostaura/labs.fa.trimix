using System.Text.Json;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Trimix.Api.Analytics;
using Trimix.Api.Auth;
using Trimix.Api.Data;
using Trimix.Contracts;
using Trimix.Contracts.Dtos;

namespace Trimix.Api.Endpoints;

/// <summary>
/// POST /auth/passkey/{register,login}/{begin,finish} — the server-side WebAuthn/FIDO2 relying party
/// (Fido2NetLib). Passwordless, discoverable (resident) credentials, so login is usernameless: the
/// authenticator returns the credential id and we resolve the account from it. Each flow is a two-step
/// begin→finish round-trip; the begin step's WebAuthn options are held in a short-lived
/// <see cref="IMemoryCache"/> entry keyed by a fresh <c>ChallengeId</c> and consumed by finish.
/// <para>
/// The registration finish path emits the frozen <c>account_created</c> funnel event (auth provider
/// <c>passkey</c>) exactly like the email register, so passkey sign-ups reconcile into the same funnel.
/// </para>
/// <para>
/// HONESTY (device/domain-dependent): the platform authenticators that produce these attestation /
/// assertion responses only operate against an app associated with a real HTTPS relying-party domain
/// (iOS associated-domains + apple-app-site-association; Android Digital Asset Links assetlinks.json
/// tied to the app-signing SHA-256 — both served from <see cref="MapWellKnownEndpoints"/>). This
/// server code is complete and unit-tested against a software authenticator, but real end-to-end
/// validation cannot run on a simulator/emulator against a localhost backend (M3-style).
/// </para>
/// </summary>
public static class PasskeyEndpoints
{
    /// <summary><c>auth_provider</c> value recorded on <c>account_created</c> for passkey sign-ups.</summary>
    public const string PasskeyAuthProvider = "passkey";

    /// <summary>How long a begin→finish challenge stays valid in the server cache.</summary>
    private static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(5);

    private const string RegistrationCachePrefix = "passkey:reg:";
    private const string LoginCachePrefix = "passkey:login:";

    public static RouteGroupBuilder MapPasskeyEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup(ApiRoutes.Auth.Group);

        group.MapPost(ApiRoutes.Auth.PasskeyRegisterBegin, (
            PasskeyRegisterBeginRequest? request,
            IFido2 fido2,
            IMemoryCache cache) =>
        {
            var pendingUserId = Guid.NewGuid();
            var displayName = string.IsNullOrWhiteSpace(request?.DisplayName) ? "Trimix diver" : request!.DisplayName!.Trim();

            // The user handle is the pending user id — usernameless login reads it back from the
            // assertion's userHandle to resolve the owning account.
            var fidoUser = new Fido2User
            {
                Id = pendingUserId.ToByteArray(),
                Name = $"diver-{pendingUserId:N}",
                DisplayName = displayName,
            };

            var options = fido2.RequestNewCredential(new RequestNewCredentialParams
            {
                User = fidoUser,
                ExcludeCredentials = [],
                AuthenticatorSelection = new AuthenticatorSelection
                {
                    // Discoverable credential so the account can be found without a username.
                    ResidentKey = ResidentKeyRequirement.Required,
                    UserVerification = UserVerificationRequirement.Preferred,
                },
                AttestationPreference = AttestationConveyancePreference.None,
            });

            var optionsJson = options.ToJson();
            var challengeId = NewChallengeId();
            cache.Set(
                RegistrationCachePrefix + challengeId,
                new PasskeyRegistrationChallenge(pendingUserId, displayName, optionsJson),
                new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = ChallengeLifetime });

            return Results.Ok(new PasskeyRegisterBeginResponse(challengeId, optionsJson));
        });

        group.MapPost(ApiRoutes.Auth.PasskeyRegisterFinish, async (
            PasskeyRegisterFinishRequest request,
            IFido2 fido2,
            IMemoryCache cache,
            AppDbContext db,
            TokenService tokenService,
            ServerAnalytics serverAnalytics,
            ILoggerFactory loggerFactory,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger(typeof(PasskeyEndpoints).FullName!);

            if (request is null
                || string.IsNullOrWhiteSpace(request.ChallengeId)
                || !cache.TryGetValue(RegistrationCachePrefix + request.ChallengeId, out PasskeyRegistrationChallenge? challenge)
                || challenge is null)
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, detail: "Unknown or expired passkey challenge.");
            }

            cache.Remove(RegistrationCachePrefix + request.ChallengeId);

            AuthenticatorAttestationRawResponse? attestation;
            try
            {
                attestation = JsonSerializer.Deserialize<AuthenticatorAttestationRawResponse>(request.AttestationResponseJson);
            }
            catch (JsonException)
            {
                attestation = null;
            }

            if (attestation is null)
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, detail: "Malformed attestation response.");
            }

            var options = CredentialCreateOptions.FromJson(challenge.OptionsJson);

            RegisteredPublicKeyCredential credential;
            try
            {
                credential = await fido2.MakeNewCredentialAsync(
                    new MakeNewCredentialParams
                    {
                        AttestationResponse = attestation,
                        OriginalOptions = options,
                        IsCredentialIdUniqueToUserCallback = async (args, ct) =>
                            !await db.PasskeyCredentials.AnyAsync(c => c.CredentialId == args.CredentialId, ct).ConfigureAwait(false),
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Fido2VerificationException ex)
            {
                logger.LogWarning(ex, "Passkey registration verification failed.");
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, detail: "Passkey registration could not be verified.");
            }

            var userId = challenge.PendingUserId;
            var user = new User
            {
                Id = userId,
                // Passkey-only accounts have no real email; a unique generated handle keeps the unique
                // index satisfied and labels the account in the passkey manager via DisplayName.
                Email = $"passkey+{userId:N}@passkey.trimix.local",
                PasswordHash = null,
                Role = UserRoles.User,
                CreatedAtUtc = DateTime.UtcNow,
                Verified = false,
            };
            db.Users.Add(user);
            db.PasskeyCredentials.Add(new PasskeyCredential
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                CredentialId = credential.Id,
                PublicKey = credential.PublicKey,
                SignatureCounter = credential.SignCount,
                AaGuid = credential.AaGuid,
                CreatedAtUtc = DateTime.UtcNow,
            });

            // Frozen funnel event — mirrors the email register path, attributed to the caller's pseudo-id.
            serverAnalytics.Emit(
                FunnelEvents.AccountCreated,
                new Dictionary<string, string> { [FunnelEvents.Props.AuthProvider] = PasskeyAuthProvider },
                ServerAnalytics.ResolvePseudoId(httpContext, userId),
                userId);

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return Results.Ok(new AuthResponse(tokenService.IssueToken(user), ToDto(user)));
        });

        group.MapPost(ApiRoutes.Auth.PasskeyLoginBegin, (
            IFido2 fido2,
            IMemoryCache cache) =>
        {
            // Usernameless: no allowCredentials — the discoverable credential identifies the account.
            var options = fido2.GetAssertionOptions(new GetAssertionOptionsParams
            {
                AllowedCredentials = [],
                UserVerification = UserVerificationRequirement.Preferred,
            });

            var optionsJson = options.ToJson();
            var challengeId = NewChallengeId();
            cache.Set(
                LoginCachePrefix + challengeId,
                new PasskeyLoginChallenge(optionsJson),
                new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = ChallengeLifetime });

            return Results.Ok(new PasskeyLoginBeginResponse(challengeId, optionsJson));
        });

        group.MapPost(ApiRoutes.Auth.PasskeyLoginFinish, async (
            PasskeyLoginFinishRequest request,
            IFido2 fido2,
            IMemoryCache cache,
            AppDbContext db,
            TokenService tokenService,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger(typeof(PasskeyEndpoints).FullName!);

            if (request is null
                || string.IsNullOrWhiteSpace(request.ChallengeId)
                || !cache.TryGetValue(LoginCachePrefix + request.ChallengeId, out PasskeyLoginChallenge? challenge)
                || challenge is null)
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, detail: "Unknown or expired passkey challenge.");
            }

            cache.Remove(LoginCachePrefix + request.ChallengeId);

            AuthenticatorAssertionRawResponse? assertion;
            try
            {
                assertion = JsonSerializer.Deserialize<AuthenticatorAssertionRawResponse>(request.AssertionResponseJson);
            }
            catch (JsonException)
            {
                assertion = null;
            }

            if (assertion is null)
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, detail: "Malformed assertion response.");
            }

            var credentialId = assertion.RawId;
            var stored = await db.PasskeyCredentials
                .SingleOrDefaultAsync(c => c.CredentialId == credentialId, cancellationToken)
                .ConfigureAwait(false);
            if (stored is null)
            {
                return Results.Unauthorized();
            }

            var options = AssertionOptions.FromJson(challenge.OptionsJson);

            VerifyAssertionResult result;
            try
            {
                result = await fido2.MakeAssertionAsync(
                    new MakeAssertionParams
                    {
                        AssertionResponse = assertion,
                        OriginalOptions = options,
                        StoredPublicKey = stored.PublicKey,
                        StoredSignatureCounter = stored.SignatureCounter,
                        IsUserHandleOwnerOfCredentialIdCallback = (args, ct) =>
                        {
                            // The asserted user handle (our user id) must own the credential id.
                            if (args.UserHandle.Length != 16)
                            {
                                return Task.FromResult(false);
                            }

                            var handleUserId = new Guid(args.UserHandle);
                            return db.PasskeyCredentials.AnyAsync(
                                c => c.CredentialId == args.CredentialId && c.UserId == handleUserId, ct);
                        },
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Fido2VerificationException ex)
            {
                logger.LogWarning(ex, "Passkey assertion verification failed.");
                return Results.Unauthorized();
            }

            // Advance the clone-detection counter, then issue the session token.
            stored.SignatureCounter = result.SignCount;
            var user = await db.Users.SingleOrDefaultAsync(u => u.Id == stored.UserId, cancellationToken).ConfigureAwait(false);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return Results.Ok(new AuthResponse(tokenService.IssueToken(user), ToDto(user)));
        });

        return api;
    }

    /// <summary>
    /// Serves the app-association documents from the site root (NOT under <c>/api/v1</c>): iOS reads
    /// <c>/.well-known/apple-app-site-association</c> and Android reads <c>/.well-known/assetlinks.json</c>
    /// to authorize passkey credentials for the associated app. Both are built from <c>Passkey:*</c> config.
    /// </summary>
    public static IEndpointRouteBuilder MapWellKnownEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/.well-known/apple-app-site-association", (PasskeyOptions options) =>
            Results.Json(
                new
                {
                    // Passkeys use the webcredentials service; applinks kept empty but well-formed.
                    applinks = new { apps = Array.Empty<string>(), details = Array.Empty<object>() },
                    webcredentials = new { apps = options.AppleAppIds },
                },
                contentType: "application/json"));

        app.MapGet("/.well-known/assetlinks.json", (PasskeyOptions options) =>
            Results.Json(
                new[]
                {
                    new
                    {
                        relation = new[] { "delegate_permission/common.get_login_creds" },
                        target = new
                        {
                            @namespace = "android_app",
                            package_name = options.AndroidPackageName,
                            sha256_cert_fingerprints = options.AndroidSha256CertFingerprints,
                        },
                    },
                },
                contentType: "application/json"));

        return app;
    }

    private static string NewChallengeId() => Guid.NewGuid().ToString("N");

    private static UserDto ToDto(User user) => new(user.Id, user.Email, user.Role, user.Verified, user.CreatedAtUtc);

    /// <summary>Cached registration challenge state (the pending account + the WebAuthn options JSON).</summary>
    private sealed record PasskeyRegistrationChallenge(Guid PendingUserId, string DisplayName, string OptionsJson);

    /// <summary>Cached login challenge state (the WebAuthn assertion options JSON).</summary>
    private sealed record PasskeyLoginChallenge(string OptionsJson);
}
