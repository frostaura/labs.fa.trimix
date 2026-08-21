using Trimix.Core.ViewModels;

namespace Trimix.App.Controls;

/// <summary>
/// The entry-point card. The tappable coordinate row is shared; the embedded map is iOS-only.
///
/// <para><b>Why the platform split.</b> MapKit on iOS is free and needs no API key, so an inline map costs
/// nothing. Android's equivalent requires a Google Maps SDK key baked into the APK plus a billable Cloud
/// project — a dependency and a secret this app deliberately does not have. Android therefore hands off to the
/// Google Maps app instead, which is still a native map. The Maps package is referenced only for iOS target
/// frameworks so the Android build carries none of it.</para>
///
/// <para>If an Android Maps key is ever provisioned, the <c>#if IOS</c> below is the single place to change.</para>
/// </summary>
public partial class EntryPointCard : ContentView
{
    private DiveDetailViewModel? _viewModel;

    public EntryPointCard()
    {
        InitializeComponent();
        BindingContextChanged += OnBindingContextChanged;
    }

    /// <summary>
    /// Rebuilds when the binding context changes AND whenever the dive itself lands.
    ///
    /// <para>Watching <see cref="DiveDetailViewModel.Dive"/> is the load-bearing half. BindingContextChanged
    /// fires when the pane is constructed, which is BEFORE <c>LoadAsync</c> has populated the dive — so at
    /// that moment there are no coordinates, the map is cleared, and without a second trigger it is never
    /// built again. That is why the embedded iOS map never appeared and the card looked like an
    /// Android-style hand-off on both platforms.</para>
    /// </summary>
    private void OnBindingContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = BindingContext as DiveDetailViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        BuildMap();
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DiveDetailViewModel.Dive) or nameof(DiveDetailViewModel.HasEntryPoint))
        {
            BuildMap();
        }
    }

    private void BuildMap()
    {
#if IOS
        if (BindingContext is not DiveDetailViewModel vm
            || vm.Dive?.Environment is not { EntryLatitude: { } lat, EntryLongitude: { } lon })
        {
            MapHost.Content = null;
            return;
        }

        var location = new Microsoft.Maui.Devices.Sensors.Location(lat, lon);
        var map = new Microsoft.Maui.Controls.Maps.Map
        {
            HeightRequest = 220,
            // A real, live MapKit view: pan, pinch-zoom and rotate all work. It was previously pinned inert
            // to keep it from fighting the pane's vertical scroll and the section swipe; that conflict is now
            // handled properly in HookInteractiveGestures below rather than by disabling the map.
            IsZoomEnabled = true,
            IsScrollEnabled = true,
        };

        map.Pins.Add(new Microsoft.Maui.Controls.Maps.Pin
        {
            Label = string.IsNullOrWhiteSpace(vm.Title) ? "Dive site" : vm.Title,
            Location = location,
        });

        map.MoveToRegion(Microsoft.Maui.Maps.MapSpan.FromCenterAndRadius(
            location, Microsoft.Maui.Maps.Distance.FromKilometers(2)));

        // The native view only exists once the handler is created.
        map.HandlerChanged += (_, _) => HookInteractiveGestures(map);
        MapHost.Content = map;
#else
        // Android and everything else: the tappable row IS the card.
        MapHost.Content = null;
#endif
    }

#if IOS
    /// <summary>
    /// Lets the map own the touch for as long as a finger is on it.
    ///
    /// <para>An interactive MKMapView nested inside a vertical UIScrollView is a genuine gesture fight: the
    /// scroll view's pan recognizer claims touches that begin on its subviews, so dragging the map scrolls
    /// the pane instead of moving the map — which is exactly why the map was originally shipped inert. The
    /// fix is to disable the ancestor scroll view's panning while a finger is down on the map, and restore it
    /// the moment the touch ends.</para>
    ///
    /// <para>A zero-duration long-press recognizer is used purely as a touch-down/up probe.
    /// <c>CancelsTouchesInView=false</c> and simultaneous recognition mean it observes without consuming, so
    /// every one of MapKit's own pan/pinch/rotate gestures still works normally.</para>
    /// </summary>
    private static void HookInteractiveGestures(Microsoft.Maui.Controls.Maps.Map map)
    {
        if (map.Handler?.PlatformView is not MapKit.MKMapView native)
        {
            return;
        }

        // HandlerChanged can fire more than once (attach, detach, re-attach); stacking probes would toggle
        // the ancestor's scrolling several times per touch.
        if (native.GestureRecognizers?.Any(r =>
                r is UIKit.UILongPressGestureRecognizer { MinimumPressDuration: 0 }) == true)
        {
            return;
        }

        static UIKit.UIScrollView? EnclosingScroll(UIKit.UIView? view)
        {
            for (var v = view?.Superview; v is not null; v = v.Superview)
            {
                if (v is UIKit.UIScrollView scroll)
                {
                    return scroll;
                }
            }

            return null;
        }

        var probe = new UIKit.UILongPressGestureRecognizer(g =>
        {
            var scroll = EnclosingScroll(native);
            if (scroll is null)
            {
                return;
            }

            scroll.ScrollEnabled = g.State is not (UIKit.UIGestureRecognizerState.Began
                or UIKit.UIGestureRecognizerState.Changed);
        })
        {
            MinimumPressDuration = 0,
            // Observe only — never swallow a touch MapKit needs.
            CancelsTouchesInView = false,
            DelaysTouchesBegan = false,
            DelaysTouchesEnded = false,
        };

        probe.ShouldRecognizeSimultaneously = (_, _) => true;
        native.AddGestureRecognizer(probe);
    }
#endif
}
