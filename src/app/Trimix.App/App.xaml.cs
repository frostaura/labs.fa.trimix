using Microsoft.Extensions.Logging;
using Trimix.Core;
using Trimix.Core.Abstractions;

namespace Trimix.App;

public partial class App : Application
{
    private readonly AppStartupService _startup;
    private readonly Trimix.Core.Abstractions.IPreferences _preferences;
    private readonly ILogger<App> _logger;
    private bool _startupRan;

    public App(AppStartupService startup, Trimix.Core.Abstractions.IPreferences preferences, ILogger<App> logger)
    {
        _startup = startup;
        _preferences = preferences;
        _logger = logger;
        InitializeComponent();

        // The storyboard is dark-only; force Dark so the mockup palette renders regardless of the
        // device theme setting. (Light* tokens stay valid should this ever be relaxed.)
        UserAppTheme = AppTheme.Dark;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var shell = new AppShell();
        shell.Loaded += OnShellLoaded;
        return new Window(shell);
    }

    /// <summary>
    /// First-run wiring (PRD Flow A / Core AppStartupService): initialize stores + session +
    /// entitlement cache, emit <c>install</c> exactly once per install, refresh the
    /// server-authoritative tier, sync, flush — then present onboarding IF this is the first run
    /// (<c>first_run_completed</c> has not fired yet). Onboarding itself fires
    /// <c>first_run_completed</c>; auto-restore runs after sign-in (Core EntitlementService).
    /// </summary>
    private async void OnShellLoaded(object? sender, EventArgs e)
    {
        if (_startupRan)
        {
            return;
        }

        _startupRan = true;

        bool firstRun;
        try
        {
            firstRun = await _startup.RunAsync();
        }
        catch (Exception ex)
        {
            // Startup must never block the app (offline, API down). Fall back to the same
            // first-run signal AppStartupService reads.
            _logger.LogWarning(ex, "Startup pipeline failed; continuing offline.");
            firstRun = _preferences.Get(PreferenceKeys.FirstRunTracked) is null;
        }

        if (firstRun)
        {
            try
            {
                await Shell.Current.GoToAsync(Routes.Onboarding);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not present onboarding.");
            }
        }
    }
}
