# MEMORY — fa.trimix

_Last verified: 2026-07-27 by the serverless-rebuild session (four-phase teardown + rebuild; Core builds, 345/345 Core tests pass, Trimix.App builds for net10.0-android and net10.0-ios, all inspected directly)._

## Current state
**Rebuilt from a server-backed BLE aggregator into a serverless, on-device, API-sync-only dive log** matching the founder's 2026-07-27 prototype (`~/Desktop/Trimix Prototype.html`). **Committed and pushed 2026-07-28**: the rebuild is commit `2151827` on `main`, tracking `origin/main`, fully pushed. The GitHub repo **did not previously exist** (the `git@github.com:...` URL had been configured but never created); it was created **private** as `frostaura/fa.trimix` (default branch `main`) and `main` fast-forwarded over the two prior commits to include the rebuild. **The remote was switched from SSH to HTTPS** because this Mac has no SSH key — `gh` is authenticated as `frostaura` over HTTPS and is the working push path here; switch back to SSH once a key is added. Working tree clean; stray `.DS_Store` files removed.

Build evidence (local only, no CI): `Trimix.Contracts` + `Trimix.Core` + `Trimix.App`, **345 Core tests green**, Android + iOS TFMs both compile. Toolchain is user-local `~/.dotnet` (10.0.301) + OpenJDK 17 + Android SDK — the system dotnet cannot build the repo (missing workload manifests).

## What the rebuild did (2026-07-27, four phases)
1. **Removal** — deleted the entire `Trimix.Api` backend, EF/PostgreSQL, docker-compose, `tests/Trimix.Api.Tests`, and all client→server code (`IApiClient`, `SyncService`, passkey/GDPR/moderation clients, server analytics/receipt validation). Fully excised BLE (all vendor protocols, transports, `ImportPage`, permissions). Removed file import/export, manual entry, and Apple HealthKit. 201 files deleted. `ImportedDive` relocated to `Abstractions/`; the FIT decoder relocated to `Connectors/Fit/` (it is a dependency of the surviving Garmin/Suunto cloud connectors — a trap caught mid-teardown).
2. **Data layer** — the connector seam and the reverse-engineered Garmin/Suunto/Oceanic+ (IN) and PADI/SSI (OUT) connectors already existed; added **Mares** and **ScubaPro** sources plus a shared `MySsiCloudClient` (Mares + ScubaPro + SSI all route through MySSI — the highest-leverage integration). Added `SyncCoordinator` ("Sync now" — the production caller `ImportService` had been missing since BLE was the only path). Master switches flipped default-ON so all seven register.
3. **UX rebuild** — the 11 prototype screens: onboarding + "how it works", biometric unlock (replacing passkey auth), Providers + in-app OAuth (guardrail-exact consent→auth→first-sync), streaming sync sheet, paywall→Unlocked on on-device entitlements, Logbook route strings + stat strip, dive detail with touch-to-scrub + site-atlas carousel + notes, derived Sites tab, You tab (editable name, units, Nitrox MOD planner), Settings trimmed to serverless truth. Added a bundled read-only `Sites/SiteAtlas.cs` (14 seed sites + haversine matcher) feeding auto-match and the carousel.
4. **Docs/context** — this pass.

