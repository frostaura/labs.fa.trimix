---
name: fa-trimix-decisions-ui
description: "Text inputs and the entry-point map are per-platform BY DESIGN — do not unify. CarouselView rejected for dive-detail; the chip strip is gone so swipe is the only pager, an open iOS risk; chart markers removed; skeleton until loaded."
type: decision
last_verified: 2026-08-07
---

# UI & layout decisions

_Split out of [`decisions.md`](decisions.md), which stays product/connector/legal; haptics, motion and perceived performance
are in [`decisions-haptics.md`](decisions-haptics.md). Nothing here is cosmetic — every entry records a founder call or a trap
paid for once already._

- **Text inputs are DELIBERATELY different per platform.** Android gets a Material **filled** field: no outline, container
  fill, top corners rounded and bottom square, with a bottom indicator drawn by the EditText (dim 1 dp at rest, mint 2 dp on
  focus) and the 16 dp gutter on the field so the indicator spans the container's full width. iOS keeps the app's outlined
  card with a borderless field, which is the iOS form idiom. **Do not unify these** — a previous pass stripped both platforms
  to a bare rounded box, which is exactly what made the Android build read as an iOS app.
- **The Android indicator is a custom `Drawable`, not a `BackgroundTintList`.** On a Material3 host theme the stock EditText
  background is a filled box, so tinting it paints a solid rectangle inside the card. `MaterialUnderlineDrawable` is stateful
  (`IsStateful` + `OnStateChange`), so focus response needs no listener on the field and survives view recycling.
- **The entry-point map is deliberately per-platform**: embedded MapKit on iOS (free, keyless), hand-off to the Google Maps app
  on Android, because an embedded Android map needs a Maps SDK key in the APK plus a billable Cloud project. The Maps package
  is referenced only for iOS TFMs, so the Android build carries none of it; Android is a strict subset of the iOS card, not a
  different design. `#if IOS` in `EntryPointCard.xaml.cs` is the single place to change if a key is ever provisioned.
- **On iOS that map is a LIVE MapKit view, not an inert picture.** It was originally pinned with `IsZoomEnabled=false` /
  `IsScrollEnabled=false` / `InputTransparent=true` to dodge a gesture fight with the enclosing scroll view — the cheap way
  out, which the founder rejected ("I want an actual interactable embedded map"). The conflict is now solved properly (see
  `gotchas-ui.md`), and the map sits at the **top** of the Details pane. The coordinate row still opens full-screen Apple Maps
  for directions; the map itself no longer navigates away when touched.
- **`CarouselView` was rejected for the dive-detail sections.** It overrides each item's `BindingContext` with the item, so
  view-model bindings inside a pane resolve against an enum; working around that fought the control on every re-measure and
  produced an ANR. The page uses static sibling panes with one visible at a time.
- **The section chip strip was removed; each pane owns its heading** (founder direction) — sections are identified from inside
  the content (`PROFILE`, `DETAILS`, `EVENTS`, `NOTES`) with the `IndicatorView` dots as the only position affordance. This
  **superseded the earlier "section switching is TAP-first" decision** ("the carousel items can be moved via sliding"), so
  swipe is now the only switching control. The original reasoning still stands as a **risk, not a refutation**: in-pane swipe
  recognizers fire on Android but are swallowed by `UIScrollView`'s pan on iOS, which is precisely why a tappable switcher
  existed. If iOS cannot page, restore switching with a pan-based implementation — do **not** re-add the chips, which the
  founder removed deliberately. Tracked in [`questions.md`](questions.md).
- **Dive-detail sections slide directionally.** Pane `IsVisible` moved off the `Is*PaneVisible` bindings into
  `DiveDetailPage.ShowPane`, because a slide needs outgoing and incoming visible simultaneously, which mutually-exclusive
  bindings cannot express; the VM properties remain the semantic answer. Direction comes from the change in `PaneIndex`
  (position in the live pane list), never from pane kind — a dive with no events has a different list, so Notes can be index 1
  on one dive and 3 on another. **This is a transition, not a finger-tracking carousel**: drag-follows-thumb needs a
  `PanGestureRecognizer`, which would fight the chart scrub and iOS's `UIScrollView` pan.
- **Event markers were removed from the chart** (founder direction). The provider's own alert list on the Events section
  carries the same information more authoritatively — Garmin reports "Ascent critical" and "Safety stop started" itself.
  `ProfileOverlays.Markers` is still built and tested (it holds the recorded deco-onset signal) but nothing paints it.
- **The scrub tooltip's layout is deliberate, not incidental.** Fixed `WidthRequest`, because sizing to content re-laid it out
  on every digit change and its follow-the-finger translation is computed from its own Width, so the jitter compounded. Depth
  is left-aligned and temp+time right-aligned — depth changes every sample, so left-floating it stops it shoving the stable
  readouts sideways. `FontAutoScalingEnabled="False"` so accessibility sizing cannot overflow the box. Do not "tidy" it into
  auto-sizing.
- **The accessible profile table is built on first open, not on load**, and both the logbook and dive detail show a pulsing
  skeleton until an `IsLoaded` flag flips. Rendering the empty state first and swapping in real data was the app asserting
  something untrue.
