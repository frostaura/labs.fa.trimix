---
name: fa-trimix-gotchas-build
description: "Build, deploy and verification traps paid for once already: EmbedAssembliesIntoApk or Android runs stale code, chained installs hiding a failed build, stale screencaps, a shared emulator, devicectl error 7, XAML tooling that fails silently"
type: gotcha
last_verified: 2026-08-07
---

# Build, deploy and on-device verification gotchas

_Runtime UI and interaction traps are in [`gotchas-ui.md`](gotchas-ui.md); durability, connector and reverse-engineering ones
in [`gotchas.md`](gotchas.md). This app's toolchain and per-program traps are the `maui-device-build` skill in
[`../../../.claude/skills/`](../../../.claude/skills/README.md), and the generic MAUI build-and-deploy procedure that skill
defers to is a capability of the installed Gaia plugins — everything below is what neither covers, because it is specific to
this app's connectors, emulator and XAML. Carried at their original date; not re-verified 2026-08-21._

- **`-p:EmbedAssembliesIntoApk=true` is mandatory for Android device testing.** .NET Android Debug builds keep assemblies
  OUTSIDE the APK, so `adb install` of a rebuilt APK leaves the previous managed code in place and the app silently runs
  stale. This cost real time: a connector rewrite appeared to fail against Garmin when the device was still executing the old
  CSRF-scraping flow, and the tell was an error message with the OLD wording. The APK jumps from ~19 MB to ~94 MB when the
  flag is on — that size change is the quickest confirmation.
- **A failed build does not stop a chained `adb install`.** Chaining build and install with `;` (or with a `grep -c` between,
  which exits non-zero when it finds nothing) can install a stale APK and produce screenshots that "disprove" a fix that was
  never deployed. Check `Build succeeded` explicitly.
- **`adb exec-out screencap` can return a stale framebuffer.** Screenshots taken right after a tap sometimes show the previous
  screen; `uiautomator dump` disagreeing with a screenshot means the screenshot is stale, not that the UI is broken. Capture
  twice and trust the second, or trust the dump.
- **`uiautomator dump` returns nothing while an animation is running** (it waits for window idle). An empty dump is a hint
  that something is animating — or hung.
- **`adb shell input swipe` cannot validate a drag gesture** — it reads as a scroll fling. Drag feel is device + real-finger
  only, and MAUI's opaque canvas defeats screenshot verification of anything transient.
- **The Android emulator is shared with `net.frostaura.taleweaver`,** which steals the foreground. Always re-launch by
  resolved activity (`cmd package resolve-activity --brief <pkg>`) and check `dumpsys window | grep mCurrentFocus` before
  trusting a screenshot or a scripted tap — an unchecked tap sequence lands in the other app and produces a screenshot that
  "disproves" a fix.
- **`devicectl device install` succeeding but launch failing with `FBSOpenApplicationErrorDomain error 7` just means the
  iPhone is locked.** The install is real; unlock and launch. Not a signing problem.
- **Garmin rate-limits sign-in** — four logins within a few minutes returned HTTP 429. Use the harness's
  `TRIMIX_TOKEN_CACHE` so dev iterations reuse the token instead of re-authenticating.
- **A simulator can be seeded with a real dive store**: copy the Android `files/trimix.db3` (via `adb exec-out run-as`) into
  the simulator container's `Library/trimix.db3`. Same code, same schema — far faster than driving the simulator UI through a
  provider link.
- **`plutil` treats dots in a key as a key path**, so it cannot write MAUI's dotted preference keys. Use
  `PlistBuddy -c 'Add :trimix.some.key string 1'`, which uses `:` as its separator. `simctl spawn defaults write` targets the
  wrong domain and does nothing useful for an app container.
- **XAML source generation rejects `x:Reference` bindings across a `DataTemplate` boundary** ("Binding with x:DataType from
  outer scope"). Do that wiring in code-behind.
- **`OnPlatform` setters are not uniformly reliable in this project's XAML.** In the `InputField` style an `OnPlatform` setter
  for `Padding` (Thickness) applied correctly while `StrokeThickness` (x:Double) and `StrokeShape` (Shape) silently did NOT —
  no build error, no warning, just the base style's values. If a platform difference must be guaranteed, express it in a
  handler mapping, not a style setter.
