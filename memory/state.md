---
name: labs-fa-trimix-state
description: "Serverless on-device API-sync-only dive log, dormant: no code change since 2026-08-21. Garmin + ScubaPro verified live, Suunto summary-only. Repo PUBLIC since 2026-11-20, rename shipped, two clones level. No CI."
type: state
last_verified: 2026-11-20
---

# Current state

## Activity — dormant since August

The last commit changing code is `555ca07`, the 2026-08-21 durability push; it carried the 2026-08-07
feature body (dated by `decisions-sync.md`, not provable from git). Since then: context commits, and
on 2026-11-20 the rename chores (`63ef006` touched only a csproj comment under `src/`). **No milestone
has moved**; M3 waits on the telemetry question in `questions.md`.

## Name and exposure — 2026-11-20 (founder)

`fa.trimix` → **`labs.fa.trimix`**: this directory, the repo `github.com/frostaura/labs.fa.trimix`
(old URL redirects) and, in the product, README, docs owner tags, build comments and the solution
`labs.fa.trimix.slnx` (`63ef006`, `7ef86ef`; `git grep` finds no other old name, late 2026-11-20).
Unchanged by design: bundle id `net.frostaura.trimix`, the `trimix.*` SKUs, `Trimix.*` namespaces, and
the harness `UserSecretsId` (`gotchas-build.md`). **The repo is PUBLIC** (`gh repo list`, late
2026-11-20) — founder decision, taken with the name uncleared; what that publishes is in
[`questions.md`](questions.md).

## Durability — probed by hand late 2026-11-20 (network probes run)

- **Two clones, nothing unpushed in either** (`git fetch`, `git ls-remote`,
  `git rev-list --left-right --count @{u}...HEAD`, `git status --porcelain`): this iCloud directory,
  level with `origin/main`, and `~/src/labs.fa.trimix`, the dev clone created 2026-11-20, which trails
  only by context commits pushed from here — `git pull` there before building. Clean, no stash.
- **The remote holds one branch, `main`.** PR #1 (README only) was opened and closed unmerged on
  2026-10-27 ([`watch-docs.md`](watch-docs.md)). The repo has no workflows (`gh workflow list --all`
  empty) — there is no CI.
- **Nothing in this program lives on one disk.** The harness and all three reverse-engineering contracts
  are tracked and pushed (`git ls-files`, 2026-11-20).

## Build & test

- **The Core suite is green, re-run 2026-11-20** in the iCloud clone, before the solution rename
  (`~/.dotnet/dotnet test tests/Trimix.Core.Tests/Trimix.Core.Tests.csproj --no-restore`, zero failures,
  zero skipped). No CI, so every green is a one-Mac result (`gotchas.md`). No count is recorded here on
  purpose — the counts pinned in the in-repo docs no longer match it ([`watch-docs.md`](watch-docs.md)).
- `~/.dotnet` reports SDK 10.0.301 (2026-11-20); no `global.json` pins it. App TFMs last exercised
  2026-08-07 (not re-run since): both building, installs on the Android emulator and the founder's
  iPhone 17 Air.

## What the product is right now

Serverless, on-device, API-sync-only dive log; 2-tab bar (Log · You); six vendor read connectors under
`Connectors/Cloud/`. **Live allowlist = `ProviderCatalog.LiveConnectorIds` = `garmin_connect`,
`scubapro`, `suunto_cloud`** — read off the source 2026-11-20. Dimmed "COMING SOON": Shearwater
(deliberate placeholder), Oceanic+, Mares, PADI, SSI; onboarding S1 mirrors both lists as static rows.
Free tier keeps the last 5 dives (`FreeTier`). Verification depth per connector:
[`gotchas-connectors.md`](gotchas-connectors.md). Provider priority with overlap merge, unlink
hard-delete plus a launch orphan sweep, and the grouped Providers screen with native drag-reorder are
all in (`decisions-sync.md`); the auto-sync flash is a cosmetic open item whose fix was reverted.

## Never observed on the founder's iPhone

Emulator-verified or code-complete only. Two need real data from two overlapping providers, which an
emulator cannot seed: **multi-provider merge** and **flash-free sync**. Three shipped feel behaviours
have never been watched by anyone: reorder feel on device, the embedded iOS map gesture, and the
logbook tap haptic. iOS section paging is an open question — `questions.md`.
