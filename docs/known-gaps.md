# Trimix — Known Gaps (serverless rebuild, as of 2026-07-27)

Honest record of what the app does NOT do, so nothing reads as shipped-when-it-isn't. This catalogue was rewritten for the serverless / API-sync-only pivot (PRD Amendments set 3); the previous version catalogued the client–server build (BLE, passkeys, server receipt validation, GDPR endpoints) and is superseded — those subsystems no longer exist to have gaps. Current evidence: 345/345 Core tests green, Android build clean, `net10.0-ios` compiling — **all local-only; there is no CI**.

## Connectors — the load-bearing gap

- **Every provider integration is fixture-validated only.** The seven connectors (Garmin, Suunto, ScubaPro, Mares, Oceanic+ in; PADI, SSI out) are reverse-engineered private-cloud clients whose endpoints, auth flows and payload shapes are **researched guesses** validated exclusively against recorded/derived fixtures. **No request has ever been made to a real vendor production server from this environment.** Any of them may fail on first live contact; Oceanic+ constants are explicitly placeholder-grade. Live validation + legal sign-off precede any store listing.
- **The MySSI corridor concentrates risk.** Mares, ScubaPro and SSI all ride one shared reverse-engineered client (`Connectors/Infra/MySsiCloudClient.cs`). One vendor-side change breaks three of seven integrations at once.
- **The legal posture is an accepted risk, not an oversight.** No vendor publishes a usable dive API; Garmin/Suunto partner APIs are enterprise-gated (documented as a future swap, not an available path). Shipping reverse-engineered clients is the founder-directed legal override (2026-07-05), knowingly failing gate-3's "integrated legally". The three user-protecting guardrails (token-only persistence, consent behind ToS-risk disclosure, truthful labelling) stand and are test-pinned.
- **M3 field reliability is untouched.** "≥2 providers importing reliably in real post-dive conditions" is field work against live backends; nothing in this repo advances it.

## Entitlements — piratable by design

- **On-device entitlements are client-trust.** Purchase/restore derives the tier locally and caches it in secure storage; there is no server receipt validation and no server to add one. A rooted/jailbroken device or a tampered store client can grant itself Pro. **Accepted serverless tradeoff (founder decision, 2026-07-27)** — record it, don't rediscover it.
- **Real store billing is runtime-gated.** `SandboxBillingService` is the only exercised purchase path. Real StoreKit/Play Billing need store accounts and a listing, blocked on **name clearance**.

## Telemetry — the gates can't be measured as built

- **M3/M4 gate metrics cannot be aggregated.** Analytics validate against the frozen taxonomy and drain to a local on-device sink (debug log); there is no ingest server. Activated-diver counts and install→payer conversion — the numbers M3/M4 exist to measure — are invisible without adding a thin analytics beacon. That is an open product decision (it reopens a hole in "no cloud, no servers"), deliberately not made here.
- The retained-but-no-longer-emitted events (`ble_*`, `dive_logged_manual`, `dives_fetched`, `site_contribution_*`, `account_created`, trigger `ble_import_cap`) are frozen names kept per the additive-only contract — do not delete them to "clean up".

## Auth & privacy

- **Biometric unlock proves the flow, not the security, off real hardware.** The emulator fallback succeeds without verifying anything; only a physical device with Face ID / fingerprint exercises real verification.
- **GDPR posture is local-only now.** With no server, export/delete reduce to on-device data handling plus connector consent/token purge (`ConnectorPurgeService`). The vendor clouds the connectors talk to hold the diver's data under the *vendor's* privacy regime — Trimix only ever reads/pushes with the diver's own credentials.

## Sites & content

- **The atlas is a 14-site bundled seed** (`Sites/SiteAtlas.cs`) — honest approximations of publicly-known values, fine for a prototype-faithful build. Any larger dataset must be legally licensed (launch-track item). There is no community site DB, contribution flow, or moderation — deleted, not deferred.

## Meta

- **No CI, no reproducible build.** Every "tests green / build clean" claim is a local `dotnet build`/`dotnet test` on one Mac with a user-space toolchain (`~/.dotnet` 10.0.301, OpenJDK 17). Nothing is store-shipped; the app is demo-grade end to end.
- **Name clearance** ("Trimix", flagged high-collision in dive software) remains the blocking gate before any listing, domain, or trademark — and transitively before real billing.
- **iOS** compiles (`-p:EnableIos=true`) and has run on the simulator, but Android is the proven target; no physical-iPhone validation of biometrics or billing has occurred.
