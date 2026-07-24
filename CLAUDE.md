# fa.trimix — Trimix (working name)

## Research question
Gaia bet APP-03, three gates in order. **(1)** Does a FREE community demo, seeded through `Underwater/`/OceanCare and r/scuba channels, produce ≥300 organic activated divers (≥1 dive logged) with ≥40% BLE-import success among computer owners — **before any paid spend**? **(2)** Among activated users shown the purchase moment, does install→payer conversion reach ≥3% at the locked one-off ladder ($14.99 Pro / $29.99 Lifetime, blended net per payer ≥$13)? **(3)** Can ≥2 of the vendor slate (Shearwater, Suunto, ScubaPro, Aqualung, Mares) be integrated **legally** and imported reliably over BLE in real post-dive field conditions? Garmin is excluded — it blocks third-party BLE dive download industry-wide.

## What this program is
A scuba dive log that fills itself. Originally BLE-only import, it was reframed by founder direction into a **multi-source import aggregator**: it pulls dives from wherever they already live (Apple HealthKit, vendor clouds, BLE-direct, standards files such as FIT/UDDF/DL7), normalizes them, shows Oceanic+-grade stats, and pushes back out to certification logbooks (PADI, SSI) and export formats — alongside a community-moderated shared dive-site database that enriches every dive. Core loop: dive → sync → enrich → reflect/share → return richer. Monetization is a single designed purchase moment at the unlimited multi-source import and cross-device cloud-sync cap. This is explicitly **the heaviest-build, thinnest-TAM bet in the Gaia slate** — the free community-demand gate exists to kill it cheaply.

## Stack & repo
This directory is the repo (`github.com/frostaura/fa.trimix` when published — **blocked on name clearance**). Solution `fa.trimix.slnx` at root; MAUI client under `src/app`, hexagonal backend under `src/backend`, shared contracts under `src/shared`.

- **Client: .NET MAUI.** A documented, root-reviewed stack override — the React mandate cannot ship a native, store-distributed BLE app, and MAUI supersedes the PRD's Swift/Kotlin dual-native exception. C# end to end, one codebase for both stores, BLE via platform bindings behind a vendor-protocol abstraction. **Android is the default target; `net10.0-ios` is opt-in behind `-p:EnableIos=true`.**
- **Backend: mandated stack** — .NET 10, EF Core, Npgsql, PostgreSQL 17. Accounts, server-authoritative entitlements and receipt validation, the shared site DB and moderation queue, dive sync, analytics ingest. Docker Compose for local.
- **Rails-first (binding):** the analytics event taxonomy and the IAP/entitlement pipeline are built and test-verified before any feature work beyond the auth shell. Entitlements are server-authoritative, account-bound, and survive reinstall.
- **Quality bar: Silver (adapted for mobile).** Store deployment is out of scope until Stage-7 compliance and name clearance.

## Owner & key people
Dean Martin (founder).

## Milestones
1. **M1 Rails** — frozen funnel taxonomy on client and server; purchase → receipt-validation → entitlement → restore verified against the sandbox store, including the reinstall `install → entitlement_restored` path.
2. **M2 Vertical slice** — every MVP flow on device: manual entry, BLE import behind the vendor abstraction, site-DB read/auto-match/moderated contribution, profile graph, logbook and stats, paywall at the import cap, restore on reinstall — with the funnel spine reconciling end to end.
3. **M3 FREE community-demand gate** — ≥300 organic activated divers **and** ≥40% BLE import success within the window. **No paid UA before this passes.**
4. **M4 Purchase-intent gate** — ≥3% install→payer at the locked ladder, blended net per payer ≥$13, with ≥1 real sandbox-validated purchase reconciled through the funnel.

M3 and M4 are field and market work, not build tasks. Neither can be closed by writing more code.

## Kill criteria
- BLE import reliable for fewer than 2 of the vendor slate in field conditions.
- The FREE community-demand gate missed by more than 40%.
- No candidate name clears store + `.com` + USPTO (Class 9/42) checks at acceptable cost.
- The site-DB seed dataset cannot be legally licensed at viable cost.

## Graduation pathway
`Technologies/` as a one-off cash-engine app once M3 and M4 both pass — the standard Labs → Technologies graduation pattern. `Ventures/` only on standalone-brand traction. **`Underwater/` is the partner subsidiary** for community seeding via dive ops and for site-DB moderation, with OceanCare adjacency for later marine-life taxonomy. **Capability-share, do not merge scope.**

