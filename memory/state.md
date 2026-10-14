---
name: fa-trimix-state
description: "Serverless on-device API-sync-only dive log, dormant: no code commit since 2026-08-21. Garmin + ScubaPro verified live, Suunto summary-only. Ahead-only by context commits, clean. Suite green is a carried 2026-09-11 claim."
type: state
last_verified: 2026-10-14
---

# Current state

## Activity — dormant since August

The last commit touching `src/` or `tests/` is the 2026-08-21 durability push; the feature body it
carried is the 2026-08-07 work; every commit since is context only. **No milestone has moved**, and M3
cannot start until the telemetry question in [`questions.md`](questions.md) is answered.

## Durability — probed 2026-10-14

- **Ahead-only, context commits only.** After `git fetch`, `git ls-remote origin HEAD` matched the
  pre-pass `HEAD`; this pass's own context commit then sits local-only on top. Clean tree including
  untracked, no `.git/*.lock` debris. Safe to push, never pushed by an agent; re-probe before acting.
- **Nothing in this program lives on one disk.** The harness and all three reverse-engineering contracts
  are tracked and pushed (`git ls-files` confirms). Remote is the private `frostaura/fa.trimix` over
  HTTPS via `gh` (`gh repo view`: private). One clone only, on this iCloud path; there is no `~/src`
  dev clone.

## Build & test

- **The Core suite is real; green is a carried-forward claim.** Last actually run 2026-09-11
  (`~/.dotnet/dotnet test tests/Trimix.Core.Tests`, zero failures) and **not re-run since** — the
  2026-09-16 and 2026-10-14 context passes ran no build, because a build writes `bin/`/`obj/` into the
  iCloud tree. No source has changed since that run, so the claim is stale in date only. No CI, so every
  green is a one-Mac result (`gotchas.md`). No count is recorded here on purpose.
- `~/.dotnet` reports SDK 10.0.301, as `CLAUDE.md` names (2026-10-14). Last exercised 2026-08-07: both
  TFMs building, installs on the Android emulator and the founder's physical iPhone 17 Air.

## What the product is right now

Serverless, on-device, API-sync-only dive log; 2-tab bar (Log · You); six vendor read connectors under
`Connectors/Cloud/`. **Live allowlist = `ProviderCatalog.LiveConnectorIds` = `garmin_connect`,
`scubapro`, `suunto_cloud`** — read off the source 2026-10-14. Dimmed "COMING SOON": Shearwater
(deliberate placeholder, its cloud is desktop/BLE), Oceanic+, Mares, PADI, SSI; onboarding S1 mirrors
both lists as static rows. Free tier keeps the last 5 dives (`FreeTier`).

- **Garmin** — verified end-to-end on the founder's own account, full 1 Hz profile fidelity.
- **ScubaPro** — verified end-to-end over LogTRAK/Firebase (`logtrak2-production`), full profile,
  through the reusable `FirebaseCloudClient`. It does not use MySSI.
- **Suunto** — sign-in and dive-list verified (Sports Tracker), **summary fidelity only**. Oceanic+,
  Mares and PADI/SSI push have never touched a live server (`decisions-connectors.md`).

Provider priority with overlap merge, unlink hard-delete plus a launch orphan sweep, and the grouped
Providers screen with native drag-reorder are all in (`decisions-sync.md`); the auto-sync flash is a
cosmetic open item whose fix was reverted.

## Never observed on the founder's iPhone

Emulator-verified or code-complete only. Two need real data from two overlapping providers, which an
emulator cannot seed: **multi-provider merge** (one row, both badges in priority order, "SOURCES · TAP
TO COMPARE" swapping dive-detail in place) and **flash-free sync**. Three shipped feel behaviours have
never been watched by anyone: reorder feel on device, the embedded iOS map gesture, and the logbook tap
haptic. iOS section paging is an open question, not a verification item — `questions.md`.
