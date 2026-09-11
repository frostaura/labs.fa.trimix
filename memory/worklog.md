---
name: fa-trimix-worklog
description: "Session index only, newest first: the 2026-08-21 durability push, the 2026-08-07 provider-priority + ScubaPro/Suunto/haptics day, the Garmin capture, the auth and Sites removals, the four-phase serverless rebuild."
type: log
last_verified: 2026-09-11
---

# Worklog — session index

Newest first, and deliberately thin: this records *what happened and where the rationale went*, never
the blow-by-blow. Rules live in the decisions and gotchas topics; current status in
[`state.md`](state.md). If an entry here starts explaining something, the explanation belongs in the
topic it points at.

- **2026-08-21 — durability pass.** The harness and all three reverse-engineering contracts were added
  to git for the first time, the 2026-08-07 working body was committed with them, and the lot was
  pushed. This closed the program's worst hazard. → [`state.md`](state.md)
- **2026-08-07 — four sessions in one day.** Provider priority, merge grouping, unlink plus the launch
  orphan sweep and the reverted flash fix; auto-sync on every provider link; ScubaPro reverse-engineered
  onto Firebase and verified live; Suunto reverse-engineered onto Sports Tracker and promoted to
  supported; and the haptics / Material-inputs / perceived-performance pass.
  → [`decisions-sync.md`](decisions-sync.md), [`decisions-connectors.md`](decisions-connectors.md),
  [`decisions-haptics.md`](decisions-haptics.md), [`gotchas-ui.md`](gotchas-ui.md)
- **2026-07-30 — the first live vendor capture (Garmin)**, the dive-detail and chart overhaul, and the
  legal-posture change to the provider sign-in page.
  → [`decisions-connectors.md`](decisions-connectors.md), [`decisions-ui.md`](decisions-ui.md)
- **2026-07-29 — three sessions.** You-tab consolidation, the Sites-capability removal, the full auth
  removal with Settings replay/reset, and onboarding plus bottom-sheet polish.
  → [`decisions.md`](decisions.md), [`decisions-ui.md`](decisions-ui.md)
- **2026-07-27 — the four-phase serverless rebuild.** Backend, BLE, file import/export, manual entry
  and HealthKit removed; the cloud data layer and `SyncCoordinator` added; the 11 prototype screens
  rebuilt — two of those screens (biometric unlock, the Sites tab) were removed two days later, so do
  not read that phase as the current shape.
  → [`decisions.md`](decisions.md), [`decisions-connectors.md`](decisions-connectors.md)
