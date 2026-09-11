---
name: fa-trimix-worklog
description: "Session pointers only, newest first. Only the 2026-08-21 durability push and the 2026-07-30 Garmin capture still change how this program is read; the July rebuild entries survive as origin, not as description."
type: log
last_verified: 2026-09-11
---

# Worklog — session index

Pointers, never a changelog: what happened, and where the rationale went. Rules are in the decisions
and gotchas topics, current status in [`state.md`](state.md). **Delete an entry the moment its
pointers say nothing the topics do not already say** — if a line here starts explaining itself, the
explanation belongs in the topic it points at and the line should go.

- **2026-08-21 — durability pass.** The harness and all three reverse-engineering contracts were
  tracked for the first time and pushed with the 2026-08-07 body, closing the program's worst
  hazard. → [`state.md`](state.md)
- **2026-08-07 — the heaviest day: sync behaviour, two connectors verified live, and the feel pass.**
  → [`decisions-sync.md`](decisions-sync.md), [`decisions-connectors.md`](decisions-connectors.md),
  [`decisions-haptics.md`](decisions-haptics.md), [`gotchas-ui.md`](gotchas-ui.md)
- **2026-07-30 — the first live vendor capture (Garmin)**, and the legal-posture change to the
  provider sign-in page. → [`decisions-connectors.md`](decisions-connectors.md),
  [`decisions-ui.md`](decisions-ui.md)
- **2026-07-27 / 07-29 — the four-phase serverless rebuild, then the auth and Sites removals that
  re-shaped it two days later.** Kept as the origin of the product's invariants only; it is not a
  description of the current shape. → [`decisions.md`](decisions.md), [`decisions-ui.md`](decisions-ui.md)
