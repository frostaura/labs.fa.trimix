# fa.trimix — Trimix (working name)

## Research question
Gaia bet APP-03 (Stage 5 vertical slice). Three gates, in order: (1) does a FREE community demo seeded through Underwater/OceanCare + r/scuba channels produce ≥300 organic activated divers (≥1 dive logged) with ≥40% BLE-import success among computer owners — before any paid spend; (2) among activated users shown the purchase moment, does install→payer conversion reach ≥3% at the locked one-off ladder ($14.99 Pro / $29.99 Lifetime, blended net/payer ≥$13); (3) can ≥2 of 4 dive-computer vendors (Shearwater, Suunto, Garmin, Mares) be integrated legally and imported reliably over BLE in real post-dive field conditions?

## What this program is
A scuba dive log that fills itself: the app auto-imports dive profiles (depth/time/temp/gas) from the diver's wrist computer over Bluetooth and enriches each dive from a shared, community-moderated dive-site database — so the logbook gets smarter with every diver. Core loop: dive → sync → enrich → reflect/share → return richer. Monetization is a single designed purchase moment (the multi-vendor BLE auto-import cap) unlocking Pro/Lifetime one-off SKUs. This is explicitly the heaviest-build, thinnest-TAM bet in the Gaia slate — the free community-demand gate exists to kill it cheaply.

## Stack & repo
Repo is this directory (local git; remote `github.com/frostaura/fa.trimix` when published — blocked on name clearance). Solution `fa.trimix.slnx` at root; MAUI client under `src/app`, hexagonal backend under `src/backend`, shared contracts under `src/shared`.

- **Client: .NET MAUI (net10.0-android first; net10.0-ios kept compiling, ships second).** Documented stack override, root-reviewed 2026-07-03: the React mandate cannot ship a native, store-distributed BLE app, and MAUI supersedes the PRD's Swift/Kotlin dual-native exception with a founder-directed shared-codebase choice — C# end-to-end, one codebase for both stores, BLE via platform bindings behind a vendor-protocol abstraction (Shearwater first; simulator transport for dev/QA).
- **Backend: mandated stack** — .NET 10 + EF Core + PostgreSQL (PostGIS for geo site search): accounts, server-authoritative entitlements + receipt validation, shared site DB + moderation queue, dive sync, analytics ingest. Docker Compose for local.
- **Rails-first (binding):** analytics event taxonomy + IAP/entitlement pipeline are built and test-verified before any feature work beyond the auth shell. Entitlements are server-authoritative, account-bound, survive reinstall; both SKUs are true non-consumables — **no auto-renew, ever; no ads; no loot-box/random-reward mechanics**.
- **Quality bar: Silver (adapted for mobile)** — lint + build + tests green, Android APK produced, funnel spine reconciled end-to-end. Store deployment is NOT in scope until Stage-7 compliance + name clearance.

## Owner & key people
Dean Martin (founder).

## Current phase
`prototyping` (scaffolded 2026-07-03).

## Milestones
1. **M1 Rails** — frozen funnel taxonomy (client + server) and purchase→receipt-validation→entitlement→restore pipeline verified by tests against a sandbox/mock store.
2. **M2 Vertical slice** — all MVP flows working on Android: manual dive entry, one-vendor BLE import (Shearwater protocol behind the four-vendor abstraction, simulator transport), site DB read + auto-match + moderated contribution, profile graph, logbook + stats, paywall at the import cap, restore-on-reinstall; funnel spine `install → first_run_completed → dive_logged_* → paywall_shown → purchase_completed → entitlement_granted` reconciles end-to-end; APK builds.
3. **M3 FREE community-demand gate** — ≥300 organic activated divers AND ≥40% of computer-owners' BLE import attempts succeed, within the test window. No paid UA before this passes.
4. **M4 Purchase-intent gate** — ≥3% install→payer at the locked ladder, blended net/payer ≥$13, ≥1 real sandbox-validated purchase reconciled through the funnel.

## Kill criteria
- BLE import reliable for fewer than 2 of 4 vendors in field conditions.
- FREE community-demand gate missed by >40%.
- No candidate name clears store + `.com` + USPTO (Class 9/42) checks at acceptable cost.
- Site-DB seed dataset cannot be legally licensed at viable cost.

## Graduation pathway
Technologies/ as a one-off cash-engine app once M3+M4 pass (mirror the fa.foresight / fa.reel Labs→Technologies pattern); Ventures/ only on standalone-brand traction. **Underwater/ is the partner subsidiary** — community seeding via dive ops, OceanCare adjacency for site-DB moderation and (later) marine-life taxonomy. Capability-share, don't merge scope.

## Working notes
- **Name: Trimix — founder-picked 2026-07-03** from the PRD candidate list. The PRD itself flags Trimix as likely-high collision risk in the dive space; the full store + `.com` + USPTO re-check remains a **blocking gate before any public listing, domain purchase, or trademark filing**. Predecessors: DeepLog (unavailable — live iOS competitor since 2026-04), Wreckline (prior working name; survives only in the design-project filename).
- **SKUs (locked ladder):** `trimix.pro` $14.99, `trimix.lifetime` $29.99 — exactly two, both non-consumable. Free tier: capped import (last-N dives), read-only site DB, capped logbook.
- **Design: "Sonar (A)"** direction from the claude.ai design project (`Wreckline Storyboard.dc.html`, project `fb253415-935b-43f4-af5e-9f8f11d849a4`). Direct import was blocked at scaffold time (no `/design-login`, Chrome extension disconnected) — the design language in `docs/design/` is a faithful re-derivation under founder-granted creative liberty. Re-sync via DesignSync once authorized and reconcile.
- **Android first** (founder direction 2026-07-03; host macOS is a beta OS with Xcode-toolchain risk). iOS target stays in the TFM list and must keep compiling, but nothing iOS-specific gates a milestone.
- Full spec stack (one-pager → PRD → monetization contract) lives in [`docs/prd.md`](docs/prd.md); the funnel event names there are **frozen** — code and docs must not drift.
- We log dives; we do **not** compute deco (liability + scope non-goal).
