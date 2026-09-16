---
name: fa-trimix-state
description: "Serverless on-device API-sync-only dive log. Garmin + ScubaPro verified live, Suunto summary-only. Clean tree, ahead-only, context commits only. Suite green is a carried 2026-09-11 claim, not re-run. Five behaviours unseen on the iPhone."
type: state
last_verified: 2026-09-16
---

# Current state

## Durability — re-inspected 2026-09-16

- **`main` is ahead-only of `origin/main`; the unpushed range is context commits only and deletes
  no product file** (its one deletion is a retired context topic). The 2026-08-21 push landed the
  whole 2026-08-07 feature body *and* the harness plus all three reverse-engineering contracts,
  which until then had never been tracked at all, so **no product work lives only in this working
  tree any more** — the worst standing hazard in this program is closed and a plain `git push` is
  safe whenever the founder calls it.
- **Re-read locally 2026-09-16** — clean tree including untracked, ahead-only, every unpushed commit
  a `chore(context):` one, its single deletion a retired context topic, no `.git/*.lock` debris. **That
  is the tracking ref, not the remote:** the last *live* confirmation was a `git ls-remote origin main`
  on 2026-09-11, and a stale ref can fake an ahead-count ([root gotchas](../../../../memory/gotchas.md)),
  so fetch before acting on it. Remote is the private `frostaura/fa.trimix` over HTTPS via `gh` and must
  stay HTTPS for the reason root records once. One clone only, on this iCloud path.

## Build & test

- **The Core suite is real; green is a carried-forward claim.** Last actually run 2026-09-11
  (`~/.dotnet/dotnet test tests/Trimix.Core.Tests`, zero failures) and **not re-run since** — the
  2026-09-16 context pass had no build or test consent, and only inspected that the suite is
  substantial and real. No CI, so every green is a one-Mac result (`gotchas.md`). No count is recorded
  here on purpose: green-or-not is the signal.
- Last exercised 2026-08-07 and not re-checked since: both TFMs building, `net10.0-ios` compiling,
  installs on the Android emulator and the founder's physical iPhone 17 Air.

## What the product is right now

Serverless, on-device, API-sync-only dive log; 2-tab bar (Log · You); six vendor read connectors under
`Connectors/Cloud/`. **Live allowlist = `ProviderCatalog.LiveConnectorIds` = `garmin_connect`,
`scubapro`, `suunto_cloud`** — read off the source 2026-09-11. Dimmed "COMING SOON": Shearwater
(deliberate placeholder, its cloud is desktop/BLE), Oceanic+, Mares, PADI, SSI; onboarding S1 mirrors
both lists as static rows.

- **Garmin** — verified end-to-end on the founder's own account, full 1 Hz profile fidelity.
- **ScubaPro** — verified end-to-end over LogTRAK/Firebase (`logtrak2-production`), full profile,
  through the reusable `FirebaseCloudClient`. It does not use MySSI.
- **Suunto** — sign-in and dive-list verified (Sports Tracker), **summary fidelity only**: depth/temp
  and the diving activityId are unconfirmed, so the profile leg degrades and an account with no real
  dives lists nothing.
- Oceanic+, Mares and the PADI/SSI push path have never touched a live server; Mares's MySSI
  assumption is suspect (`decisions-connectors.md`).

Provider priority with overlap merge, unlink hard-delete plus a launch orphan sweep, and the grouped
Providers screen with native drag-reorder are all in (`decisions-sync.md`); the auto-sync flash is a
cosmetic open item whose fix was reverted.

## Never observed on the founder's iPhone

Emulator-verified or code-complete only. Two need real data from two overlapping providers, which an
emulator cannot seed: **multi-provider merge** (one row, both badges in priority order, the priority
provider's stats leading, "SOURCES · TAP TO COMPARE" swapping dive-detail to the other recording in
place) and **flash-free sync** (dimmed backdrop and sheet the instant the logbook appears). Three
shipped feel behaviours have never been watched by anyone: reorder feel on device, the embedded iOS
map gesture, and the logbook tap haptic. iOS section paging is an open question, not a verification
item — `questions.md`.