## Conventions & invariants
- **The funnel event taxonomy is frozen.** 19 events, additive-only — new events may be added, existing ones are never renamed. Code and docs must not drift from `docs/prd.md`.
- **We log dives; we do not compute deco.** Liability and scope non-goal, permanently.
- **Seven non-goals:** no auto-renew ever, no loot-box or gacha, no ads, no deco engine, no social feed, no IR-only computers, no web app.
- **SKUs are a locked ladder:** `trimix.pro` $14.99 and `trimix.lifetime` $29.99 — exactly two, both true non-consumables. Free tier is capped import (last-N dives), read-only site DB, capped logbook.
- **Auth is passkey-only** (WebAuthn/FIDO2, Fido2NetLib backend relying party, iOS AuthenticationServices + Android Credential Manager). No email/password, no OAuth. Guest remains the no-account path.
- **Apple receipt handling is dictated by the client.** StoreKit **1** posts the base64 bundle receipt, so the server uses classic `verifyReceipt` with a prod→sandbox (21007) fallback — *not* the App Store Server API, which needs a `transactionId` that StoreKit 1 never sends. Migrating to StoreKit 2 forces a switch to ES256 JWT + JWS. Know this before touching billing.
- **GDPR delete erases analytics by device pseudo-id(s), not only `UserId`**, so pre-auth and guest funnel rows are erased and the returned count is truthful. Known asymmetry: export omits connector state while erasure wipes it.
- **Reverse-engineered connector guardrails (do NOT relax, independent of the legal override):** (i) never persist a vendor password in plaintext — ephemeral exchange → token in Keychain/Keystore, server-brokered secrets where possible; (ii) per-user opt-in behind a plain-language ToS-risk disclosure; (iii) truthful labelling — "user-assisted personal export", **never** "official <Vendor> integration". All such connectors ship dark (`EnabledByDefault=false`) with `LegalPosture` on the descriptor making the gating structural, and are never exercised against real vendor production servers.
- **Design:** [`docs/design/mockup-spec.md`](docs/design/mockup-spec.md) is the authoritative contract and **supersedes** `sonar-design-language.md` where they conflict. Palette is mint `#2FE3C4` on `#0A1013`; Space Grotesk 700 headings, JetBrains Mono numerals/eyebrows/badges, Hanken Grotesk body; forced dark; 3-tab bar (Log · Sites · You). `assets-notes.md` records nine numbered judgement calls, including darkening SonarDeep to `#1E7A91` because the doc's own value measured 2.8:1 on white and failed the doc's own AA mandate.

## Build / test / run
`docker compose up -d postgres` for the database — note the service is named **`postgres`**, not `db`. Backend migrates from zero against real PostgreSQL 17; health at `/api/v1/healthz`. Android is the default build; add `-p:EnableIos=true` for the iOS TFM. The toolchain that actually worked is **OpenJDK 17** plus Android SDK API 36 in user space (some docs say `openjdk@21` — they are wrong).

Real BLE, StoreKit and passkeys are all **runtime-gated** — they need a physical device, a real store account, and a real associated relying-party domain with entitlements. `docs/known-gaps.md` is the catalogue.

## Blocking gates that are not code
**Name clearance** transitively gates the store listing, the API domain `api.trimix.frostaura.net`, the passkey relying party, and therefore all real billing. **Site-DB seed licensing** — the 64 seeded sites are honest approximations; a legally licensed dataset is a launch blocker. Also required: Apple paid provisioning, Digital Asset Links for passkeys, and a physical iOS device plus Apple Watch Ultra / Oceanic+ for HealthKit validation.

## Where docs live
[`docs/prd.md`](docs/prd.md) is the binding spec (one-pager → PRD → monetization contract, plus amendment sets). [`docs/design/aggregator-architecture.md`](docs/design/aggregator-architecture.md) is authoritative on the import hub. [`docs/known-gaps.md`](docs/known-gaps.md) catalogues runtime-gated edges and accepted risks. [`docs/production-readiness.md`](docs/production-readiness.md) lists deploy inputs. **`docs/architecture/overview.md` states its own rule — when it and the code disagree, stop and resolve the drift. Treat it as suspect until checked: `MEMORY.md` tracks the known drift.**

## Never do this here
- Never rename a funnel event.
- Never ship a deco calculation.
- Never label a reverse-engineered connector as an official vendor integration, or relax any of the three guardrails.
- Never enable a gated connector by default.
- Never treat a fixture-validated BLE parser as hardware-validated.
- Never claim a gate passed on the strength of a local build — there is no CI here to catch you.

## Upkeep
This `CLAUDE.md`, [`MEMORY.md`](MEMORY.md) and any local `.claude/skills/` here are kept current as changes land. `MEMORY.md` is updated at the end of any session that shipped, decided, discovered or abandoned something, and the program's row in [`../MEMORY.md`](../MEMORY.md) is updated in the same pass. A skill that no longer matches how work is actually done is fixed or deleted, not left to rot. The standard: [`/docs/operating/context-cascade.md`](../../../docs/operating/context-cascade.md).
