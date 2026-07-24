using System.Text.Json;
using System.Text.Json.Nodes;
using AuthenticationServices;
using CoreFoundation;
using Foundation;
using Microsoft.Extensions.Logging;
using Trimix.Core.Abstractions;
using UIKit;

namespace Trimix.App.Auth;

/// <summary>
/// iOS platform-passkey <see cref="IPasskeyAuthService"/> over the AuthenticationServices framework
/// (iOS 16+ platform/synced passkeys), the twin of the Android Credential Manager implementation. The
/// FIDO2 relying-party server (Fido2NetLib) issues WebAuthn <c>PublicKeyCredentialCreationOptions</c> /
/// <c>PublicKeyCredentialRequestOptions</c> as JSON; this service hands the parsed challenge/user to an
/// <see cref="ASAuthorizationPlatformPublicKeyCredentialProvider"/>, drives an
/// <see cref="ASAuthorizationController"/> to gather the Face ID / Touch ID gesture, and re-serializes
/// the platform result into the exact JSON shape the backend deserializes with Fido2NetLib's
/// <c>AuthenticatorAttestationRawResponse</c> (register) / <c>AuthenticatorAssertionRawResponse</c>
/// (login): base64url <c>id</c>/<c>rawId</c>, <c>type: "public-key"</c>, and a <c>response</c> object of
/// base64url members.
///
/// Runtime prerequisite (HONEST, per docs/known-gaps.md): platform passkeys require the app to be
/// associated with a real HTTPS relying-party domain — the <c>com.apple.developer.associated-domains</c>
/// entitlement (<c>webcredentials:&lt;rpId&gt;</c>, wired in Platforms/iOS/Entitlements.plist) PLUS a
/// server-hosted <c>/.well-known/apple-app-site-association</c> that lists this app's Team+bundle id.
/// Without that association iOS refuses <c>PerformRequests</c> (the sheet never appears / errors). This
/// cannot be exercised on the iOS Simulator against a localhost dev backend; real device/domain
/// validation is M3 work. The code path is complete and compiles.
/// </summary>
public sealed class PasskeyAuthService(ILogger<PasskeyAuthService> logger) : IPasskeyAuthService
{
    /// <summary>ASAuthorization NSError domain — used to disambiguate a genuine user-cancel.</summary>
    private const string AuthorizationErrorDomain = "com.apple.AuthenticationServices.AuthorizationError";

    /// <inheritdoc />
    public bool IsAvailable => OperatingSystem.IsIOSVersionAtLeast(16);

    /// <inheritdoc />
    public async Task<string> CreatePasskeyAsync(
        string creationOptionsJson, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(creationOptionsJson);
        if (!IsAvailable)
        {
            throw new PasskeyUnavailableException();
        }

        string relyingPartyId;
        NSData challenge;
        NSData userId;
        string name;
        try
        {
            using var document = JsonDocument.Parse(creationOptionsJson);
            var root = document.RootElement;

            relyingPartyId = ReadRelyingPartyId(root)
                ?? throw new PasskeyException("The WebAuthn creation options are missing rp.id.");
            challenge = NSData.FromArray(DecodeBase64Url(ReadRequiredString(root, "challenge")));

            if (!root.TryGetProperty("user", out var user) || user.ValueKind != JsonValueKind.Object)
            {
                throw new PasskeyException("The WebAuthn creation options are missing the user object.");
            }

            userId = NSData.FromArray(DecodeBase64Url(ReadRequiredString(user, "id")));
            name = ReadOptionalString(user, "name")
                ?? ReadOptionalString(user, "displayName")
                ?? relyingPartyId;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or FormatException)
        {
            throw new PasskeyException("The WebAuthn creation options JSON was malformed.", ex);
        }

        logger.LogDebug("Creating a platform passkey for relying party {RelyingParty}.", relyingPartyId);

        var provider = new ASAuthorizationPlatformPublicKeyCredentialProvider(relyingPartyId);
        var request = provider.CreateCredentialRegistrationRequest(challenge, name, userId);

        var authorization = await PerformRequestAsync(request, cancellationToken).ConfigureAwait(false);
        var registration = authorization.GetCredential<ASAuthorizationPlatformPublicKeyCredentialRegistration>();
        if (registration is null)
        {
            throw new PasskeyException("The passkey registration returned an unexpected credential type.");
        }

        var credentialId = EncodeBase64Url(ToBytes(registration.CredentialId));
        var response = new JsonObject
        {
            ["clientDataJSON"] = EncodeBase64Url(ToBytes(registration.RawClientDataJson)),
            ["attestationObject"] = EncodeBase64Url(ToBytes(registration.RawAttestationObject)),
        };

        return new JsonObject
        {
            ["id"] = credentialId,
            ["rawId"] = credentialId,
            ["type"] = "public-key",
            ["response"] = response,
        }.ToJsonString();
    }

