---
name: fa-trimix-questions
description: "Can iOS page the dive-detail sections now the chip strip is gone; M3 telemetry needs a thin beacon; is the thin-TAM bet worth field-testing; name clearance; CAC placeholder unmeasured"
type: question
last_verified: 2026-08-07
---

# Open questions

- **The auto-sync-after-link flash (minor).** On linking a provider, the logbook paints bare for a beat
  before the sync sheet's backdrop slides up. A 2026-08-07 fix attempt broke the add-sheet "Sync now" path
  and was reverted (see `decisions-sync.md`). Any re-attempt must treat from-Providers vs on-Logbook-under-a-modal
  differently and be device-tested (the sheet is transient; `adb` screenshots can't verify it on MAUI's
  opaque canvas). Low priority — cosmetic.
- **Can iOS actually page the dive-detail sections now that the chip strip is removed?** Owner: founder, on
  the physical iPhone — I cannot drive one. In-pane `SwipeGestureRecognizer`s are swallowed by
  `UIScrollView`'s pan on iOS, which is the documented reason a tappable switcher existed at all; removing
  the chips left swipe as the only control. If it cannot page, the fix is a pan-based implementation, **not**
  restoring the chips (the founder removed those deliberately). See `decisions-ui.md`.
- **M3/M4 telemetry.** A serverless app cannot aggregate "≥300 activated divers" — the funnel fires locally with no ingest. Running M3 needs *some* thin analytics beacon (the one plausible exception to "no servers"). Founder decision.
- **Whether the heaviest-build, thinnest-TAM bet is worth field-testing at all** — unchanged by the rebuild; the free gate exists to answer it cheaply.
- **Name clearance** remains a launch blocker. (Site-atlas seed licensing is no longer a blocker — the Sites capability was removed 2026-07-29.)
- **`[CAC_PER_PAYER = TBD]`** — still an unmeasured placeholder; no CPI test has run.
