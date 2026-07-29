namespace Trimix.Core.Abstractions;

/// <summary>
/// Runtime-flippable dev/demo mode (DEBUG-only surface; the demo connectors and the Settings toggle
/// that flip this only exist in Debug builds). A mutable singleton backed by
/// <see cref="IPreferences"/> (<see cref="PreferenceKeys.DevDemoMode"/>) so the toggle survives
/// restarts and applies live — the two connector-enumeration points
/// (<c>ConnectedAccountsViewModel</c> and <c>SyncCoordinator</c>) route their connector sets through
/// <see cref="SelectActive{T}"/> on every use, no restart needed.
///
/// <para>Demo connectors are marked structurally by the <see cref="ConnectorIdPrefix"/> on their
/// <c>ConnectorId</c> ("demo_garmin", …), so filtering needs no registry: demo mode ON shows only
/// demo connectors, OFF shows only real ones. When no demo connectors are registered (Release), the
/// filter is a no-op and real behavior is identical whatever the persisted flag says.</para>
/// </summary>
public sealed class DemoModeState(IPreferences preferences)
{
    /// <summary>Structural marker prefix on every demo connector's <c>ConnectorId</c>.</summary>
    public const string ConnectorIdPrefix = "demo_";

    private bool? _cached;

    /// <summary>Raised (new value as args) whenever <see cref="Enabled"/> flips.</summary>
    public event EventHandler<bool>? Changed;

    /// <summary>Whether demo mode is on. Persisted; flipping raises <see cref="Changed"/>.</summary>
    public bool Enabled
    {
        get => _cached ??= preferences.Get(PreferenceKeys.DevDemoMode) == "1";
        set
        {
            if (Enabled == value)
            {
                return;
            }

            _cached = value;
            preferences.Set(PreferenceKeys.DevDemoMode, value ? "1" : "0");
            Changed?.Invoke(this, value);
        }
    }

    /// <summary>True when <paramref name="connectorId"/> belongs to a demo connector.</summary>
    public static bool IsDemoId(string connectorId) =>
        connectorId.StartsWith(ConnectorIdPrefix, StringComparison.Ordinal);

    /// <summary>
    /// Filters a registered connector set down to the ACTIVE family: demo connectors when demo mode
    /// is on (and any are registered), real connectors otherwise. Null-instance safe for callers
    /// constructed without a <see cref="DemoModeState"/> (tests): pass-through.
    /// </summary>
    public static IReadOnlyList<T> SelectActive<T>(
        DemoModeState? state, IEnumerable<T> connectors, Func<T, string> connectorIdOf)
    {
        var all = connectors as IReadOnlyList<T> ?? [.. connectors];
        if (state is null)
        {
            return all;
        }

        var useDemo = state.Enabled && all.Any(c => IsDemoId(connectorIdOf(c)));
        return [.. all.Where(c => IsDemoId(connectorIdOf(c)) == useDemo)];
    }
}
