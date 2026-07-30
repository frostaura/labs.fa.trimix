using System.Linq;
using Trimix.Core;
using Trimix.Core.Abstractions;

namespace Trimix.App.Services;

/// <summary>
/// <see cref="INavigator"/> over Shell navigation. Tab roots (Logbook · Sites · Stats/"You")
/// navigate absolutely ("//route", which also dismisses any modal stack — e.g. onboarding → logbook);
/// the modal/pushed routes registered in <see cref="AppShell"/> — Import, dive/site detail, paywall
/// and the provider flows, which are pushed flows rather than tabs — push relatively.
/// </summary>
public sealed class ShellNavigator : INavigator
{
    private static readonly HashSet<string> TabRoutes = new(StringComparer.Ordinal)
    {
        Routes.Logbook,
        Routes.Stats,
    };

    public Task GoToAsync(string route, IReadOnlyDictionary<string, object>? parameters = null) =>
        MainThread.InvokeOnMainThreadAsync(async () =>
        {
            // First-run: onboarding was the window ROOT (no Shell yet — that is what keeps the Logbook
            // from flashing before onboarding). Completing onboarding lands here; swap the root to the
            // main Shell, which opens on the Logbook tab. This is the one-time onboarding → app handoff.
            if (Shell.Current is null)
            {
                var window = Application.Current?.Windows.FirstOrDefault();
                if (window is not null)
                {
                    window.Page = new AppShell();
                }

                if (route == Routes.Logbook)
                {
                    return; // the fresh AppShell already opens on the Logbook tab
                }
            }

            var target = TabRoutes.Contains(route) ? $"//{route}" : route;

            // A bottom-sheet overlay (the app's only PushModalAsync modal — the add/sync sheet host)
            // must never survive a navigation. If one is up when we push a route (e.g. add-sheet
            // "Sync now" / "Manage providers" with nothing linked → Providers), leaving it on the modal
            // stack strands a transparent, input-blocking page over the destination, and its deferred
            // self-pop (fired when the sheet's close animation ends) would tear down the WRONG page —
            // exactly the "page closes itself, then the app freezes" symptom. Absolute tab routes
            // ("//route") already flush the modal stack; clear it explicitly for pushed routes too.
            if (!TabRoutes.Contains(route))
            {
                var navigation = Shell.Current.Navigation;
                while (navigation.ModalStack.Count > 0)
                {
                    await navigation.PopModalAsync(false);
                }
            }

            if (parameters is null)
            {
                await Shell.Current.GoToAsync(target);
            }
            else
            {
                await Shell.Current.GoToAsync(target, new Dictionary<string, object>(parameters));
            }
        });

    public Task GoBackAsync() =>
        MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var navigation = Shell.Current.Navigation;
            if (navigation.ModalStack.Count > 0 || navigation.NavigationStack.Count > 1)
            {
                await Shell.Current.GoToAsync("..");
            }
            else
            {
                // Already at a tab root (e.g. Import results "Done") — land on the Logbook home.
                await Shell.Current.GoToAsync($"//{Routes.Logbook}");
            }
        });
}
