using Trimix.Core.Abstractions;

namespace Trimix.Core.Analytics;

/// <summary>
/// <see cref="IUserPseudoIdProvider"/> backed by <see cref="IPreferences"/> (wiped on uninstall — the
/// correct per-install scope). Lazily generates and persists the id on first read, then caches it.
/// </summary>
public sealed class UserPseudoIdProvider(IPreferences preferences) : IUserPseudoIdProvider
{
    private readonly Lock _gate = new();
    private string? _userPseudoId;

    public string UserPseudoId
    {
        get
        {
            lock (_gate)
            {
                if (_userPseudoId is not null)
                {
                    return _userPseudoId;
                }

                _userPseudoId = preferences.Get(PreferenceKeys.UserPseudoId);
                if (_userPseudoId is null)
                {
                    _userPseudoId = Guid.NewGuid().ToString("N");
                    preferences.Set(PreferenceKeys.UserPseudoId, _userPseudoId);
                }

                return _userPseudoId;
            }
        }
    }
}
