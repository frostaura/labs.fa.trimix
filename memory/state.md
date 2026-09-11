---
name: fa-trimix-state
description: "Serverless on-device API-sync-only dive log. Garmin + ScubaPro verified live, Suunto summary-only. Repo clean and pushed, Core suite real and green (2026-09-11). Five shipped behaviours never seen on the founder's iPhone."
type: state
last_verified: 2026-09-11
---

# Current state

## Durability — inspected 2026-09-11

- **The working tree is clean and `main` matches its `origin/main` tracking ref.** The last push
  (2026-08-21) landed both the whole 2026-08-07 feature body *and* the harness plus all three
  reverse-engineering contracts, which until then had never been tracked at all. **Nothing of value
  now lives only in this working tree** — the worst standing hazard in this program is closed.
- **No fetch has run since 2026-08-07**, so the tracking ref is local evidence of the last push from
  this clone, not a live read of GitHub — fetch before concluding anything about the remote. Remote is
  the private `frostaura/fa.trimix` over HTTPS via `gh` (no SSH key on this Mac); one clone only, on
  this iCloud path; no `.git/*.lock` debris at this inspection.

## Build & test

- **The Core suite is real and fully green** — `dotnet test tests/Trimix.Core.Tests`, re-run
  2026-09-11 on the user-local `~/.dotnet` toolchain, zero failures across 51 test files. There is no
  CI, so this is a one-Mac result and nothing else will catch a regression (`gotchas.md`).
- Verified 2026-08-07 and not re-checked since: both TFMs building, `net10.0-ios` still compiling,
  installs on the Android emulator and on the founder's physical iPhone 17 Air.

## What the product is right now

Serverless, on-device, API-sync-only dive log; 2-tab bar (Log · You); six vendor read connectors
under `Connectors/Cloud/`. **Live allowlist = `ProviderCatalog.LiveConnectorIds` = `garmin_connect`, `scubapro`, `suunto_cloud`**
— read off the source 2026-09-11. Dimmed "COMING SOON": Shearwater (deliberate placeholder, its cloud
is desktop/BLE), Oceanic+, Mares, PADI, SSI. Onboarding S1 mirrors the same two lists as static rows.

- **Garmin** — verified end-to-end on the founder's own account, full 1 Hz profile fidelity.
- **ScubaPro** — verified end-to-end over LogTRAK/Firebase (`logtrak2-production`), full profile,
  through the reusable `FirebaseCloudClient`. It does not use MySSI.
- **Suunto** — sign-in and dive-list verified (Sports Tracker), **summary fidelity only**: depth/temp
  and the diving activityId are unconfirmed, so the profile leg degrades and an account with no real
  dives lists nothing.
- Oceanic+, Mares and the PADI/SSI push path have never touched a live server; Mares's MySSI
  assumption is suspect (`decisions-connectors.md`).

Provider priority with overlap merge, unlink hard-delete plus a launch orphan sweep, and the grouped
Providers screen with native drag-reorder are all in (`decisions-sync.md`). The auto-sync flash is a
cosmetic open item whose fix was reverted.

## Never observed on the founder's iPhone

Emulator-verified or code-complete only. Two need real data from two overlapping providers, which an
emulator cannot seed:

1. **Multi-provider merge** — one row, both badges in priority order, the priority provider's stats
   leading, and "SOURCES · TAP TO COMPARE" swapping dive-detail to the other recording in place.
2. **Flash-free sync** — dimmed backdrop and sync sheet the instant the logbook appears.

Three shipped feel behaviours have never been watched by anyone: reorder feel (haptics/animation) on
device, the embedded iOS map gesture, and the logbook tap haptic. iOS dive-detail section paging is a
harder case and is an open question, not a verification item — `questions.md`.
