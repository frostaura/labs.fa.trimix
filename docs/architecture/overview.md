# Trimix — Architecture (serverless rebuild, 2026-07-27)

Binding contract for all implementation work. When this doc and code disagree, stop and resolve the drift (spec-driven mandate). This version describes the app **after the serverless / API-sync-only pivot** (PRD Amendments set 3); the previous version of this file described the client–server slice and is superseded in full.

## Solution layout

```
labs.fa.trimix.slnx
Directory.Build.props            # net10.0 defaults, nullable enable, implicit usings;
                                 #   TreatWarningsAsErrors=true except Trimix.App
src/
  shared/Trimix.Contracts/       # net10.0 — DTOs, enums, FROZEN funnel event names + required props,
                                 #   SKU ids, free-tier constants (5). No dependencies.
  app/Trimix.Core/               # net10.0 — ALL client logic, no MAUI dependency:
                                 #   viewmodels, connector layer (7 providers), sync coordinator,
                                 #   import pipeline, entitlement service/gate, sandbox billing,
                                 #   local analytics, bundled site atlas, stats, local-store abstractions.
  app/Trimix.App/                # MAUI (net10.0-android; net10.0-ios opt-in via -p:EnableIos=true) —
                                 #   XAML views, Shell nav, platform impls (SecureStorage, Preferences,
                                 #   biometric unlock, billing adapters), DI bootstrap, Sonar resources.
tests/
  Trimix.Core.Tests/             # xUnit — 345 tests: viewmodels, connectors (against recorded fixtures),
                                 #   caps/gates, analytics contract, funnel reconciliation.
```

There is **no backend project, no database server, no docker-compose**. `Trimix.Api`, EF Core/PostgreSQL, and `tests/Trimix.Api.Tests` were deleted in the pivot. Project references: `App → Core → Contracts`; nothing references App.

## Cross-cutting decisions (locked)

1. **Serverless / on-device everything.** "No cloud, no servers — logbook and links never leave the device." No accounts, no cross-device sync, no server API surface of our own. The only network traffic the app ever makes is the per-user-consented provider sync (decision 3).
2. **Auth = biometric-only local unlock.** `IBiometricUnlock` (iOS LocalAuthentication / Android BiometricPrompt; emulator fallback succeeds) gates the app locally. No passkeys, no email/OAuth accounts, no relying party. `AuthSession` is purely local device state in `ISecureStore`.
3. **Connector layer is the import surface — API sync only.** `IDiveSourceConnector` / `IDiveDestinationConnector` with a self-declared `ConnectorDescriptor` (`ConnectorId`, `Kind`, `LegalPosture`, `ProfileFidelity`, `AuthModel`, `Platforms`, `EnabledByDefault`). Seven on-device reverse-engineered private-cloud clients register: sources Garmin Dive, Suunto, ScubaPro, Mares, Oceanic+; destinations PADI, SSI. Mares + ScubaPro + SSI share `MySsiCloudClient`. BLE, file import/export, manual entry and HealthKit are removed. `SyncCoordinator` ("Sync now") enumerates consented+authenticated sources, runs `ImportService` incrementally via a per-connector `SyncCursor` in `ISecureTokenStore`, then pushes new dives to consented destinations. The cursor only advances on a fully-imported run, so cap-truncated dives are re-fetched after upgrade (deterministic dive ids make re-import idempotent).
4. **Connector legal gating is structural.** Every `ToSRiskReverseEng` descriptor ships `EnabledByDefault=false`; connectors are *registered and visible* by default, but zero network I/O happens before per-user consent recorded behind a plain-language ToS-risk disclosure. Vendor passwords are used once for an ephemeral credential→token exchange; only the token persists (Keychain/Keystore). Labels are truthful: "personal export", never "official <Vendor>". Endpoints are researched-guess constants validated only against recorded fixtures — never exercised against real vendor production servers from this environment.
5. **Entitlements are on-device (client-trust).** `EntitlementService` derives the tier locally from store receipts (`IBillingService` purchase/restore → SKU → tier) and caches it in secure storage. Restore is the single grant path after reinstall — the store owns the receipt. There is **no server receipt validation; this is piratable by design**, the accepted cost of serverless. `SandboxBillingService` is the exercised path today; real StoreKit/Play Billing are runtime-gated on store accounts + name clearance.
6. **Free tier keeps the last 5 dives.** `FreeTier` constants (both 5) enforced by `EntitlementGate` in Core; the 6th dive fires `paywall_shown`. UI copy binds to the constant.
7. **Analytics: frozen taxonomy, local sink.** `IAnalytics.Track` validates against the frozen `FunnelEvents` contract (throw in Debug, drop+log in Release), persists a durable queue via `ILocalStore`, and drains to the debug log — **there is no ingest endpoint**. Retained-but-no-longer-emitted names (`ble_*`, `dive_logged_manual`, `dives_fetched`, `site_contribution_*`, `account_created`, trigger `ble_import_cap`) stay in the contract; nothing is renamed or deleted. Live spine: `install → first_run_completed → source_connected → dives_synced → dive_logged_import → paywall_shown(cloud_sync_cap) → purchase_completed → entitlement_granted`.
8. **Sites: derived + bundled atlas.** The Sites tab derives from the user's own synced dives. `Sites/SiteAtlas.cs` is a bundled, read-only 14-site seed atlas (bounding-box prefilter + in-memory haversine) used for auto-match and the dive-detail "NEAR THIS SITE" carousel. No live community DB, no contributions, no moderation.
9. **Local-first storage.** Dives live in local SQLite (`SqliteLocalStore`; `InMemoryLocalStore` for tests). There is nothing to sync them to.
10. **No deco computation, ever.** Displaying imported NDL/ceiling/CNS and the You-tab Nitrox MOD planner (MOD = (1.4/FO₂ − 1) × 10) is display math over recorded/entered values, not tissue-loading computation.
11. **Profile chart** drawn with `GraphicsView`/`IDrawable` (no external chart package): mint depth line, touch-to-scrub crosshair + tooltip, accessible table alternative.

## App structure (Shell)

Tabs: **Log · Sites · You** (3-tab, per the prototype). Eleven prototype screens map onto: `OnboardingPage` (Welcome + How it works), `AuthPage` (biometric Account), `LogbookPage`, `ConnectedAccountsPage` (Providers), `ProviderOAuthPage` (provider sign-in sheet), `DiveDetailPage`, `PaywallPage` (Purchase moment + Unlocked as two states of one page), `SitesPage`/`SiteDetailPage`, `StatsPage` (You), with `SettingsPage` pushed from You's gear. Every page = XAML view + Core viewmodel. Accessibility: SemanticProperties, dynamic font scaling, ≥44pt targets, chart table alternative.

## Definition of done (Silver, adapted)

`dotnet build` clean (incl. net10.0-android) · `dotnet test` green (`Trimix.Core.Tests`, 345 tests at last gate) · funnel spine reconciling in tests · README run-instructions accurate. **No CI exists — every gate claim is local evidence on one machine.**
