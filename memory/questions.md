---
name: fa-trimix-questions
description: "Can iOS page dive-detail with chips gone; M3 needs a beacon a serverless app lacks and seeds via a killed OceanCare; is the bet worth field-testing; name clearance unrun; CAC unmeasured; warnings-as-errors exemption."
type: question
last_verified: 2026-11-20
---

# Open questions

The first six are **founder-owned** and none can be closed by writing more code; the last has no named owner.

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
- **Gate 1 names a seeding channel that no longer exists.** The research question seeds the free demo
  "through `Underwater/`/OceanCare and r/scuba"; OceanCare Identifier was killed 2026-11-20 and
  `Underwater/` now has no programs, so r/scuba is the only named channel and the graduation pathway's
  partner subsidiary has nothing to share. Restate the channel, or keep it and say who seeds. The
  `CLAUDE.md` sentence is left as written until this is answered.
- **Name clearance has never been run** and transitively gates the store listing, the public domain and
  going public with the repo. Store + `.com` + USPTO (Class 9/42); no candidate has been cleared.
- **`[CAC_PER_PAYER = TBD]` is still a placeholder** — no CPI test has run, so the payer economics in
  the M4 gate rest on an unmeasured number.
- **Is `Trimix.App`'s exemption from `TreatWarningsAsErrors` a recorded exception or unwaived debt?**
  `Directory.Build.props` turns it off for the app project ("MAUI/XAML tooling emits warnings outside
  our control"), so the app build can accumulate warnings that the Core suite and the other projects
  would fail on. Nothing in the context layer records the exemption as accepted, and it sits against the
  founder's standing zero-warnings preference. Owner: unassigned.
