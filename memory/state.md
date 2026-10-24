---
name: fa-trimix-state
description: "Serverless on-device API-sync-only dive log, dormant: no code commit since 2026-08-21. Garmin + ScubaPro verified live, Suunto summary-only. Ahead-only by unpushed context commits, clean. Core suite re-run green 2026-10-22."
type: state
last_verified: 2026-10-22
---

# Current state

## Activity — dormant since August

The last commit touching `src/` or `tests/` is `555ca07`, the 2026-08-21 durability push; it carried
the 2026-08-07 feature body (dated by `decisions-sync.md`, not provable from git). Every commit since is
context only. **No milestone has moved**; M3 waits on the telemetry question in `questions.md`.

## Durability — probed by hand 2026-10-22

- **Ahead-only, context commits only.** After `git fetch`, `git rev-list --left-right --count
  @{u}...HEAD` shows nothing behind, and `git ls-remote origin HEAD` is the 2026-09-16 context commit;
  every unpushed commit on top is a 2026-10-14 `chore(context)` commit. Clean including untracked at
  probe time, bar context edits left uncommitted for the owner; no lock debris. A fast-forward push
  would lose nothing; never pushed by an agent; re-probe before acting.
- **Nothing in this program lives on one disk.** The harness and all three reverse-engineering contracts
  are tracked and pushed (`git ls-files` confirms). Remote is the private `frostaura/fa.trimix` over
  HTTPS via `gh` (`gh repo view`: private). One clone only, on this iCloud path; there is no `~/src`
  dev clone.

## Build & test

- **The Core suite is green, re-run 2026-10-22** (`~/.dotnet/dotnet test
  tests/Trimix.Core.Tests/Trimix.Core.Tests.csproj --no-restore`, zero failures, zero warnings in the
  log; it wrote only gitignored `bin/`/`obj/`). No CI, so every green is a one-Mac result
  (`gotchas.md`). No count is recorded here on purpose.
- `Trimix.App` is exempt from `TreatWarningsAsErrors` in `Directory.Build.props`, so the app build can
  carry warnings the suite never sees — [`questions.md`](questions.md).
- `~/.dotnet` reports SDK 10.0.301 (re-run 2026-10-22); no `global.json` pins it. App TFMs last
  exercised 2026-08-07: both building, installs on the Android emulator and the founder's iPhone 17 Air.

## What the product is right now

Serverless, on-device, API-sync-only dive log; 2-tab bar (Log · You); six vendor read connectors under
`Connectors/Cloud/`. **Live allowlist = `ProviderCatalog.LiveConnectorIds` = `garmin_connect`,
`scubapro`, `suunto_cloud`** — read off the source 2026-10-22. Dimmed "COMING SOON": Shearwater
(deliberate placeholder, its cloud is desktop/BLE), Oceanic+, Mares, PADI, SSI; onboarding S1 mirrors
both lists as static rows. Free tier keeps the last 5 dives (`FreeTier`). How far each live connector
is verified (Garmin and ScubaPro full, Suunto summary-only) is in
[`gotchas-connectors.md`](gotchas-connectors.md).

Provider priority with overlap merge, unlink hard-delete plus a launch orphan sweep, and the grouped
Providers screen with native drag-reorder are all in (`decisions-sync.md`); the auto-sync flash is a
cosmetic open item whose fix was reverted.

## Never observed on the founder's iPhone

Emulator-verified or code-complete only. Two need real data from two overlapping providers, which an
emulator cannot seed: **multi-provider merge** (one row, both badges in priority order, "SOURCES · TAP
TO COMPARE" swapping dive-detail in place) and **flash-free sync**. Three shipped feel behaviours have
never been watched by anyone: reorder feel on device, the embedded iOS map gesture, and the logbook tap
haptic. iOS section paging is an open question, not a verification item — `questions.md`.
