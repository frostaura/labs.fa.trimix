---
name: fa-trimix-gotchas-ui
description: "MAUI runtime traps paid for once: handler mappers re-run and loop the layout, DescendantAdded goes quadratic, anything interactive in a ScrollView must claim the touch stream on end AND cancel, haptics need a runloop yield."
type: gotcha
last_verified: 2026-11-20
---

# Gotchas — MAUI runtime UI

_Build/deploy traps: [`gotchas-build.md`](gotchas-build.md). Durability and capture hygiene: [`gotchas.md`](gotchas.md). Connector verification: [`gotchas-connectors.md`](gotchas-connectors.md)._

- **A MAUI handler mapping re-runs on EVERY property update of its view, so it must never assign a fresh reference-typed
  value unconditionally.** A new `RoundRectangle` for `StrokeShape` or a new `SolidColorBrush` for `Background` inside a
  `BorderHandler` mapping raises property-changed → invalidate → re-measure → the mapping runs again → two more instances
  that can never compare equal: an unbounded layout loop. It does NOT show at rest — the page renders fine, then hard-freezes
  when anything triggers a layout pass, e.g. the soft keyboard resizing the page on a field tap. Guard with a
  `ConditionalWeakTable` so it runs once per view; "assign only if different" fails, a fresh brush is never equal.
- **`Element.DescendantAdded` fires once per descendant at every depth — do not walk the subtree in its handler.** That
  re-walks the same elements once per ancestor (quadratic in page size), on the UI thread, re-triggered by every relayout that
  re-parents anything. Hook the added element only. This and the mapper bug above presented identically: "freezes on tap".
- **ANY interactive child inside a `ScrollView` must claim the touch stream, or the scroll container eats it.** Bitten twice
  in unrelated controls and it will bite again: the **chart scrub** (`GraphicsView`) got a CancelInteraction the moment a
  dragging finger drifted off horizontal, so the crosshair kept vanishing mid-drag — nobody drags a straight line on a phone;
  the **entry-point map** (`MKMapView`) scrolled the pane instead of panning, which is why it originally shipped inert, the
  cheap workaround the founder rejected. Fix is per-platform: Android
  `PlatformView.Parent.RequestDisallowInterceptTouchEvent(true)`, iOS `UIScrollView.ScrollEnabled = false`. **Arm on
  touch-down and release on BOTH the end and the cancel path** — releasing on one leaves the page permanently unscrollable.
  Where the child has no MAUI-level touch events (the map), a zero-duration `UILongPressGestureRecognizer` with
  `CancelsTouchesInView=false` + `ShouldRecognizeSimultaneously => true` is a passive probe that consumes nothing.
- **A MAUI gesture-recognizer drag does NOT work as a reorder handle inside a `ScrollView` on Android — it receives ZERO touch
  events** (verified 2026-08-07 with `PanGestureRecognizer` + `PointerGestureRecognizer` and a solid-background grip). Use a
  native `CollectionView` with `CanReorderItems`, **outside** any ScrollView with a bounded content height, or the
  GraphicsView-touch approach the chart scrub uses. Up/down buttons work (taps aren't stolen); the founder rejected them.
- **The sync-sheet overlay is timing-sensitive; the known-good ordering is nav-FIRST then `RunAsync`.**
  `SyncSessionViewModel.SyncNowAsync` / `AutoSyncAfterLinkAsync` navigate to `//Logbook` and THEN set `IsSyncing=true`, so
  the `//tab` nav lands LogbookPage as foreground page before the flip and its `OnSyncPropertyChanged` reliably shows the
  sheet for both the from-Providers and on-Logbook cases. **Flipping `IsSyncing` before the nav breaks the add-sheet case**
  (the reverted flash-fix: [`decisions-sync.md`](decisions-sync.md)). Do not reorder without device-testing both paths.
- **`MainThread.InvokeOnMainThreadAsync` always posts, even on the UI thread** — a dispatcher frame lost per navigation.
  `ShellNavigator` short-circuits on `MainThread.IsMainThread`.
- **Firing a haptic before the command is NOT enough — you must also yield the runloop.** Emitting a cue only hands the
  impact to the system; the main thread must still turn over before the hardware is driven, and a navigating command jams it
  immediately (DI resolve → XAML inflation → transition). It presented as "a massive delay" while the code read as correct. `HapticCommand.Execute` posts the inner command via
  `MainThread.BeginInvokeOnMainThread` and `PageHeader.OnBackTapped` uses `await Task.Yield()` — one runloop turn (~16 ms).
- **iOS haptics go late when the Taptic Engine has idled**, which takes a couple of seconds. `Prepare()` on page-appear is
  not enough — a user who reads a list before tapping always gets the cold path. Prepare on touch-DOWN (a passive
  `PointerGestureRecognizer` emitting no cue, so a press that becomes a scroll cannot tick) and fire on release. Android
  needs none of this, which is why an emulator trace showed perfect timing while the phone felt broken — **never diagnose iOS
  haptic latency from Android logs.**
- **`BindingContextChanged` on a child view fires BEFORE the viewmodel has loaded its data.** Dive-detail panes are
  constructed with the page, well before `LoadAsync` populates `Dive`, so any child that reads model state there without ALSO
  watching the relevant `PropertyChanged` sees nulls once and never rebuilds. This silently killed the embedded iOS
  entry-point map for weeks — it cleared `MapHost.Content` for want of coordinates and nothing rebuilt it, so iOS looked like
  it had Android's hand-off behaviour. `ProfilePane` gets this right; anything reading `vm.Dive` must too.
- **`Auto` grid columns do NOT align across sibling rows.** Each events-list row is its own `Grid`, so an `Auto` first column
  is measured per row and the labels visibly stagger. Use a fixed width for any column that must line up down a list, and
  centre vertically explicitly when the columns differ in font size (13 pt time and 16 pt label share no baseline).
