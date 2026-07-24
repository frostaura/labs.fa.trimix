# [APP-03] Trimix — Vertical-Slice Spec Stack (Gaia Stage 5)

> **Naming status.** Original codename **DeepLog is NOT available** (exact-name iOS competitor live in the scuba-logging category since April 2026). Prior working name: Wreckline. **Founder selection 2026-07-03: Trimix** — diver-native gas-blend term, instantly credible to the target user. ⚠ The original spec flags Trimix as *likely high collision risk in the dive space*: a full App Store + Play Store + `.com`/`.app` + USPTO (Class 9 software / Class 42 SaaS) re-check is a **blocking gate before any public listing, domain purchase, or trademark filing**. Until cleared, every artifact says **Trimix (working name)**.

## Amendments (2026-07-03, founder-directed — supersede conflicting lines below)
1. **Name:** Trimix (working name). All SKU ids, package ids, and artifacts use `trimix.*`.
2. **Client stack:** .NET MAUI single shared codebase (net10.0-android + net10.0-ios), replacing the dual-native Swift/Kotlin plan. Rationale: maximal code reuse across both app stores, C# end-to-end with the mandated backend stack, BLE via platform bindings behind a vendor-protocol abstraction. This is the root-reviewed override recorded in `../CLAUDE.md`.
3. **Platform order:** **Android first**, then iOS (host build machine runs a beta macOS with Xcode-toolchain risk). The iOS TFM stays in the project and must keep compiling.
4. **Design direction:** "Sonar (A)" from the claude.ai design project (`Wreckline Storyboard.dc.html`). See `design/sonar-design-language.md`.

## Amendments set 2 (2026-07-05, founder-directed — the aggregator pivot; supersede conflicting lines below)

The product's data-acquisition strategy changes from **BLE-only import** to a **multi-source normalization hub**. Core loop reframed: *dive → auto-sync from wherever the dive already is (Apple HealthKit / vendor cloud / BLE / file) → normalize to one canonical dive → beautiful Oceanic+-grade stats → push out to certification logbooks (PADI, SSI) and standards files.* Authoritative architecture: [`design/aggregator-architecture.md`](design/aggregator-architecture.md).

