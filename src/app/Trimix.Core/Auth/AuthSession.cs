using System.Text.Json;
using Trimix.Contracts.Dtos;
using Trimix.Core.Abstractions;

namespace Trimix.Core.Auth;

/// <summary>
/// Holds the signed-in state. The token + user snapshot live in <see cref="ISecureStore"/>
/// (<see cref="SecureStoreKeys.AuthToken"/> / <see cref="SecureStoreKeys.AuthUser"/>). Serverless:
/// nothing reads the token remotely — the session is purely local device state.
/// </summary>
public sealed class AuthSession(ISecureStore secureStore) : IAuthSession
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public bool IsAuthenticated { get; private set; }

    public UserDto? CurrentUser { get; private set; }

    public event EventHandler<bool>? AuthStateChanged;

    public event EventHandler? IdentityChanged;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var token = await secureStore.GetAsync(SecureStoreKeys.AuthToken, cancellationToken).ConfigureAwait(false);
        IsAuthenticated = !string.IsNullOrEmpty(token);
        if (!IsAuthenticated)
        {
            CurrentUser = null;
            return;
        }

        var userJson = await secureStore.GetAsync(SecureStoreKeys.AuthUser, cancellationToken).ConfigureAwait(false);
        if (userJson is not null)
        {
            try
            {
                CurrentUser = JsonSerializer.Deserialize<UserDto>(userJson, JsonOptions);
            }
            catch (JsonException)
            {
                CurrentUser = null;
            }
        }

        // Backfill the last-established-account marker for a session restored from an app version that
        // predates identity-change tracking, so a subsequent DIFFERENT account is still detected as a switch.
        if (CurrentUser is { } restored)
        {
            var lastAccountId = await secureStore
                .GetAsync(SecureStoreKeys.LastAccountId, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(lastAccountId))
            {
                await secureStore
                    .SetAsync(SecureStoreKeys.LastAccountId, restored.Id.ToString(), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    public async Task SignInAsync(AuthResponse auth, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auth);

        // Detect a genuine identity change BEFORE overwriting state. The device-global connector consent+tokens
        // belong to whichever identity established them; if a DIFFERENT real account last owned this device we
        // must purge them. A guest→account upgrade has no prior established account (null marker) and is
        // intentional single-user continuity — connectors are KEPT.
        var priorAccountId = await secureStore
            .GetAsync(SecureStoreKeys.LastAccountId, cancellationToken).ConfigureAwait(false);
        var incomingAccountId = auth.User.Id.ToString();
        var isIdentityChange = !string.IsNullOrEmpty(priorAccountId)
            && !string.Equals(priorAccountId, incomingAccountId, StringComparison.Ordinal);

        await secureStore.SetAsync(SecureStoreKeys.AuthToken, auth.Token, cancellationToken).ConfigureAwait(false);
        await secureStore
            .SetAsync(SecureStoreKeys.AuthUser, JsonSerializer.Serialize(auth.User, JsonOptions), cancellationToken)
            .ConfigureAwait(false);
        await secureStore
            .SetAsync(SecureStoreKeys.LastAccountId, incomingAccountId, cancellationToken)
            .ConfigureAwait(false);
        IsAuthenticated = true;
        CurrentUser = auth.User;

        // Fire identity-change FIRST so the connector purge runs before any post-sign-in sync uses the new
        // identity; guest→account and same-account re-sign-in never raise it (continuity preserved).
        if (isIdentityChange)
        {
            IdentityChanged?.Invoke(this, EventArgs.Empty);
        }

        AuthStateChanged?.Invoke(this, true);
    }

    public Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        secureStore.Remove(SecureStoreKeys.AuthToken);
        secureStore.Remove(SecureStoreKeys.AuthUser);
        IsAuthenticated = false;
        CurrentUser = null;
        AuthStateChanged?.Invoke(this, false);
        return Task.CompletedTask;
    }
}
