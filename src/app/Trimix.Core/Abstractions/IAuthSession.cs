using Trimix.Contracts.Dtos;

namespace Trimix.Core.Abstractions;

/// <summary>
/// The signed-in state of the app. Guest mode = not authenticated (local-only features).
/// The JWT lives in <see cref="ISecureStore"/>; <see cref="IApiClient"/> reads it from there.
/// </summary>
public interface IAuthSession
{
    bool IsAuthenticated { get; }

    UserDto? CurrentUser { get; }

    /// <summary>Raised with the new <see cref="IsAuthenticated"/> after sign-in/sign-out.</summary>
    event EventHandler<bool>? AuthStateChanged;

    /// <summary>
    /// Raised from <see cref="SignInAsync"/> ONLY when a genuinely DIFFERENT account establishes identity on
    /// this device — a real account user-id that differs from the last-established one (account→different-account).
    /// It is deliberately NOT raised on the guest→account upgrade (no prior established account), which is
    /// intentional single-user continuity. Consumers (connector purge) use it to wipe the previous identity's
    /// device-global connector consent+tokens on a shared-device handoff.
    /// </summary>
    event EventHandler? IdentityChanged;

    /// <summary>Loads persisted session state (token + user) from secure storage.</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>Persists the token + user from a successful register/login.</summary>
    Task SignInAsync(AuthResponse auth, CancellationToken cancellationToken = default);

    Task SignOutAsync(CancellationToken cancellationToken = default);
}
