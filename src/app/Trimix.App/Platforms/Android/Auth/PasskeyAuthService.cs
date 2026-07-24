using AndroidX.Credentials;
using AndroidX.Credentials.Exceptions;
using Java.Util.Concurrent;
using Microsoft.Extensions.Logging;
using Trimix.Core.Abstractions;

namespace Trimix.App.Auth;

/// <summary>
/// Android platform <see cref="IPasskeyAuthService"/> over the Jetpack <b>Credential Manager</b>
/// (<c>androidx.credentials</c>, via the maintained <c>Xamarin.AndroidX.Credentials</c> +
/// <c>Xamarin.AndroidX.Credentials.PlayServicesAuth</c> net10.0-android bindings). The server (a
/// FIDO2 relying party) hands us the standard WebAuthn options JSON; Credential Manager presents the
/// system passkey sheet on the current <see cref="Platform.CurrentActivity"/>, gathers the
/// screen-lock/biometric gesture, and returns the WebAuthn attestation/assertion JSON verbatim — the
/// exact shape Fido2NetLib deserializes on the backend (base64url <c>id</c>/<c>rawId</c>,
/// <c>type:"public-key"</c>, <c>response:{ … }</c>), so no reshaping happens here.
///
/// Runtime prerequisite (HONEST, per <c>docs/known-gaps.md</c>): platform passkeys require the app to
/// be associated with a real HTTPS relying-party domain through <b>Digital Asset Links</b> — an
/// <c>assetlinks.json</c> hosted at <c>https://&lt;rp&gt;/.well-known/assetlinks.json</c> whose
/// <c>sha256_cert_fingerprints</c> matches the app-signing certificate (see the
/// <c>well-known-assetlinks.json.txt</c> template beside this file). Without that association the
/// Credential Manager refuses; the flow therefore cannot be exercised on an emulator against a
/// localhost dev backend, and real runtime validation is device/domain-dependent (M3-style).
/// </summary>
public sealed class PasskeyAuthService(ILogger<PasskeyAuthService> logger) : IPasskeyAuthService
{
    /// <inheritdoc />
    /// <remarks>Credential Manager passkeys require Android 9 (API 28) or newer.</remarks>
    public bool IsAvailable => OperatingSystem.IsAndroidVersionAtLeast(28);