    /// <inheritdoc />
    public async Task<string> GetPasskeyAsync(
        string requestOptionsJson, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestOptionsJson);
        if (!IsAvailable)
        {
            throw new PasskeyUnavailableException();
        }

        string relyingPartyId;
        NSData challenge;
        try
        {
            using var document = JsonDocument.Parse(requestOptionsJson);
            var root = document.RootElement;

            relyingPartyId = ReadRelyingPartyId(root)
                ?? throw new PasskeyException("The WebAuthn request options are missing rpId.");
            challenge = NSData.FromArray(DecodeBase64Url(ReadRequiredString(root, "challenge")));
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or FormatException)
        {
            throw new PasskeyException("The WebAuthn request options JSON was malformed.", ex);
        }

        logger.LogDebug("Asserting a platform passkey for relying party {RelyingParty}.", relyingPartyId);

        var provider = new ASAuthorizationPlatformPublicKeyCredentialProvider(relyingPartyId);
        var request = provider.CreateCredentialAssertionRequest(challenge);

        var authorization = await PerformRequestAsync(request, cancellationToken).ConfigureAwait(false);
        var assertion = authorization.GetCredential<ASAuthorizationPlatformPublicKeyCredentialAssertion>();
        if (assertion is null)
        {
            throw new PasskeyException("The passkey assertion returned an unexpected credential type.");
        }

        var credentialId = EncodeBase64Url(ToBytes(assertion.CredentialId));
        var response = new JsonObject
        {
            ["clientDataJSON"] = EncodeBase64Url(ToBytes(assertion.RawClientDataJson)),
            ["authenticatorData"] = EncodeBase64Url(ToBytes(assertion.RawAuthenticatorData)),
            ["signature"] = EncodeBase64Url(ToBytes(assertion.Signature)),
            ["userHandle"] = EncodeBase64Url(ToBytes(assertion.UserId)),
        };

