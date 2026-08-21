---
name: fa-trimix-worklog
description: "Session index 2026-07-27 → 2026-08-07, pruned to pointers: the four-phase serverless rebuild, Sites + auth removal, the Garmin capture, the UI/feel pass, ScubaPro and Suunto reverse-engineering, provider priority — rules live in the decisions and gotchas topics"
type: log
last_verified: 2026-08-21
---

# Worklog — session index

Newest first, and deliberately thin: this records *what happened and where the detail went*, never the blow-by-blow. Every
rule a session produced lives in the decisions and gotchas topics; current status lives in [`state.md`](state.md). Pruned to
pointers on 2026-08-21, after it had grown into a changelog.

## 2026-08-07 — four sessions in one day, all still UNCOMMITTED

- **Provider priority · merge grouping · unlink and orphan cleanup · the reverted flash fix.** Large feature session; reorder
  UX went through three iterations before the native `CollectionView` drag. 405/405 tests at that point, both TFMs green,
  deployed to emulator + iPhone. → [`decisions-sync.md`](decisions-sync.md), [`gotchas-ui.md`](gotchas-ui.md).
- **Auto-sync on every provider link.** A founder-reported bug (linking ScubaPro second synced nothing) removed a deliberate
  first-link-only gate and inverted the test pinning it. 392 tests. → [`decisions-sync.md`](decisions-sync.md).
- **ScubaPro reverse-engineered and implemented end-to-end.** mitmproxy against LogTRAK 2 proved it is a Firebase app, not
  MySSI; the contract was banked, `FirebaseCloudClient` built as a reusable seam, the connector rewritten onto it and verified
  live against the founder's account (22 dives, full 311-sample profile). 392 tests. →
  [`decisions-connectors.md`](decisions-connectors.md), [`gotchas.md`](gotchas.md).
- **Suunto reverse-engineered** — real backend is Sports Tracker, not `cloudapi.suunto.com`; connector rewritten to the
  verified login+list corridor and promoted to supported at summary fidelity. 408 tests. →
  [`decisions-connectors.md`](decisions-connectors.md).
- **Haptics · Material inputs · perceived performance · dive-detail polish.** One long founder-driven UI/feel session: haptics
  went from nothing to a six-cue semantic port, Android inputs became genuinely Material, two separate freeze bugs were found
  and fixed, and three independent latency sources were closed — one of them found by instrumenting, which disproved my own
  hypothesis. 390 tests. → [`decisions-haptics.md`](decisions-haptics.md), [`decisions-ui.md`](decisions-ui.md),
  [`gotchas-ui.md`](gotchas-ui.md).

## 2026-07-30 — Garmin API reverse-engineered; dive-detail overhaul — PRUNED

The first live vendor capture (Garmin Dive, founder's own account: dives #66–69, Sodwana, 5,581 samples at 1 Hz), the
accompanying dive-detail/chart overhaul and the legal-posture change to the provider sign-in page. Contract:
`docs/reverse-engineering/garmin-dive-api.md`. Rationale survives in [`decisions-connectors.md`](decisions-connectors.md) and
[`decisions-ui.md`](decisions-ui.md); commit `399110b` and earlier carry the blow-by-blow.

## 2026-07-29 — three sessions — PRUNED

You-tab consolidation, the Sites-capability removal, the full auth removal with Settings replay/reset, the onboarding marquee
fix and the bottom-sheet overlay polish. Rationale survives in [`decisions.md`](decisions.md) and
[`decisions-ui.md`](decisions-ui.md); commits `750ba82`, `8ad5241` and `399110b` carry the detail.

## 2026-07-27 — the four-phase serverless rebuild

1. **Removal, 201 files.** The entire `Trimix.Api` backend, EF/PostgreSQL, docker-compose, `tests/Trimix.Api.Tests` and all
   client→server code; BLE fully excised (vendor protocols, transports, `ImportPage`, permissions); file import/export, manual
   entry and HealthKit removed. `ImportedDive` moved to `Abstractions/`, the FIT decoder to `Connectors/Fit/` — it is a
   dependency of the surviving cloud connectors, a trap caught mid-teardown.
2. **Data layer.** Added Mares and ScubaPro sources plus a shared `MySsiCloudClient`, on the premise that Mares + ScubaPro +
   SSI all route through MySSI — **that premise was later disproved for ScubaPro** (see `decisions-connectors.md`). Added
   `SyncCoordinator` as the "Sync now" entry point, which had been missing since BLE was the only import path.
3. **UX rebuild** of the 11 prototype screens. Two of them are since gone: **biometric unlock** (all auth removed 2026-07-29)
   and the **Sites tab / bundled `SiteAtlas`** (capability removed 2026-07-29). Do not read this phase as current shape.
4. **Docs/context pass.**