    /// <inheritdoc />
    public Task<string> CreatePasskeyAsync(string creationOptionsJson, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(creationOptionsJson);

        // The server's PublicKeyCredentialCreationOptions JSON goes in unmodified; Credential Manager
        // parses it and returns the registration (attestation) response JSON on success.
        var request = new CreatePublicKeyCredentialRequest(creationOptionsJson);
        return InvokeAsync(
            (manager, activity, signal, executor, callback) =>
                manager.CreateCredentialAsync(activity, request, signal, executor, callback),
            result => (result as CreatePublicKeyCredentialResponse
                    ?? throw new PasskeyException(
                        $"Credential Manager returned an unexpected create response: '{result?.GetType().Name ?? "null"}'."))
                .RegistrationResponseJson
                    ?? throw new PasskeyException("Credential Manager returned an empty registration response."),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<string> GetPasskeyAsync(string requestOptionsJson, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestOptionsJson);

        // Discoverable (usernameless) assertion: the server's PublicKeyCredentialRequestOptions JSON
        // becomes a single public-key credential option; the resulting PublicKeyCredential carries the
        // authentication (assertion) response JSON.
        var option = new GetPublicKeyCredentialOption(requestOptionsJson);
        var request = new GetCredentialRequest.Builder()
            .AddCredentialOption(option)
            .Build();
        return InvokeAsync(
            (manager, activity, signal, executor, callback) =>
                manager.GetCredentialAsync(activity, request, signal, executor, callback),
            result =>
            {
                var response = result as GetCredentialResponse
                    ?? throw new PasskeyException(
                        $"Credential Manager returned an unexpected get response: '{result?.GetType().Name ?? "null"}'.");
                var credential = response.Credential as PublicKeyCredential
                    ?? throw new PasskeyException(
                        $"Selected credential was not a passkey (got '{response.Credential?.GetType().Name ?? "null"}').");
                return credential.AuthenticationResponseJson
                    ?? throw new PasskeyException("Credential Manager returned an empty authentication response.");
            },
            cancellationToken);
    }

    /// <summary>
    /// Shared adapter that marshals a Credential Manager callback-style call into a <see cref="Task{String}"/>:
    /// resolves the current activity, wires an <see cref="Android.OS.CancellationSignal"/> to
    /// <paramref name="cancellationToken"/>, launches <paramref name="launch"/>, and projects the Java
    /// result via <paramref name="project"/> (or maps the error) onto a <see cref="TaskCompletionSource{String}"/>.
    /// </summary>
    private async Task<string> InvokeAsync(
        Action<ICredentialManager, Android.App.Activity, Android.OS.CancellationSignal, IExecutor, ICredentialManagerCallback> launch,
        Func<Java.Lang.Object?, string> project,
        CancellationToken cancellationToken)
    {
        if (!IsAvailable)
        {
            throw new PasskeyException("Platform passkeys require Android 9 (API 28) or newer.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var activity = Platform.CurrentActivity
            ?? throw new PasskeyException("No foreground activity is available to present the passkey sheet.");
        var manager = CredentialManager.Create(activity);

        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var signal = new Android.OS.CancellationSignal();
        var callback = new CredentialManagerCallback(
            onResult: result =>
            {
                try
                {
                    completion.TrySetResult(project(result));
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            },
            onError: error =>
            {
                var mapped = MapError(error);
                logger.LogWarning("Credential Manager passkey operation failed: {Message}", mapped.Message);
                completion.TrySetException(mapped);
            });

        launch(manager, activity, signal, DirectExecutor.Instance, callback);

        using var registration = cancellationToken.Register(() =>
        {
            signal.Cancel();
            completion.TrySetCanceled(cancellationToken);
        });
        return await completion.Task.ConfigureAwait(false);
    }

    /// <summary>
    /// Maps a Credential Manager error object onto the abstraction's exception contract: user
    /// dismissal (<c>CreateCredentialCancellationException</c> / <c>GetCredentialCancellationException</c>)
    /// becomes <see cref="PasskeyCancelledException"/>; everything else becomes <see cref="PasskeyException"/>.
    /// </summary>
    private static PasskeyException MapError(Java.Lang.Object? error)
    {
        // The androidx.credentials exceptions bind as Java.Lang.Object proxies (Java.Lang.Throwable derives
        // from System.Exception, not Java.Lang.Object, so it can't be cast from the erased Object). Detect
        // user-cancel from the proxy's type name AND its Java toString (which carries the class + message).
        var typeName = error?.GetType().Name ?? "unknown";
        var message = error?.ToString() ?? "unknown error";

        if (typeName.Contains("Cancel", StringComparison.OrdinalIgnoreCase)
            || message.Contains("cancel", StringComparison.OrdinalIgnoreCase))
        {
            return new PasskeyCancelledException();
        }

        return new PasskeyException($"Passkey operation failed [{typeName}]: {message}");
    }

    /// <summary>
    /// Inline <see cref="IExecutor"/> that runs the Credential Manager result/error callback on the
    /// thread that delivers it — the callback only completes a <see cref="TaskCompletionSource{TResult}"/>,
    /// so no dedicated dispatcher is needed.
    /// </summary>
    private sealed class DirectExecutor : Java.Lang.Object, IExecutor
    {
        public static readonly DirectExecutor Instance = new();

        public void Execute(Java.Lang.IRunnable? command) => command?.Run();
    }

    /// <summary>
    /// Java callback adapter for <c>CredentialManagerCallback&lt;R, E&gt;</c> (generics erased to
    /// <see cref="Java.Lang.Object"/> across the JNI boundary), forwarding to managed delegates.
    /// </summary>
    private sealed class CredentialManagerCallback(Action<Java.Lang.Object?> onResult, Action<Java.Lang.Object?> onError)
        : Java.Lang.Object, ICredentialManagerCallback
    {
        public void OnResult(Java.Lang.Object? result) => onResult(result);

        public void OnError(Java.Lang.Object? error) => onError(error);
    }
}
