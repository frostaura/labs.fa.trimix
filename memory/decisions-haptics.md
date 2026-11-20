---
name: labs-fa-trimix-decisions-haptics
description: "Feel is engineered: a six-cue semantic port (never MAUI Essentials), cues on intent coalesced inside 120 ms, the HapticCommand decorator that must not be simplified back, Taptic Prepare, chart and merge off the UI thread."
type: decision
last_verified: 2026-10-22
---

# Haptics, motion and perceived performance

_Split out of [`decisions-ui.md`](decisions-ui.md), which keeps layout and visual chrome. Nothing here is cosmetic — each entry
is a founder call or a trap paid for once already. The matching runtime traps are in [`gotchas-ui.md`](gotchas-ui.md)._

- **Haptics are a semantic port, not a vibrate call.** `IHaptics` in Core carries six cues — Tap, Selection, Navigate, Success,
  Warning, Error — and the App maps them to each OS's own idiom: iOS Taptic Engine generators (impact / selection /
  notification), Android `View.performHapticFeedback` constants. **MAUI Essentials' `HapticFeedback` was deliberately not
  used**: it exposes only Click and LongPress and on Android drives the vibrator directly, which needs the `VIBRATE`
  permission and ignores the user's touch-feedback setting. Going native meant the manifest gained nothing and the system
  setting is respected for free.
- **Cues fire on the INTENT, never on arrival.** Buttons tick on `Pressed` (not `Clicked`), navigation on `Shell.Navigating`
  (not `Navigated`), scrub on `StartInteraction` only, and the two code-behind handlers that navigate from inside themselves
  (`PageHeader.OnBackTapped`, `BottomSheet.OnDismissRequested`) tick as their first statement. The point is perceived
  performance: the tick lands under the finger while the destination is still measuring.
- **Tap targets in lists tick on RELEASE, not touch-down** (founder decision). Touch-down would be earliest, but dive rows live
  inside a scrolling list and would tick when pressed-then-scrolled. No false positives was judged worth ~100 ms.
- **Light cues coalesce inside 120 ms; terminal cues never do.** Several hooks legitimately overlap on one gesture (button
  press → command → navigate) and two buzzes for one finger reads as a stutter. Success / Warning / Error are exempt because
  they report an outcome, and losing one loses information.
- **Cues fire from inside an `ICommand` decorator, not from `Tapped`.** `Trimix.App/Services/HapticCommand.cs` wraps a gesture
  recognizer's bound command so the cue provably precedes the command; a `TapGestureRecognizer` raises `Tapped` and invokes
  `Command` in the same dispatch and MAUI's ordering between them is an implementation detail that could not be verified
  locally. **Do not simplify this back to an event subscription**, and never do both — wrapping the command *and* subscribing
  `Tapped` double-ticks, because a navigating command easily outlasts the 120 ms coalesce window.
- **Coverage is centralised, not per-site.** One `ButtonHandler` mapping covers every Button; one `Feedback.AttachTapFeedback`
  per page covers every `TapGestureRecognizer` in its subtree, including rows materialized later from a DataTemplate (via
  `DescendantAdded`). The `Feedback.Cue` attached property (`services:Feedback.Cue="Selection"`) overrides the default
  Tap cue for a target that is a choice rather than a press, but **no XAML sets it today** — its one user, the dive-detail
  chip strip, was removed (`decisions-ui.md`); section swipes get `Selection` by a code default in `Feedback.cs` instead
  (re-read 2026-10-22). The attach call lives in `TrimixPage` / `TransparentModalPage`
  rather than being repeated in eight page constructors, so a page added later cannot forget it.
- **A capped sync reports Warning, not Success.** The run technically succeeded but ended by refusing to import dives and
  navigates straight to the paywall; Success would tell the diver the opposite of what happened. Pinned by test.
- **iOS generators are `Prepare()`d.** `Prepare()` spins the Taptic Engine up; without it the first cue of a session lands
  late. `Feedback.Warm()` runs on page appearance **and on touch-down** (a passive `PointerPressed` hook that emits no
  cue), and every cue re-arms its generator — page-appear alone left the cold path for anyone who reads before tapping
  ([`gotchas-ui.md`](gotchas-ui.md); re-read off `Services/Feedback.cs` 2026-10-22). This was likely a bigger contributor
  to "the haptic feels late" than ordering was.
- **`DiveDetailViewModel.LoadAsync` and `LogbookViewModel.LoadAsync` compute off the UI thread.** The four `ChartGeometry`
  builders over a ~5,600-sample profile plus `DiveMergePolicy.Merge` were stalling the thread driving the page-push animation.
  The `Task.Run` regions must stay **pure** — they are safe only because they touch no shared mutable state, and property
  assignment stays on the UI thread. The Core tests await `LoadAsync` and would catch a changed *result*, but cannot catch a
  *race*, so nothing will warn you if a later edit reaches into shared state.
- **Motion durations shortened, movement retained** (founder: "don't remove the slide, just make the animations faster").
  `screenIn` 350→200 ms, row-in 400→240 ms, row stagger 60→30 ms, section reveal 220→160 ms; the `translateY 10→0` and
  `translateX −8→0` movements are unchanged. `Motion.ScreenInMs` is the single knob if 200 ms reads as abrupt. `ProfilePane`'s
  2200 ms chart draw-in is deliberately left alone — decorative, and it blocks nothing.
