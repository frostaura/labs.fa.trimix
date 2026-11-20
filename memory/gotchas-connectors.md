---
name: labs-fa-trimix-gotchas-connectors
description: "Only Garmin and ScubaPro are verified end-to-end; Suunto degrades to summary-only, the rest are fixture-only guesses. A dive's activityId is learnable only from a real dive. Push/Dl7 is PADI's payload codec, not an export feature."
type: gotcha
last_verified: 2026-11-20
---

# Gotchas — connector verification

- **Garmin + ScubaPro verified end-to-end; Suunto supported at summary fidelity; the rest fixture-only.** Garmin (`docs/reverse-engineering/garmin-dive-api.md`) and ScubaPro/LogTRAK (`docs/reverse-engineering/scubapro-logtrak-api.md`, Firebase) ran end-to-end against the founder's own account (full profiles). **Suunto** has a live-VERIFIED sign-in + dive-list corridor (Sports Tracker `api.sports-tracker.com` + `STTAuthorization`; `docs/reverse-engineering/suunto-sports-tracker-api.md`) and is **supported**, but its depth/temp profile leg + diving activityId are UNVERIFIED (no real dive available) so `FetchProfileAsync` degrades to a **summary-only** dive — an account with no real dives lists nothing. Still fixture-only / researched-guess and Coming soon: Oceanic+, **Mares** (its MySSI-corridor assumption is now suspect — ScubaPro's identical assumption proved wrong), PADI/SSI push GraphQL. Do not generalise "full-fidelity" beyond Garmin + ScubaPro.
- **Provider availability is an allowlist, not a per-connector flag.** The trap is confusing `ProviderCatalog.LiveConnectorIds` — the ONLY thing that makes a provider connectable — with the descriptor's `EnabledByDefault`, which is a legal-dark flag and is always false for `ToSRiskReverseEng`. Reading the second one tells you nothing about what a user can connect. The rule and how to promote a connector are in [`decisions.md`](decisions.md); **which ids are in the set right now is state and is written once, in [`state.md`](state.md)** — read it there rather than trusting a list copied into a gotcha.
- **A dive connector cannot be finished from a test account.** (The verify-by-capture rule is `../CLAUDE.md`'s; the corridors it overturned are in [`decisions-connectors.md`](decisions-connectors.md).) A dive connector's diving `activityId` and per-dive sample endpoint can ONLY be learned from a real dive on the wire — an account with only manual test workouts cannot finish one.
- **Dive-row push destinations reflect *currently linked* push providers**, not a per-dive push ledger (same approximation the prototype makes).
- **`Connectors/Push/Dl7/` is not a surviving file-export feature, and it is not dead code.** `Dl7DiveExporter` is the payload codec `PadiPushDestinationConnector` renders its push body with (its only consumers are that connector, DI and its test — re-counted 2026-11-20); no UI reaches it. Do not read it as licence to re-expose export (a founder-removed non-goal) and do not delete it as cleanup — the PADI push corridor depends on it. The FIT parser under `Connectors/Fit/` is the same kind of internal codec.

_Durability and capture hygiene: [`gotchas.md`](gotchas.md). Firebase/Firestore corridor traps: [`gotchas-firebase.md`](gotchas-firebase.md)._
