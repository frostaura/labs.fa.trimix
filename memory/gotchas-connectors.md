---
name: fa-trimix-gotchas-connectors
description: "Only Garmin and ScubaPro are verified end-to-end; Suunto degrades to a summary-only dive and the rest are fixture-only guesses. A vendor cloud is a captured contract — every shared-corridor assumption made here has been wrong."
type: gotcha
last_verified: 2026-10-14
---

# Gotchas — connector verification

- **Garmin + ScubaPro verified end-to-end; Suunto supported at summary fidelity; the rest fixture-only.** Garmin (`docs/reverse-engineering/garmin-dive-api.md`) and ScubaPro/LogTRAK (`docs/reverse-engineering/scubapro-logtrak-api.md`, Firebase) ran end-to-end against the founder's own account (full profiles). **Suunto** has a live-VERIFIED sign-in + dive-list corridor (Sports Tracker `api.sports-tracker.com` + `STTAuthorization`; `docs/reverse-engineering/suunto-sports-tracker-api.md`) and is **supported**, but its depth/temp profile leg + diving activityId are UNVERIFIED (no real dive available) so `FetchProfileAsync` degrades to a **summary-only** dive — an account with no real dives lists nothing. Still fixture-only / researched-guess and Coming soon: Oceanic+, **Mares** (its MySSI-corridor assumption is now suspect — ScubaPro's identical assumption proved wrong), PADI/SSI push GraphQL. Do not generalise "full-fidelity" beyond Garmin + ScubaPro.
- **Provider availability is an allowlist, not a per-connector flag.** The trap is confusing `ProviderCatalog.LiveConnectorIds` — the ONLY thing that makes a provider connectable — with the descriptor's `EnabledByDefault`, which is a legal-dark flag and is always false for `ToSRiskReverseEng`. Reading the second one tells you nothing about what a user can connect. The rule and how to promote a connector are in [`decisions.md`](decisions.md); **which ids are in the set right now is state and is written once, in [`state.md`](state.md)** — read it there rather than trusting a list copied into a gotcha.
- **A vendor cloud is a captured contract, not a guess — verify by capture, never assume a shared corridor.** The Suunto and ScubaPro placeholders were each wrong on host and auth when captured ([`decisions-connectors.md`](decisions-connectors.md)). The trap that remains: a dive connector's diving `activityId` and per-dive sample endpoint can ONLY be learned from a real dive on the wire — an account with only manual test workouts cannot finish one.
- **Dive-row push destinations reflect *currently linked* push providers**, not a per-dive push ledger (same approximation the prototype makes).

_Durability and capture hygiene: [`gotchas.md`](gotchas.md). Firebase/Firestore corridor traps: [`gotchas-firebase.md`](gotchas-firebase.md)._
