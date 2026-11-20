---
name: fa-trimix-state
description: "Serverless on-device API-sync-only dive log, dormant: no code commit since 2026-08-21. Garmin + ScubaPro verified live, Suunto summary-only. Pushed 2026-11-20, ahead-only by context since. Core suite re-run green 2026-11-20."
type: state
last_verified: 2026-11-20
---

# Current state

## Activity — dormant since August

The last commit touching `src/` or `tests/` is `555ca07`, the 2026-08-21 durability push; it carried
the 2026-08-07 feature body (dated by `decisions-sync.md`, not provable from git). Every commit since is
context only. **No milestone has moved**; M3 waits on the telemetry question in `questions.md`.

## Durability — probed by hand 2026-11-20 (network probes run)

- **Ahead-only, context commits only.** Re-probed late 2026-11-20 after `git fetch`: the context
  commits up to the 2026-11-20 sweep (`9a7bdd8`) were pushed that day, not by an agent; later context
  commits sit on top, nothing behind (`git rev-list --left-right --count @{u}...HEAD`). Clean including untracked, no lock debris.
  Re-probe before acting.
- **The remote holds one branch, `main`, plus the ref of a closed PR.** PR #1 (README only) was opened
  and closed unmerged on 2026-10-27 and its head branch is gone from the remote; `main` never received it
  ([`watch-docs.md`](watch-docs.md)). `gh run list` shows no workflow runs — there is no CI.
- **Nothing in this program lives on one disk.** The harness and all three reverse-engineering contracts
  are tracked and pushed (`git ls-files` confirms, 2026-11-20). Remote is the private `frostaura/fa.trimix`
  over HTTPS (`gh repo view`: `PRIVATE`, 2026-11-20). One clone only, on this iCloud path; there is no
  `~/src/fa.trimix` dev clone (checked 2026-11-20).

## Build & test

- **The Core suite is green, re-run 2026-11-20** (`~/.dotnet/dotnet test
  tests/Trimix.Core.Tests/Trimix.Core.Tests.csproj --no-restore`, zero failures, zero skipped; the tree
  stayed clean afterwards). No CI, so every green is a one-Mac result (`gotchas.md`). No count is recorded
  here on purpose — the counts pinned in the in-repo docs no longer match it ([`watch-docs.md`](watch-docs.md)).
- `~/.dotnet` reports SDK 10.0.301 (re-run 2026-11-20); no `global.json` pins it. App TFMs last
  exercised 2026-08-07 (not re-run since): both building, installs on the Android emulator and the
  founder's iPhone 17 Air.

## What the product is right now

Serverless, on-device, API-sync-only dive log; 2-tab bar (Log · You); six vendor read connectors under
`Connectors/Cloud/`. **Live allowlist = `ProviderCatalog.LiveConnectorIds` = `garmin_connect`,
`scubapro`, `suunto_cloud`** — read off the source 2026-11-20. Dimmed "COMING SOON": Shearwater
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
