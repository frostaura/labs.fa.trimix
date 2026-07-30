---
name: fa-trimix-decisions
description: "Serverless pivot (reversed the parking); API-sync-only; connectors promoted to primary surface with guardrails; no auth of any kind; free tier = last 5 dives"
type: decision
last_verified: 2026-07-30
---

# Live decisions

- **Serverless / on-device pivot (2026-07-27, founder-directed).** The prototype's "no cloud, no servers" is now a product invariant. Backend deleted; entitlements on-device (client-trust, piratable — accepted); site atlas bundled read-only; analytics fire to a local sink. **This reversed the 2026-07-24 "nothing further gets built until M3 runs" parking decision** — the founder directed the rebuild explicitly, so the parking is lifted. Rationale: the founder re-scoped the whole product from a prototype; M3 will now be run against this serverless shape.
- **API-sync-only (2026-07-27).** BLE, file import/export, manual entry, and HealthKit removed; the only dive-entry path is cloud/API provider sync. "For now we only want API sync" — founder.
- **Connector promotion (2026-07-27).** The reverse-engineered connectors moved from "ship dark, default-off" to the **primary product surface** — registered/visible by default. This goes beyond the 2026-07-05 override. **The three user-protecting guardrails still stand** and every `ToSRiskReverseEng` descriptor still carries `EnabledByDefault=false`; availability now comes from the per-user consent gate, not that flag. Guardrail tests run over all five cloud connectors.
- **Provider slate = 7 (2026-07-27).** IN: Garmin, Suunto, ScubaPro, Mares, Oceanic+. OUT: PADI, SSI. Mares + ScubaPro have no cloud of their own → reached via the shared MySSI corridor.
- **Auth = biometric-only local unlock (2026-07-27). ~~SUPERSEDED 2026-07-29~~ → NO auth of any kind.** The founder removed all auth on 2026-07-29 (phone-is-the-account, minimal cognitive load): no biometric unlock, no accounts, no sign-in. See the 2026-07-29 live-work section above. The 2026-07-27 biometric-only decision (which had itself removed passkeys/accounts) is void.
- **Free tier = last 5 dives (2026-07-27).** `FreeTier` constants changed from 25 visible / 3 per-import to **5**, matching the prototype; the 6th dive fires `paywall_shown(cloud_sync_cap)`, store-aware across sessions. SKU ladder unchanged ($14.99 / $29.99, no auto-renew).
- **Aggregator pivot (2026-07-05) and the legal override (2026-07-05)** still hold: no clean published dive API exists; vendor-cloud pull + cert-agency push are reverse-engineered, a knowingly-accepted gate-3 ToS/CFAA risk. **Name "Trimix" founder-picked 2026-07-03** (flagged high-collision). **MAUI client override root-reviewed 2026-07-03.** **PostGIS rejected** in favour of in-memory haversine — the new `SiteAtlas` matcher continues that choice.
