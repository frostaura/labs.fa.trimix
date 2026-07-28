# Trimix — Production Readiness Ledger

**Status:** Re-grounded 2026-07-27 for the serverless / API-sync-only rebuild (PRD Amendments set 3) · **Owner:** Production architect (fa.trimix)

Companion to [`docs/architecture/overview.md`](architecture/overview.md) and [`docs/known-gaps.md`](known-gaps.md). This file answers one question per surface: **what is REAL code that runs today**, versus **what is RUNTIME-GATED** on a precondition external to this repo. The earlier version of this ledger covered a client–server app (server receipt validation, passkey relying party, BLE deploy inputs, API host provisioning); all of that is deleted — **there is nothing to provision server-side, ever**. That removes whole categories of deploy input, and it removes the server-side safety nets with them: what remains gated is gated on *stores, hardware, and vendors*, not on ops.

**Evidence baseline (local-only — there is no CI):** solution builds 0 errors (net10.0-android; `net10.0-ios` opt-in and kept compiling); **345/345 `Trimix.Core.Tests` pass**. Nothing is store-shipped. Connectors are demo-grade.

---

## Legend

- **REAL** — code exists, compiles, is unit-tested, and executes its real behaviour at runtime with no further input.
- **REAL (fixture-proven only)** — the code path is real and runs, but its correctness against the outside world is proven only against recorded/derived fixtures, never a live counterparty.
- **RUNTIME-GATED** — compiled and correct as far as tests can show, but cannot execute its real effect until a platform/account/hardware/vendor precondition external to this repo exists.

---

## Surface 1 — Provider sync (the only import path)

| Item | State | External precondition |
|---|---|---|
| Connector ports, descriptors, `LegalPosture` gating, registry | REAL | — |
| Consent ledger + token store (consent-before-I/O, token-only persistence, purge on identity reset) | REAL (test-pinned) | — |
| `SyncCoordinator` (consent+auth filtering, per-connector cursor, cap-safe re-fetch, push fan-out) | REAL | — |
| The 7 connector clients (Garmin, Suunto, ScubaPro, Mares, Oceanic+, PADI, SSI incl. the shared `MySsiCloudClient`) | REAL (fixture-proven only) | **Real vendor cloud endpoints.** Every URL, auth flow and payload shape is a researched guess validated against recorded fixtures; no request has ever been made to a vendor production server from this environment. Live validation + legal sign-off precede any store listing |
| FIT decode (Garmin/Suunto payloads), DL7 render (PADI push), SSI QR | REAL (fixture-proven only) | Same as above — real payloads may differ from fixtures |

The M3 question — do ≥2 providers import reliably in real post-dive field conditions — **cannot be answered from this repo**. It is field work against live vendor backends the connectors have never touched.

## Surface 2 — Entitlements & billing (on-device)

| Item | State | External precondition |
|---|---|---|
| `EntitlementService` local grant/restore + `EntitlementGate` free-cap (5) enforcement | REAL (test-pinned) | — |
| `SandboxBillingService` end-to-end (purchase → local grant → restore) | REAL — the exercised path today | — |
| Real StoreKit / Play Billing purchase | RUNTIME-GATED | App Store Connect / Play Console accounts + a listing, which is blocked on **name clearance**; plus real devices |
| Entitlement integrity | — | **None exists.** On-device entitlements are client-trust and piratable by design — an accepted serverless tradeoff, not a wiring gap. There is no server to validate a receipt |

## Surface 3 — Biometric unlock (the only auth)

| Item | State | External precondition |
|---|---|---|
| `IBiometricUnlock` flow + local `AuthSession` state | REAL | — |
| Actual biometric verification | RUNTIME-GATED | Real biometric hardware (Face ID / fingerprint). The emulator fallback **succeeds without verifying anything** — simulator runs prove the flow, not the security |

## Surface 4 — Analytics (local sink)

| Item | State | External precondition |
|---|---|---|
| Frozen-taxonomy validation, durable local queue, once-per-install `install`/`first_run_completed` | REAL (test-pinned) | — |
| Aggregation of funnel data | — | **Impossible as built.** Events drain to the debug log on the device; there is no ingest server. M3/M4 gate metrics (activated divers, install→payer) cannot be measured without adding a thin beacon — an open product decision, recorded in `known-gaps.md` |

## Surface 5 — Sites & stats

| Item | State | External precondition |
|---|---|---|
| Bundled `SiteAtlas` (14 seed sites, haversine match), derived Sites tab, "NEAR THIS SITE" carousel | REAL | Seed sites are honest approximations of publicly-known values; a legally-licensed dataset at any larger scale remains a launch-track item |
| Stats, merge policy, chart + scrub, MOD planner (display math only) | REAL (test-pinned) | — |

## Surface 6 — Store shipping

Entirely RUNTIME-GATED and out of scope until: **name clearance** ("Trimix" is flagged high-collision; store + `.com`/`.app` + USPTO Class 9/42 re-check is a blocking gate), Stage-7 compliance, legal sign-off on the reverse-engineered connectors, and real-device validation. **No CI/CD exists** — a store pipeline would have to be built from zero.

---

## Guardrails that must stay test-pinned

- Every `ToSRiskReverseEng` descriptor ships `EnabledByDefault=false`; zero network I/O before consent; only exchanged tokens persist; truthful "personal export" labels.
- Free-tier cap counts raw stored dives (`FreeTier` = 5); merge is display-only.
- Frozen funnel taxonomy: additive-only, zero renames, zero deletions — including every retained-but-no-longer-emitted name.
- No deco/tissue computation anywhere; the MOD planner and imported NDL/ceiling/CNS display are data, not computation.
- Both SKUs one-off non-consumables; no auto-renew, no ads, no loot-box.
