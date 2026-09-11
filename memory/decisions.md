---
name: fa-trimix-decisions
description: "Serverless and on-device is a product invariant, not a phase: no backend, no auth, API sync the only dive-entry path, entitlements client-trusted and piratable by choice, and reverse-engineered connectors the primary surface by override."
type: decision
last_verified: 2026-09-11
---

# Live decisions — product, economics and legal posture

_Product, economics and cross-cutting policy. Multi-provider merge and sync behaviour:
[`decisions-sync.md`](decisions-sync.md). Per-vendor reverse-engineering:
[`decisions-connectors.md`](decisions-connectors.md). Layout and visual: [`decisions-ui.md`](decisions-ui.md).
Haptics, motion and perceived performance: [`decisions-haptics.md`](decisions-haptics.md)._

- **Serverless / on-device pivot (2026-07-27, founder-directed).** The prototype's "no cloud, no servers"
  became a product invariant: backend deleted, entitlements on-device (client-trust, piratable — accepted),
  analytics fire to a local sink. This **reversed the 2026-07-24 "nothing further gets built until M3 runs"
  parking decision** — the founder re-scoped the whole product, so M3 will now be run against the serverless
  shape rather than the prototype's.
- **API-sync-only (2026-07-27).** BLE, file import/export, manual entry and HealthKit removed; cloud/API
  provider sync is the only dive-entry path. "For now we only want API sync" — founder.
- **Connector promotion (2026-07-27).** The reverse-engineered connectors moved from "ship dark, default-off"
  to the **primary product surface** — registered and visible by default, going beyond the 2026-07-05
  override. **The three user-protecting guardrails still stand** and every `ToSRiskReverseEng` descriptor
  still carries `EnabledByDefault=false`; availability now comes from the per-user consent gate, not that
  flag. Guardrail tests run over all the cloud connectors.
- **Provider slate (2026-07-27, since amended).** Read IN: Garmin, Suunto, ScubaPro, Mares, Oceanic+ — plus
  **Shearwater, added 2026-08-07 as a deliberate dark placeholder** (Shearwater Cloud is desktop/BLE, there is
  no phone cloud to read, so its methods throw "coming soon"). Push-only, never read corridors: PADI, SSI. The
  original "Mares and ScubaPro have no cloud of their own, both reached via the shared MySSI corridor" premise
  **was wrong for ScubaPro and is therefore suspect for Mares** — see `decisions-connectors.md`.
- **Provider availability is an allowlist, not a per-connector flag (2026-08-07, founder-directed).** Only ids
  in `ProviderCatalog.LiveConnectorIds` are connectable; every other registered connector renders **dimmed
  with a subtle "COMING SOON" pill**, `ConnectAsync` no-ops, and `LoadAsync` force-keeps it out of the
  Connected group even if consent or a token exists. **To promote a provider, add its id to that one set** —
  and update onboarding S1's static SYNCS WITH / COMING SOON wrap rows in the same pass, since they mirror it.
  Rationale: keep the Providers list and onboarding honest about what actually syncs.
- **No auth of any kind (2026-07-29, founder).** Phone-is-the-account, minimum cognitive load: no biometric
  unlock, no accounts, no sign-in. This voided the 2026-07-27 biometric-only decision, which had itself voided
  passkeys and accounts. Vendor OAuth for connectors is unrelated and stays.
- **Free tier = last 5 dives (2026-07-27).** `FreeTier` constants changed from 25 visible / 3 per-import to
  **5**, matching the prototype; the 6th dive fires `paywall_shown(cloud_sync_cap)`, store-aware across
  sessions. The SKU ladder is unchanged ($14.99 / $29.99, no auto-renew).
- **Standing from 2026-07-03/07-05:** the aggregator pivot and its legal override (no clean published dive API
  exists, so vendor-cloud pull and cert-agency push are reverse-engineered — a knowingly-accepted gate-3
  ToS/CFAA risk); the name "Trimix", founder-picked and flagged high-collision; the root-reviewed MAUI client
  override.
