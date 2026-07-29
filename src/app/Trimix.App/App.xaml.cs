using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trimix.Core;
using Trimix.Core.Abstractions;

namespace Trimix.App;

public partial class App : Application
{
    private readonly AppStartupService _startup;
    private readonly Trimix.Core.Abstractions.IPreferences _preferences;
    private readonly IServiceProvider _services;
    private readonly ILogger<App> _logger;
    private bool _startupRan;

    public App(
        AppStartupService startup,
        Trimix.Core.Abstractions.IPreferences preferences,
        IServiceProvider services,
        ILogger<App> logger)
    {
        _startup = startup;
        _preferences = preferences;
        _services = services;
        _logger = logger;
        InitializeComponent();

        // The storyboard is dark-only; force Dark so the mockup palette renders regardless of the
        // device theme setting. (Light* tokens stay valid should this ever be relaxed.)
        UserAppTheme = AppTheme.Dark;
    }

    /// <summary>
    /// On first run, ONBOARDING is the window root — not the Shell. If we rooted on the Shell (whose
    /// first tab is the Logbook), the Logbook paints for a frame before any navigation could cover it:
    /// that is the "flash of the empty logbook". With onboarding as the root the Shell (and Logbook) are
    /// never created until onboarding completes — <see cref="Services.ShellNavigator"/> swaps the root to
    /// the Shell then. Subsequent launches boot straight into the Shell. Startup init (store/entitlement
    /// cache, install tracking, analytics flush) runs on the root's Loaded, behind whatever is showing.
    /// </summary>
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var firstRun = _preferences.Get(PreferenceKeys.FirstRunTracked) is null;
        Page root = firstRun
            ? _services.GetRequiredService<Pages.OnboardingPage>()
            : new AppShell();

        root.Loaded += OnRootLoaded;
        return new Window(root);
    }

    private async void OnRootLoaded(object? sender, EventArgs e)
    {
        if (sender is VisualElement element)
        {
            element.Loaded -= OnRootLoaded;
        }

        if (_startupRan)
        {
            return;
        }

        _startupRan = true;

        try
        {
            await _startup.RunAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Startup pipeline failed; continuing offline.");
        }
    }
}
