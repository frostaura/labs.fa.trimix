---
name: fa-trimix-state
description: "Serverless, on-device, API-sync-only dive log; Garmin + ScubaPro verified live, Suunto supported at summary fidelity; 410/410 Core tests as of 2026-08-07; a large uncommitted body plus an unpushed context commit; four behaviours awaiting founder device verification"
type: state
last_verified: 2026-08-21
---

# Current state

## ⚠ Durability — inspected 2026-08-21, decays fast

Re-run `repo-durability-check` before repeating any of this. **No fetch was performed**, so ahead/behind is
measured against the ref last fetched 2026-08-07.

- **A large uncommitted body** — the whole 2026-08-07 work *plus* the never-tracked harness and RE
  contracts (see the alert topic). It exists nowhere but this Mac, **unchanged for two weeks**, on an
  iCloud path where eviction is a known hazard. Run `audit-tree.py` for the current path count.
- **`main` is AHEAD-ONLY of `origin/main`** — the unpushed range is `b160536` (memory topic-store
  migration) onward, zero deletions, a clean fast-forward. Remote is private `frostaura/fa.trimix` over HTTPS via `gh`.
- Stale `.git/index.lock` + `.git/objects/maintenance.lock` were present on 2026-08-21 and cleared in the
  same pass; sweep `find .git -name '*.lock'` before concluding a commit failed for another reason.
- **Committing and pushing has been the highest-value action here since 2026-08-07**, awaiting founder go.

## Build & test — verified 2026-08-07, not re-run since

**410/410 Core tests green**; both TFMs build with 0 errors and `net10.0-ios` still compiles. Installed on the
Android emulator and wirelessly on the physical iPhone 17 Air (`devicectl`, device
`CAF80486-0AA8-5641-B865-93773026D3A9`). Not re-run 2026-08-21 — this workspace cannot build the repo.

## What the product is right now

Serverless, on-device, API-sync-only dive log, 2-tab bar (Log · You), six read connectors under
`Connectors/Cloud/`. `src/tools/Trimix.Harness` is deliberately outside `fa.trimix.slnx` *and* accidentally
outside git.

**Live allowlist = `ProviderCatalog.LiveConnectorIds` = `garmin_connect`, `scubapro`, `suunto_cloud`.**
Coming soon: Shearwater (deliberate placeholder — Shearwater Cloud is desktop/BLE), Oceanic+, Mares, PADI, SSI.
Onboarding S1 mirrors the same two lists as static wrap rows.

- **Garmin** — verified end-to-end on the founder's own account: dives #66–69, Sodwana, 5,581 samples at 1 Hz.
- **ScubaPro** — verified end-to-end: LogTRAK/Firebase (`logtrak2-production`), 22 dives, full 311-sample
  profile, through the reusable `FirebaseCloudClient`. It does NOT use MySSI.
- **Suunto** — sign-in + dive-list verified (Sports Tracker), **summary fidelity only**: depth/temp and the
  diving activityId are unconfirmed so the profile leg degrades; an account with no real dives lists nothing.
- Oceanic+, Mares and PADI/SSI push have never touched a live server; Mares's MySSI assumption is suspect.

Provider priority + overlapping-dive merge, unlink hard-delete with a launch orphan sweep and the grouped
Providers screen with native drag-reorder are in (`decisions-sync.md`). The auto-sync flash is a cosmetic open
item; its fix was reverted.

## Awaiting founder verification on the iPhone

Emulator-verified: reorder persists, hint shows, badges render, no crash. What needs the founder's device and
real data (two overlapping providers cannot be seeded on an emulator):

1. **Multi-provider merge** — one row, both badges in priority order, the priority provider's stats leading,
   and the "SOURCES · TAP TO COMPARE" strip swapping dive-detail to the other provider's version in place.
2. **Flash-free sync** — dimmed backdrop + sync sheet the instant the logbook appears.
3. Reorder feel (haptics/animation) on device, iOS dive-detail section paging, the embedded iOS map gesture
   and the logbook tap haptic — all shipped, none observed by an agent.
