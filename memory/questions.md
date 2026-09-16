---
name: fa-trimix-questions
description: "All founder-owned: can iOS page the dive-detail sections with the chips gone; M3 needs a telemetry beacon a serverless app cannot provide; is the thinnest-TAM bet worth field-testing; name clearance unrun; CAC never measured."
type: question
last_verified: 2026-09-11
---

# Open questions

Every one of these is **founder-owned** — none can be closed by writing more code.

- **Can iOS actually page the dive-detail sections now that the chip strip is removed?** Needs the
  founder on the physical iPhone; an agent cannot drive one. In-pane `SwipeGestureRecognizer`s are
  swallowed by `UIScrollView`'s pan on iOS, which is the documented reason a tappable switcher existed
  at all, and removing the chips left swipe as the only control. If it cannot page, the fix is a
  pan-based implementation — **not** restoring the chips, which the founder removed deliberately.
  Rationale: [`decisions-ui.md`](decisions-ui.md).
- **M3/M4 telemetry has no carrier.** A serverless app cannot aggregate "≥300 activated divers" — the
  funnel fires to a local sink with no ingest anywhere. Running the demand gate needs *some* thin
  beacon, which would be the one plausible exception to the no-servers invariant. Until this is
  decided, M3 cannot be run at all, whatever the build state.
- **Is the heaviest-build, thinnest-TAM bet in the Labs portfolio worth field-testing?** Unchanged by the
  rebuild. The free community-demand gate exists precisely to answer this cheaply.
- **Name clearance has never been run** and transitively gates the store listing, the public domain and
  going public with the repo. Store + `.com` + USPTO (Class 9/42); no candidate has been cleared.
- **`[CAC_PER_PAYER = TBD]` is still a placeholder** — no CPI test has run, so the payer economics in
  the M4 gate rest on an unmeasured number.