## Live decisions
- **Serverless / on-device pivot (2026-07-27, founder-directed).** The prototype's "no cloud, no servers" is now a product invariant. Backend deleted; entitlements on-device (client-trust, piratable — accepted); site atlas bundled read-only; analytics fire to a local sink. **This reversed the 2026-07-24 "nothing further gets built until M3 runs" parking decision** — the founder directed the rebuild explicitly, so the parking is lifted. Rationale: the founder re-scoped the whole product from a prototype; M3 will now be run against this serverless shape.
- **API-sync-only (2026-07-27).** BLE, file import/export, manual entry, and HealthKit removed; the only dive-entry path is cloud/API provider sync. "For now we only want API sync" — founder.
- **Connector promotion (2026-07-27).** The reverse-engineered connectors moved from "ship dark, default-off" to the **primary product surface** — registered/visible by default. This goes beyond the 2026-07-05 override. **The three user-protecting guardrails still stand** and every `ToSRiskReverseEng` descriptor still carries `EnabledByDefault=false`; availability now comes from the per-user consent gate, not that flag. Guardrail tests run over all five cloud connectors.
- **Provider slate = 7 (2026-07-27).** IN: Garmin, Suunto, ScubaPro, Mares, Oceanic+. OUT: PADI, SSI. Mares + ScubaPro have no cloud of their own → reached via the shared MySSI corridor.
- **Auth = biometric-only local unlock (2026-07-27).** Passkeys and accounts removed. Supersedes the former passkey-only invariant.
- **Free tier = last 5 dives (2026-07-27).** `FreeTier` constants changed from 25 visible / 3 per-import to **5**, matching the prototype; the 6th dive fires `paywall_shown(cloud_sync_cap)`, store-aware across sessions. SKU ladder unchanged ($14.99 / $29.99, no auto-renew).
- **Aggregator pivot (2026-07-05) and the legal override (2026-07-05)** still hold: no clean published dive API exists; vendor-cloud pull + cert-agency push are reverse-engineered, a knowingly-accepted gate-3 ToS/CFAA risk. **Name "Trimix" founder-picked 2026-07-03** (flagged high-collision). **MAUI client override root-reviewed 2026-07-03.** **PostGIS rejected** in favour of in-memory haversine — the new `SiteAtlas` matcher continues that choice.

## Gotchas
- **Never pushed; large uncommitted rebuild on the iCloud mount.** Until a commit + push lands, a single iCloud eviction loses the entire rebuild. Run `repo-durability-check` before asserting any git safety. (The earlier stale `.git/*.lock` files are gone — the tree is a clean checkout.)
- **No CI.** Every "green" rests on a local `dotnet build`/`test` on one Mac with the user-local toolchain. Never mark a gate met on that basis.
- **Connectors are fixture-validated only.** No live vendor server was ever touched. Endpoints (Garmin SSO, Suunto `/v1`, Oceanic+, MySSI for Mares/ScubaPro/SSI, PADI GraphQL) are **researched-guess defaults**, config-injectable, and will likely need real endpoints at first live contact. Garmin's unofficial path (`garth`) died March 2026; `python-garminconnect`-style Android SSO is the surviving reverse-eng route. **No connector has ever imported a real dive.**
- **On-device entitlements are piratable** (no server receipt validation) — the accepted serverless tradeoff.
- **The community-atlas "N divers logged here" counters are bundled seed facts, not live data** — the serverless model has no shared DB.
- **Dive-row push destinations reflect *currently linked* push providers**, not a per-dive push ledger (same approximation the prototype makes).

## Open questions
- **M3/M4 telemetry.** A serverless app cannot aggregate "≥300 activated divers" — the funnel fires locally with no ingest. Running M3 needs *some* thin analytics beacon (the one plausible exception to "no servers"). Founder decision.
- **Whether the heaviest-build, thinnest-TAM bet is worth field-testing at all** — unchanged by the rebuild; the free gate exists to answer it cheaply.
- **Name clearance** and **site-atlas seed licensing** remain launch blockers.
- **`[CAC_PER_PAYER = TBD]`** — still an unmeasured placeholder; no CPI test has run.

## Watch list
- **Durability done (2026-07-28):** rebuild committed (`2151827`) and pushed to the new private `frostaura/fa.trimix`. Remaining git note: origin is HTTPS on this Mac (no SSH key); the founder's other machines using the SSH URL will need this repo to exist (it now does) and their own auth.
- **A fresh adversarial re-review of the rebuilt tree has not been run** — recommended before any M3 field testing, especially over the seven reverse-engineered connectors and the on-device entitlement path.
- **`SiteDetailPage`/`SiteDetailViewModel` remain in the tree, stubbed and unrouted** from the new derived Sites tab — a Phase-cleanup candidate.
- **Dead server DTOs** (`ApiRoutes.cs`, `PasskeyDtos`, `PrivacyDtos`, `ContributionDtos`) remain in `Trimix.Contracts` as frozen shapes; `network_security_config.xml` (dev-loopback) is now moot — cleanup candidates.