        return new JsonObject
        {
            ["id"] = credentialId,
            ["rawId"] = credentialId,
            ["type"] = "public-key",
            ["response"] = response,
        }.ToJsonString();
    }

    /// <summary>
    /// Builds and runs an <see cref="ASAuthorizationController"/> on the main thread, awaiting the
    /// delegate callback via a <see cref="TaskCompletionSource{TResult}"/>. The coordinator (delegate +
    /// presentation-context provider) and controller are kept alive across the await so the native side
    /// can call back.
    /// </summary>
    private static async Task<ASAuthorization> PerformRequestAsync(
        ASAuthorizationRequest request, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<ASAuthorization>(TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = new AuthorizationCoordinator(completion);

        using var registration = cancellationToken.Register(
            static state => ((TaskCompletionSource<ASAuthorization>)state!).TrySetCanceled(), completion);

        DispatchQueue.MainQueue.DispatchAsync(() =>
        {
            try
            {
                var controller = new ASAuthorizationController([request])
                {
                    Delegate = coordinator,
                    PresentationContextProvider = coordinator,
                };
                coordinator.Controller = controller;
                controller.PerformRequests();
            }
            catch (Exception ex)
            {
                completion.TrySetException(new PasskeyException("Could not present the passkey sheet.", ex));
            }
        });

        try
        {
            return await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            // Keep the delegate (and, via it, the controller) rooted until the native call-back landed.
            GC.KeepAlive(coordinator);
        }
    }

    /// <summary>Reads rp.id (creation options) or the flat rpId (request options), whichever is present.</summary>
    private static string? ReadRelyingPartyId(JsonElement root)
    {
        if (root.TryGetProperty("rp", out var rp)
            && rp.ValueKind == JsonValueKind.Object
            && rp.TryGetProperty("id", out var id)
            && id.ValueKind == JsonValueKind.String)
        {
            return id.GetString();
        }

        return ReadOptionalString(root, "rpId");
    }

    private static string ReadRequiredString(JsonElement element, string property) =>
        ReadOptionalString(element, property)
        ?? throw new PasskeyException($"The WebAuthn options are missing the '{property}' field.");

    private static string? ReadOptionalString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static byte[] ToBytes(NSData? data) => data?.ToArray() ?? [];

    /// <summary>Decodes a base64url string (no padding, <c>-_</c> alphabet) to raw bytes.</summary>
    internal static byte[] DecodeBase64Url(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized += (normalized.Length % 4) switch
        {
            2 => "==",
            3 => "=",
            _ => string.Empty,
        };

        return Convert.FromBase64String(normalized);
    }

    /// <summary>Encodes raw bytes to a base64url string (no padding, <c>-_</c> alphabet).</summary>
    internal static string EncodeBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>
    /// <see cref="NSObject"/> that fronts the AuthenticationServices delegate + presentation-context
    /// protocols, completing a <see cref="TaskCompletionSource{TResult}"/> from the native call-backs.
    /// </summary>
    private sealed class AuthorizationCoordinator(TaskCompletionSource<ASAuthorization> completion)
        : NSObject, IASAuthorizationControllerDelegate, IASAuthorizationControllerPresentationContextProviding
    {
        /// <summary>Strong reference so the controller is not collected before it calls back.</summary>
        public ASAuthorizationController? Controller { get; set; }

        [Export("authorizationController:didCompleteWithAuthorization:")]
        public void DidComplete(ASAuthorizationController controller, ASAuthorization authorization) =>
            completion.TrySetResult(authorization);

        [Export("authorizationController:didCompleteWithError:")]
        public void DidComplete(ASAuthorizationController controller, NSError error) =>
            completion.TrySetException(ToPasskeyException(error));

        [Export("presentationAnchorForAuthorizationController:")]
        public UIWindow GetPresentationAnchor(ASAuthorizationController controller) => KeyWindow();

        /// <summary>Maps the ASAuthorization error to a cancel vs generic failure.</summary>
        private static PasskeyException ToPasskeyException(NSError error)
        {
            if (error.Domain == AuthorizationErrorDomain
                && (ASAuthorizationError)(long)error.Code == ASAuthorizationError.Canceled)
            {
                return new PasskeyCancelledException();
            }

            return new PasskeyException(
                $"The passkey operation failed ({error.LocalizedDescription ?? error.Domain}).");
        }

        /// <summary>The current key window, the anchor iOS presents the passkey sheet over.</summary>
        private static UIWindow KeyWindow()
        {
            var window = UIApplication.SharedApplication.ConnectedScenes
                .OfType<UIWindowScene>()
                .SelectMany(scene => scene.Windows)
                .FirstOrDefault(candidate => candidate.IsKeyWindow);

            return window
                ?? UIApplication.SharedApplication.ConnectedScenes
                    .OfType<UIWindowScene>()
                    .SelectMany(scene => scene.Windows)
                    .FirstOrDefault()
                ?? throw new PasskeyException("There is no active window to present the passkey sheet.");
        }
    }
}
