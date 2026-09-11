---
name: fa-trimix-gotchas-connectors
description: "Only Garmin and ScubaPro are verified end-to-end; Suunto degrades to a summary-only dive and the rest are fixture-only guesses. A vendor cloud is a captured contract — every shared-corridor assumption made here has been wrong."
type: gotcha
last_verified: 2026-09-11
---

# Gotchas — connector verification

- **Garmin + ScubaPro verified end-to-end; Suunto supported at summary fidelity; the rest fixture-only.** Garmin (`docs/reverse-engineering/garmin-dive-api.md`) and ScubaPro/LogTRAK (`docs/reverse-engineering/scubapro-logtrak-api.md`, Firebase) ran end-to-end against the founder's own account (full profiles). **Suunto** has a live-VERIFIED sign-in + dive-list corridor (Sports Tracker `api.sports-tracker.com` + `STTAuthorization`; `docs/reverse-engineering/suunto-sports-tracker-api.md`) and is **supported**, but its depth/temp profile leg + diving activityId are UNVERIFIED (no real dive available) so `FetchProfileAsync` degrades to a **summary-only** dive — an account with no real dives lists nothing. Still fixture-only / researched-guess and Coming soon: Oceanic+, **Mares** (its MySSI-corridor assumption is now suspect — ScubaPro's identical assumption proved wrong), PADI/SSI push GraphQL. Do not generalise "full-fidelity" beyond Garmin + ScubaPro.
- **Provider availability is an allowlist, not a per-connector flag.** `ProviderCatalog.LiveConnectorIds` (currently `garmin_connect`, `scubapro`, `suunto_cloud`) is the ONLY thing that makes a provider connectable; everything else renders a disabled "COMING SOON" pill and `ConnectAsync` no-ops. Promote a connector by adding its id there once verified live — do not confuse this with the descriptor's `EnabledByDefault` (which is a legal-dark flag, always false for ToSRiskReverseEng).
- **A vendor cloud is a captured contract, not a guess — verify by capture, never assume a shared corridor.** Suunto's placeholder was wrong on host (`cloudapi.suunto.com` → really `api.sports-tracker.com`), auth (Bearer → really `STTAuthorization` session key) AND download path — exactly like ScubaPro's MySSI assumption was wrong. The diving `activityId` and per-dive sample endpoint can ONLY be learned from a real dive on the wire; an account with only manual test workouts cannot finish a dive connector.
- **Dive-row push destinations reflect *currently linked* push providers**, not a per-dive push ledger (same approximation the prototype makes).

_Durability and capture hygiene: [`gotchas.md`](gotchas.md). Firebase/Firestore corridor traps: [`gotchas-firebase.md`](gotchas-firebase.md)._