5. **The connector-landscape reality (researched 2026-07-05).** There is **no clean, published cloud API that returns a dive profile** from any major dive-computer vendor. Shearwater / Mares / ScubaPro / Oceanic+ publish no developer API. Garmin and Suunto have official partner APIs that **structurally exclude scuba** (Garmin switched third-party dive access off industry-wide; Suunto's is a fitness API that strips tank/gas/route). The **only** clean first-party cloud surface carrying dive data is **Apple HealthKit** (iOS-only; depth + temperature only; Apple-Watch-Ultra / Oceanic+ dives). Android Health Connect has no dive data type. PADI and SSI expose **no logbook-write API**. Every other "read/write their cloud" path is reverse-engineering an unpublished, credential-gated backend — a Terms-of-Service breach with CFAA-type exposure that fails this project's own **gate-3 ("integrated *legally*")**.

6. **Legally-clean channels (build as real, default features):** (a) **Apple HealthKit** read (iOS); (b) **file import** — Garmin/Suunto FIT, UDDF, DAN DL7, Suunto-app JSON — the diver moves their own data, and the *only* clean Garmin path; (c) **BLE-direct from the diver's own hardware** — the original architecture, **retained**, generalized to one connector kind (reading your own computer touches no vendor cloud); (d) **standards-based file export** — UDDF / DL7 / PDF the diver controls (the honest "push to PADI/DAN").

7. **Founder-directed legal override (2026-07-05).** After the gate-3/legal reality was flagged explicitly, the founder directed that the **reverse-engineered connectors ship as real, enabled features, not dark stubs**: vendor-cloud **pull** (Garmin Connect `garth`-style login, Suunto private cloud, Oceanic+ token cloud) and cert-agency **push** (PADI private sync, SSI QR + private cloud). This **knowingly accepts** the gate-3 "integrated legally" ToS/CFAA risk as a parent-controlled (legal/IP/brand) decision. Non-negotiable engineering guardrails that stand *regardless* of the override, because they protect users rather than the ToS posture: **(i) never persist a user's vendor password in plaintext** — ephemeral credential→token exchange, token in platform secure storage (iOS Keychain / Android Keystore), server-brokered secrets where possible; **(ii) per-user opt-in behind a plain-language ToS-risk disclosure**; **(iii) truthful in-app labeling** — "user-assisted personal-data export", never "official <Vendor> integration". Recorded in `CLAUDE.md` working notes and `known-gaps.md`.

8. **BLE vendor mapping correction.** Garmin **cannot** be a BLE auto-import vendor — Garmin explicitly asked third parties (Subsurface) not to support BLE dive download; Garmin is a **FIT-file / cloud** source only. The BLE-direct vendor slate is **Shearwater (done), Suunto, ScubaPro, Aqualung, Mares** (Mares highest field-reliability risk; ScubaPro cheapest via mature protocol modelling). The gate-3 kill line ("≥2 vendors integrated legally + imported reliably in field conditions") is now satisfiable via *any* clean channel (HealthKit/file/BLE-owned) — but the **field-reliability half is inherited, not eliminated**: the slice still parses a *simulator fixture*, never a real Shearwater BLE stream. Real-hardware BLE validation (M3) remains make-or-break.

9. **Purchase-moment reframe.** The single designed purchase moment moves from *"multi-vendor BLE auto-import cap"* to *"unlimited multi-source auto-import + cross-device cloud sync cap"* (free = capped per-session import + local-only logbook; Pro/Lifetime = unlimited multi-source import + account-bound cross-device sync of the unified logbook). SKUs, prices, and the entire money half of the funnel are **unchanged**.

10. **Taxonomy delta — additive-only, ZERO renames** (this section is the binding spec change; `FunnelEvents.cs` + server `FunnelContract.cs` follow it). Add trigger value `cloud_sync_cap` to `paywall_shown.trigger`; add events `source_connected` (props `source`, `platform`) and `dives_synced` (props `source`, `count`) as the multi-source analogues of `ble_device_connected` / `dives_fetched`; **reuse `dive_logged_import` with `source=import` for every clean import channel** (BLE, HealthKit, file, cloud) — differentiate the origin via the existing free-text `vendor` prop (`apple_health`, `garmin_fit`, `garmin_cloud`, `suunto_ble`, `shearwater`, `oceanic_health`, …). Retain-and-deprecate `ble_*` events and `ble_import_cap` (historical validity; BLE still fires them). Never add a sibling value to the frozen `AllowedValues[source] = {import}`. Updated spine: `install → first_run_completed → dive_logged_* → dives_synced → paywall_shown(cloud_sync_cap) → purchase_completed → entitlement_granted`.

11. **Data-model extension** (Layer-2 "Data model sketch" is superseded here). `ProfileSample` gains nullable rich channels the full-fidelity sources record: `ndl`, `ceiling_m`, `ascent_rate_m_min`, `cns`, `tank_pressure_bar`, `setpoint` (+ populate existing `ppo2`), optional `heart_rate` — all nullable, each source fills what it has, every aggregate null-tolerant. Add a **Cylinder** model (`o2`, `he`, `start_bar`, `end_bar`, `volume_l`, `working_pressure_bar`) enabling SAC/RMV, multi-gas, gas-switch tracking (the denormalized `gas_mix` string stays for display). Structure **Conditions** into typed, aggregatable fields (visibility, current, weather, entry type, water type, exposure suit, weight). The `DiveComputer` entity generalizes to a **Connection/Source** concept (kind + descriptor + auth state). **Deco stays non-computed**: displaying imported NDL/ceiling/CNS is data, not computation — the liability line (we do not compute tissue loading) is unchanged, as are the no-auto-renew / no-ads / no-loot-box non-goals.

---

## LAYER 1 — ONE-PAGER

**Product name:** Trimix (working name)

**One-line pitch:** The scuba dive log that auto-imports from every major dive computer over Bluetooth and is built on a shared, community-moderated dive-site database — so your logbook fills itself and gets smarter with every diver.

**Target user + JTBD:**
- **Who:** Certified recreational-to-technical divers (Open Water through Tec/instructor) who log dives and own at least one dive computer (Shearwater, Suunto, Garmin, or Mares). Secondary: dive pros (instructors, divemasters) who log high volume.
- **Job to be done:** *"When I surface from a dive, I want my dive profile, site details, and conditions captured accurately and effortlessly — without retyping numbers off my wrist computer — so I have a credible, complete, shareable logbook I actually trust for my next trip and my certification record."*

**Core loop (5 beats):**
1. **Dive** — diver completes a dive; their dive computer records the profile.
2. **Sync** — diver opens Trimix near the computer; app discovers it over Bluetooth and pulls new dive(s) (depth/time/temp/gas profile) via the vendor protocol.
3. **Enrich** — app auto-attaches the matching site from the shared dive-site DB (GPS + name match); diver confirms/edits site, conditions, gear, buddies, notes, photos.
4. **Reflect & share** — diver reviews the logged dive (profile graph, stats, site info contributed by others), updates running totals, optionally contributes a site correction back to the shared DB (moderated).
5. **Return** — next trip, the diver searches the shared site DB *before* diving for site intel (depth, hazards, marine life, access) → loop restarts richer.

**The single designed PURCHASE MOMENT:**
The free tier imports dives and reads the shared site DB, but caps **multi-vendor Bluetooth auto-import** and **full logbook depth** (free = limited manual/last-N-dives; site-DB contribution gated to verified accounts). The purchase moment fires at the **point of friction the user most wants gone**: when a diver taps **"Auto-import all dives from my [Shearwater/Suunto/Garmin/Mares] computer"** and hits the free cap — the paywall presents **Pro (one-off $14.99)** and **Lifetime ($29.99 true non-consumable)** as the unlock for unlimited multi-vendor import + unlimited logbook + offline site DB. This is the moment of maximal, concrete WTP.

**Platforms:** Android (primary launch, per Amendment 3) + iOS. One shared .NET MAUI codebase; BLE via platform bindings.

**Scope tier:** **M–L (medium-to-large).** Heaviest build in the slate: shared site-DB infrastructure + licensing AND multi-vendor proprietary BLE integration. Explicitly the highest-build, thinnest-TAM bet — see riskiest assumptions.

**Top 3 riskiest assumptions:**
1. **TAM volume (the kill risk).** Certified-diver intent is validated (r/scuba ~111K) but *volume* is not. The addressable, paying, computer-owning diver population may be too thin to return the heaviest build cost in the slate. **Dominant assumption; the reason for the FREE community-demand gate before any paid spend.**
2. **Multi-vendor BLE import actually works reliably** across Shearwater + Suunto + Garmin + Mares proprietary protocols on real devices in real (post-dive, salt-water, low-signal) conditions — and that each can be legally/technically integrated. If even 2 of 4 vendors are unreliable, the core differentiator collapses.
3. **The shared site DB is licensable/seedable and stays moderated-clean.** A credible initial site dataset can be acquired legally, moderation is sustainable, and FrostAura Underwater/OceanCare adjacency delivers the near-zero-CAC organic seeding it promises.

---

## LAYER 2 — PRD

### Feature list — MVP vs Later

**MVP (vertical slice must contain all of these):**
- **Account + auth** (email/social), entitlement-bound, survives reinstall.
- **Manual dive entry** (depth, time, date, site, conditions, gas, gear, buddies, notes, rating).
- **Bluetooth dive-computer import — ONE vendor end-to-end in the slice (Shearwater first), with the import abstraction designed for all four.** Free tier: capped import; Pro/Lifetime: unlimited.
- **Shared dive-site database (read)** — search by name/GPS, view site detail (depth range, hazards, marine life, access, community notes). Read access in free tier.
- **Site auto-match** on import (GPS + name).
- **Site contribution/correction (write)** — gated to verified/Pro accounts; **enters a moderation queue** before going live.
- **Dive profile visualization** (depth-over-time graph from computer data).
- **Logbook view + running stats** (total dives, max depth, total bottom time, etc.). Free: last-N-dives cap; Pro/Lifetime: unlimited.
- **Paywall + IAP** (the purchase moment) — Pro one-off + Lifetime non-consumable. Restore purchases.
- **Analytics + entitlement rails (built FIRST — see rails-first).**

**Later (post-slice):**
- Remaining 3 BLE vendors hardened to production (Suunto, Garmin, Mares).
- Photo/video attachment + galleries.
- Offline site-DB pack (Pro perk: download region for offline use).
- Buddy/social graph, dive-trip grouping, export (PDF/UDDF/CSV) for certification records.
- Marine-life logging tied to OceanCare species taxonomy (Underwater adjacency).
- Watch companions. Localization. Tablet layout.

### Primary user flows

**Flow A — First-run → first logged dive (manual):**
1. Install → open → onboarding (3 screens: what it is, site-DB value, computer-import value) → `first_run_completed`.
2. Create account / continue as guest (guest data is local until account created) → `account_created`.
3. "Log your first dive" → choose **Import from computer** or **Add manually**.
4. (Manual) fill dive form → search site in shared DB → select/confirm site → save → `dive_logged_manual`.
5. View dive detail + running stats → prompted to import from computer for accuracy.

**Flow B — Bluetooth import (differentiator → purchase moment):**
1. Tap **Import from computer** → request BLE permission (just-in-time, with rationale) → `ble_permission_requested` / `ble_permission_granted|denied`.
2. App scans, discovers computer → user selects device → `ble_device_connected` (with `vendor`).
3. App reads new dives via vendor protocol → `dives_fetched` (count, vendor).
4. **Within free cap:** dives imported, sites auto-matched → `dive_logged_import`.
5. **Free cap hit:** show **purchase moment** paywall → `paywall_shown` (trigger=`ble_import_cap`) → Pro or Lifetime → IAP → `purchase_completed` → `entitlement_granted` → import unlocked, all dives pulled.

**Flow C — Site DB read/contribute:**
1. Browse/search sites (map + list) → open site detail → `site_viewed`.
2. (Pro/verified) "Suggest correction / add site" → form → submit → moderation queue → `site_contribution_submitted` (status=pending).
3. Moderator approves/rejects (server) → contributor notified → `site_contribution_resolved` (status=approved|rejected).

**Flow D — Restore / reinstall:**
1. Reinstall → sign in → `restore_purchases` auto-invoked on first launch + manually available in settings → server reconciles receipt → `entitlement_restored`. Entitlement MUST survive reinstall.

### Data model sketch
- **User** { id, auth_provider, email, created_at, verified_bool }
- **Entitlement** { user_id, tier (free|pro|lifetime), source_receipt_id, granted_at, platform } — server-authoritative.
- **Dive** { id, user_id, datetime, duration, max_depth, avg_depth, min_temp, gas_mix, source (manual|import), source_vendor, profile_samples[], site_id, conditions, gear[], buddies[], notes, rating, created_at }
- **DiveComputer** { id, user_id, vendor, model, ble_identifier, last_sync_at }
- **Site** (shared) { id, name, geo (lat/lng), depth_range, type, hazards[], marine_life[], access_notes, status (live), source (seed|contributed) }
- **SiteContribution** { id, site_id?, user_id, payload, status (pending|approved|rejected), moderator_id, resolved_at }
- **ProfileSample** { dive_id, t_seconds, depth_m, temp_c, ppo2? }

### On-device vs server decisions
- **On-device:** BLE discovery + protocol parsing (raw computer reads happen on the phone), local cache of the user's dives, last-downloaded site pack (Pro offline, Later), draft entries.
- **Server-authoritative:** entitlements (NEVER trusted from client), shared site DB + moderation queue, account/auth, cross-device dive sync, receipt validation.
- **Explicit trust rule:** entitlement and site-DB integrity are server-authoritative. Dive-profile data parsed on-device is trusted as the user's own record; anything written to the shared site DB passes server-side moderation before becoming visible.

### Privacy / permissions posture
- **Bluetooth:** just-in-time with explicit rationale (only when importing). Never on launch.
- **Location:** site auto-match/search only; requested when first needed, "while using" only. No background location.
- **Photos:** first attach only (Later).
- **Data minimization:** dive logs default private. Site contributions are public-by-design and the UI states this before submit. No ATT prompt in MVP (premium model, no ads). GDPR/CCPA export + delete supported.

### Accessibility baseline
Dynamic Type / scalable fonts; screen-reader labels on all controls; min 4.5:1 contrast; profile graph has a text/table alternative; tap targets ≥44pt; no color-only state encoding. WCAG 2.1 AA target for all MVP screens.

### Platform / stack choice + rationale
- **Mobile: .NET MAUI shared codebase** (Amendment 2) — net10.0-android first, net10.0-ios second. BLE reliability remains the riskiest technical assumption; BLE access goes through per-platform bindings (Android BLE / CoreBluetooth) behind `IDiveComputerTransport`, keeping vendor protocol parsers pure C# and unit-testable.
- **Backend: mandated stack — .NET 10 + EF Core + PostgreSQL** (site DB, accounts, entitlements, moderation, receipt validation). PostGIS for geo site search.
- **Site-DB licensing:** initial seed dataset must be legally sourced/licensed before launch — blocking task (slice ships with a small original/openly-licensed seed).

### Non-goals (explicit)
- **NO auto-renewing subscriptions. Ever.**
- No randomized/loot-box/gacha mechanics of any kind.
- No ads in MVP (premium model).
- No dive *planning/deco computation* engine (liability + scope; we log, we don't compute deco).
- No social network / feed beyond site contributions in MVP.
- No support for non-Bluetooth/legacy IR-only computers in MVP.
- No consumer web app in MVP.

### Rails-first requirement (binding)
Analytics event taxonomy (Layer 3) and the IAP/entitlement pipeline (Play Billing / StoreKit 2 → server receipt validation → entitlement record) **must be built and verified in sandbox BEFORE any content/feature work beyond the auth shell.** A dive cannot be the first thing built; the funnel and the till are.

### Restore / receipt / entitlement-survives-reinstall (binding)
- Purchases are server-validated; entitlement stored server-side keyed to account.
- **"Restore Purchases"** present in settings AND auto-invoked on fresh install after sign-in.
- Entitlement MUST survive reinstall, device change, and OS reinstall (account-bound, not device-bound).
- Lifetime is a **true non-consumable** — restorable forever — NOT a relabeled auto-renew product.

---

## LAYER 3 — MONETIZATION DESIGN DOC (binding instrumentation contract)

### Locked price ladder (deterministic, one-off only — NO auto-renew)

| SKU ID | Display | Type | Price (USD) | Unlocks |
|---|---|---|---|---|
| `trimix.pro` | **Pro** | One-off premium (non-consumable) | **$14.99** | Unlimited multi-vendor BLE import, unlimited logbook, site-DB write/contribute |
| `trimix.lifetime` | **Lifetime** | **True non-consumable** (NOT auto-renew) | **$29.99** | Everything in Pro + offline site-DB packs + all future Pro perks, forever |

Exactly two paid SKUs. No consumables. No DLC in MVP. No auto-renew.

### Net-of-fee contribution per SKU (CAC = explicit placeholder `[CAC_PER_PAYER = TBD — set by S5 ad-creative CPI test]`)

| SKU | Gross | Net @ 15% (SBP small-biz) | Net @ 30% (standard) |
|---|---|---|---|
| Pro $14.99 | $14.99 | **$12.74** | **$10.49** |
| Lifetime $29.99 | $29.99 | **$25.49** | **$17.99** |

> Carry both fee tiers forward; the blended ~$13/payer planning number stands. Contribution per payer = `net_per_payer − [CAC_PER_PAYER]`. CAC remains a placeholder until the S5 CPI test replaces it.

### Conversion / attach assumptions (pre-registered, to be validated)
- Install → first-run completion: ~70%. First-run → first dive logged: ~45%.
- Activated user → hits BLE-import purchase moment: ~35% of dive-loggers.
- Purchase-moment → purchase: **pre-registered 3–5% install→payer**. Blended net/payer ~$13.
- Pro vs Lifetime split: ~60/40 among payers.
- All assumptions are placeholders the funnel below exists to measure.

### Funnel event taxonomy (frozen names — binding analytics contract)

| Event | Fires when | Required properties |
|---|---|---|
| `install` | First app open (attributed) | `platform`, `source`, `app_version` |
| `first_run_completed` | Onboarding finished | `user_pseudo_id` |
| `account_created` | Account established | `auth_provider` |
| `dive_logged_manual` | Manual dive saved | `dive_id`, `site_matched_bool` |
| `ble_permission_requested` / `ble_permission_granted` / `ble_permission_denied` | BLE permission prompt lifecycle | `outcome` |
| `ble_device_connected` | Computer connected over BLE | `vendor`, `model` |
| `dives_fetched` | Dives read from computer | `vendor`, `count` |
| `dive_logged_import` | Imported dive saved | `dive_id`, `vendor`, `source=import` |
| `paywall_shown` | **PURCHASE MOMENT** displayed | `trigger` (`ble_import_cap`\|`logbook_cap`\|`site_contribute`), `entitlement_state=free` |
| `purchase_started` | User taps a SKU | `sku_id` |
| `purchase_completed` | Store confirms purchase | `sku_id`, `gross_price`, `currency` |
| `purchase_failed` / `purchase_cancelled` | Purchase did not complete | `sku_id`, `reason` |
| `entitlement_granted` | Server validates receipt + grants tier | `tier`, `source_receipt_id` |
| `entitlement_restored` | Restore reconciles existing receipt | `tier` |
| `site_viewed` | Site detail opened | `site_id` |
| `site_contribution_submitted` | Contribution enters moderation | `status=pending` |
| `site_contribution_resolved` | Moderator decision | `status` (`approved`\|`rejected`) |

Funnel spine (must reconcile end-to-end in the slice): `install → first_run_completed → dive_logged_* → paywall_shown → purchase_completed → entitlement_granted`. Reinstall path: `install → entitlement_restored`.

### Entitlement model
Two tiers above free: **pro** and **lifetime** (lifetime ⊇ pro). Server-authoritative, account-bound, reconciled from validated Play Billing / StoreKit 2 receipts. Free tier = capped import (last-N dives, single import session) + read-only site DB + capped logbook. Entitlement survives reinstall/device change.

### Pre-registered Stage-5 success thresholds
1. **FREE community-demand gate (before ANY paid UA):** ≥300 organic activated divers (≥1 dive logged) AND ≥40% BLE-import success among computer-owners, within the test window. Failure = NO-GO / loop back, no paid spend.
2. **Purchase-intent threshold:** ≥3% install→payer at the locked ladder, blended net/payer ≥$13, and ≥1 real sandbox-validated purchase + entitlement reconciliation in the slice.
3. **Kill line:** BLE import succeeds for <2 of 4 vendors reliably in field conditions, OR the free-demand gate misses by >40% → the bet is **killed**, not advanced.

---

## Carried-forward risks & open questions
- **[DOMINANT — TAM volume]** Heaviest build in the slate against the thinnest TAM; the FREE gate exists to kill this cheaply. Open: true count of computer-owning, actively-logging divers reachable via Underwater/OceanCare organic channels?
- **[BLE multi-vendor]** Slice proves ONE vendor (Shearwater); the other three are unvalidated and the differentiator depends on ≥2 working. Kill line tied to this.
- **[Site-DB licensing + moderation]** Seed dataset must be legally licensed (blocking). Moderation ops cost unsized. Open: does OceanCare adjacency actually deliver near-zero-CAC seeding and volunteer moderators?
- **[Name/IP]** Trimix selected but flagged high-collision-risk; store + `.com` + USPTO re-check is a blocking gate before any listing/domain/trademark.
- **[CAC placeholder]** `[CAC_PER_PAYER = TBD]` until the S5 ad-creative CPI test — itself gated behind the free community-demand pass.
- **[Pricing WTP]** $14.99/$29.99 anchored to MacDive/Currents — competitor-anchored, not validated for this bundle. Confirm in S5 painted-price test.
