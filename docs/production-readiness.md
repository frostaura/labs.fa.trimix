# Trimix — Production Readiness Ledger

**Status:** Production-real inventory for the four production-wiring slices — **release gate PASSED 2026-07-06** · **Date:** 2026-07-06 · **Owner:** Production architect (fa.trimix)

**Release gate (2026-07-06):** fast-path build 0 errors; **447 Core + 79 Api tests green**; signed Android **APK + AAB** produced (34 MB each, `net.frostaura.trimix-Signed.{apk,aab}`); backend migrate-from-zero applied all 5 migrations on a clean Postgres 17 and `/api/v1/healthz` returned `{"status":"ok","db":"ok"}` (HTTP 200). All binding guardrails re-verified (see the bottom "Guardrail re-verification" section).

Companion to [`docs/design/aggregator-architecture.md`](design/aggregator-architecture.md) §9 (Production wiring) and [`docs/known-gaps.md`](known-gaps.md). This file answers one question per production surface: **what is now REAL code that runs**, versus **what remains a deploy/runtime input** the operator must supply — and, for each input, the **exact config key or provisioning step**.

The honest boundary (do not blur it): *plumbing* is code and is built here; *secrets and unpublished protocol facts* — reverse-eng endpoint VALUES, undocumented BLE GATT UUIDs / command bytes, store credentials, the passkey relying-party domain, and physical dive-computer hardware — are DEPLOY/RUNTIME inputs. Every one below is config-injectable, defaults inert/dark, and is labelled, never guessed.

Baseline at authorship: **371 Core + 61 Api tests green, 0 errors**; signed Android APK builds. Slice work only grows those counts — **final release gate: 447 Core + 79 Api green, signed APK + AAB.**

---

## Legend

- **REAL** — code exists, compiles, is unit-tested, and executes its real behaviour at runtime with no further input.
- **REAL (needs data)** — code path is real and runs; it produces nothing useful until an operator supplies the marked config value/asset (which has an inert default).
- **RUNTIME-GATED** — code is compiled and correct but cannot execute its effect until a platform/account/hardware precondition external to this repo exists.

---

## Surface 1 — Diver-controlled file export

| Item | State | Deploy/runtime input |
|---|---|---|
| UDDF / DL7 / PDF byte rendering (`UddfDiveExporter`, `Dl7DiveExporter`, `MinimalPdfDiveExporter`) | REAL (fixture-tested) | — |
| `FileExportDestinationConnector` (renders + hands to sink) | REAL | — |
| **`IExportFileSink` → real platform sink** (`MauiExportFileSink`: iOS `UIActivityViewController`, Android system share/save sheet via `Share.Default.RequestAsync`) | REAL after slice 1 | — (works at runtime; no input) |
| Android true SAF `ACTION_CREATE_DOCUMENT` "save to chosen folder" | REAL (override hook) | Optional: `MainActivity.OnActivityResult` forward — labelled TODO in the Android partial; `Share` sheet is the shipping default |
| Export entry point (DiveDetail → format picker → run connector) | REAL after slice 1 | — |

Before slice 1, `IExportFileSink` binds only to `InMemoryExportFileSink` (a `TryAdd` dev fallback) — exported bytes never leave the device. Slice 1 closes this. No secret/deploy value is required for export to work.

---

## Surface 2 — Connector activation (aggregator "Connected Accounts")

| Item | State | Deploy/runtime input |
|---|---|---|
| Connector registry + descriptors (`IDiveSourceConnector`/`IDiveDestinationConnector`, `LegalPosture`, `EnabledByDefault`) | REAL | — |
| Consent ledger + token store (`PreferencesConnectorConsentStore`, `SecureStoreTokenStore`); revoke severs consent **and** exchanged token | REAL | — |
| Gated credential→token exchange, password never persisted (`GatedCloudSourceConnector.ProvideCredentials`, `GatedPushConnectorBase.SignInAsync`) | REAL (offline-tested) | — |
| **Activation UI** (Connected Accounts page/VM, legal badges, disclosure gate, credential sheet, disconnect, SSI QR render) | REAL after slice 2 | — |
| SSI QR **matrix render** (`BasicQrEncoder` → `QrCodeDrawable`) | REAL after slice 2 | Optional swap to a vetted QR NuGet (integrator `.csproj` add) behind the `IQrEncoder` port |
| **PADI push endpoints** (`PadiPushOptions.AuthUrl`/`PushUrl`) | REAL (needs data) — connector ships DARK, reports `Unsupported`/`NeedsInteraction` until set | Config keys `Connectors:Padi:AuthUrl`, `Connectors:Padi:PushUrl` (default null). Reverse-eng values + legal sign-off are the deploy input; **never inline a guessed URL** |
| **SSI push endpoints** (`SsiPushOptions.AuthUrl`/`PushUrl`) | REAL (needs data) — DARK | `Connectors:Ssi:AuthUrl`, `Connectors:Ssi:PushUrl` (default null) |
| **Cloud pull endpoints** (Garmin / Suunto / Oceanic) | REAL (needs data) — DARK, `EnabledByDefault=false`, zero I/O before consent | After slice 2: `CloudSourceOptions.{Garmin,Suunto,Oceanic}*Url` (default null ⇒ falls back to the in-source reverse-eng consts, which for Oceanic are explicitly placeholder). Live values + legal sign-off are the deploy input |
| Reverse-eng connector legality | RUNTIME-GATED | Per-user opt-in + disclosure (built) **and** recorded legal/BD sign-off before the `EnabledByDefault` flag is flipped for a listing — founder override, `known-gaps.md` |

Guardrails that must stay intact and are test-pinned: every `ToSRiskReverseEng` connector `EnabledByDefault=false`; zero network I/O before `IConnectorConsentStore.HasConsent`; only the exchanged token is persisted (never the password); truthful labels ("personal export", never "official <Vendor>").

---

## Surface 3 — BLE dive-computer import

| Item | State | Deploy/runtime input |
|---|---|---|
| Six clean-room protocol parsers (Shearwater + Suunto/Mares/ScubaPro/Aqualung; Garmin = `NotSupported`) | REAL (fixture-tested) | — |
| GATT pipeline — scan → connect → discover service → subscribe notify → surface frames (Android `Android.Bluetooth.LE`, iOS CoreBluetooth) | REAL (compile-verified) | Physical device + real dive computer (the iOS Simulator has no radio) |
| **Vendor identification by advertised service-UUID + name** (`BleGattProfiles.TryIdentify`) | REAL after slice 3 | — |
| **Write-side handshake / download command** (write char resolved by GATT property or profile UUID, then command bytes written to start the download) | REAL pipeline after slice 3 | Per-vendor `HandshakeCommands` bytes. Shearwater's exact PC-protocol init sequence must be transcribed from libdivecomputer `shearwater_common.c` and hardware-validated — **labelled TODO / config override**, never guessed |
| Shearwater BLE **service UUID** `fe25c237-0ece-443c-b0aa-e02033e7029d` | REAL (public, in-repo, libdivecomputer) | — |
| Shearwater **characteristic UUIDs** | REAL via property-based resolution | Optional pinned override `Ble:Shearwater:{WriteCharUuid,NotifyCharUuid}`; the Subsurface `27b7570a/b-359e-45a3-91bb-cf7e70049bd2` pair is the *documented candidate* — verify against current libdivecomputer before pinning |
| Real Shearwater **log-format** decode (Manifest + block download) mapped onto a real parser | RUNTIME-GATED (M3) | Physical Perdix + field validation. The shipped decoder is the fixture-frame contract; the real log parser is hardware-gated M3 work, unchanged by slice 3 |
| Other vendors' GATT profiles (Suunto/Mares/ScubaPro/Aqualung) | REAL pipeline + override hooks | `Ble:<Vendor>:{ServiceUuid,WriteCharUuid,NotifyCharUuid,HandshakeCommands}` (default empty ⇒ name-hint scan only) |

The M3 kill-line (≥2 of 4 vendors reliable over BLE in real field conditions, ≥40% import success) is **inherited, not closed**, by this slice. Slice 3 makes the scan→identify→handshake→notify pipeline real and vendor-parameterised; it does not manufacture hardware validation.

---

## Surface 4 — Client config + privacy

| Item | State | Deploy/runtime input |
|---|---|---|
| **External config binding** (`TrimixClientConfig` ← `Resources/Raw/appsettings.json` via `AppConfigLoader`) for API base URL, connector endpoints, feature flags, BLE UUID overrides | REAL after slice 4 | Operator edits `appsettings.json` (asset ships with dev defaults); no secret required to boot |
| API base URL | REAL (needs data for prod) | `Api:BaseUrl` (Release default `https://api.trimix.frostaura.net`; blocked on name clearance) |
| GDPR **export** (serializes local-first dive store incl. cylinders + structured conditions) | REAL | — |
| GDPR **delete** (tombstone → sync erase server-side → sign-out wipe) | REAL | Full account/analytics erasure endpoint exists server-side (`PrivacyEndpoints`); client path real |
| Local-dive purge on sign-out/account-switch (`SyncService` on `AuthStateChanged`) | REAL | — |
| **Connector consent + exchanged-token purge on sign-out/delete** (`ConnectorPurgeService`) | REAL after slice 4 — closes the gap where a live Garmin/Suunto/Oceanic/SSI/PADI token survived sign-out (and, on iOS Keychain, uninstall) | — |
| Cylinders + structured conditions in export/delete | REAL (carried in `DiveDto`; cleared with dives, included in export JSON) | — |

---

## Surface 5 — Server receipt validation (Apple App Store + Google Play)

The two post-slice `NotConfigured` stubs (`app_store`, `google_play`) are replaced by real vendor-API verifiers. Both are **REAL (needs data)**: the code runs, calls the real vendor endpoint through an injectable `HttpClient`, and maps the response to `Valid`/`Invalid`/`NotConfigured` — but every verifier **ships DARK** (empty config ⇒ `NotConfigured` ⇒ the endpoint returns 501, exactly as today) until an operator supplies the store credentials below. Real end-to-end validation additionally needs a real device receipt, which is name-clearance / listing gated (Surface "Store billing" cross-ref).

Method choice is dictated by **how the client actually sends the token** (do not change without changing the client):

- **Apple** — the iOS client (`StoreKitBillingService`) is StoreKit **1** and posts the base64 **`AppStoreReceiptUrl` bundle receipt blob**. The matching server method is Apple's classic **`verifyReceipt`** (shared-secret, JSON) with the **production → sandbox (status `21007`)** fallback — *not* the App Store Server API, which requires a `transactionId` the StoreKit-1 client never sends. If the client is ever migrated to StoreKit 2 (signed-transaction JWS), switch the verifier to the App Store Server API (ES256 JWT auth + x5c JWS verification); tracked as a forward note, not this slice.
- **Google** — the Android client (`PlayBillingService`) posts the Play **purchase token**. The matching server method is a service-account **OAuth2** token exchange → **`purchases.products.get`** on the Android Publisher API; `purchaseState == 0` (Purchased) ⇒ `Valid`.

| Item | State | Deploy/runtime input |
|---|---|---|
| Async `IReceiptVerifier.VerifyAsync` seam; sandbox verifier + `POST /purchases/validate` migrated | REAL | — |
| **Apple** `verifyReceipt` verifier (prod→sandbox `21007` fallback; verifies `status==0`, `bundle_id`, and requested `product_id` ∈ `in_app`) | REAL (needs data) — DARK ⇒ `NotConfigured` until secret set; preserves the dev/sandbox HMAC reconciliation path when unset | Config keys below |
| **Google** `purchases.products.get` verifier (SA JWT → OAuth2 access token, cached; `purchaseState==0` ⇒ Valid) | REAL (needs data) — DARK ⇒ `NotConfigured` until SA + package set | Config keys below |
| All vendor HTTP through injectable `HttpClient` (named clients via `IHttpClientFactory`) | REAL | — (unit-tested against a mocked handler + recorded vendor JSON; **no real network in tests or CI**) |

### Deploy inputs — Apple

| Config key | Default | Provisioning step |
|---|---|---|
| `Billing:AppStore:SharedSecret` | *(empty ⇒ NotConfigured)* | App Store Connect → **Apps → (app) → App Information → App-Specific Shared Secret** (or Account → Shared Secret for the primary). Injected as a deploy secret; never committed. |
| `Billing:AppStore:BundleId` | *(empty)* | The app bundle id (`net.frostaura.trimix`). The verifier rejects any receipt whose `receipt.bundle_id` differs. |
| `Billing:AppStore:ProductionUrl` | `https://buy.itunes.apple.com/verifyReceipt` | Optional override (proxy/testing only); the canonical Apple endpoint otherwise. |
| `Billing:AppStore:SandboxUrl` | `https://sandbox.itunes.apple.com/verifyReceipt` | Optional override; the `21007` fallback target. |

### Deploy inputs — Google

| Config key | Default | Provisioning step |
|---|---|---|
| `Billing:GooglePlay:PackageName` | *(empty ⇒ NotConfigured)* | The Android application id (`net.frostaura.trimix`). |
| `Billing:GooglePlay:ServiceAccountJsonPath` | *(empty)* | Path to a Google Cloud **service-account JSON key**. Create the SA in Google Cloud, then in **Play Console → Users & permissions** grant it the account/app permission that includes **View financial data / Manage orders and subscriptions**; download the JSON key. Preferred in deploy (mounted secret file). |
| `Billing:GooglePlay:ServiceAccountJson` | *(empty)* | Inline alternative to the path (for env-var / secret-store injection). Verifier prefers inline when both are set, else falls back to the path; **both empty ⇒ NotConfigured**. |
| `Billing:GooglePlay:AndroidPublisherBaseUrl` | `https://androidpublisher.googleapis.com` | Optional override (proxy/testing only). |
| `Billing:GooglePlay:TokenUrl` | *(SA `token_uri`, else `https://oauth2.googleapis.com/token`)* | Optional override for the OAuth2 token endpoint. |

Contract that must stay test-pinned: **empty config ⇒ `NotConfigured` ⇒ 501** (regression guard `EntitlementAuthorityTests.Store_platforms_are_explicit_not_configured_stubs`); the server remains the only entitlement authority; a receipt that fails verification grants nothing; both SKUs are non-consumables (no expiry, no auto-renew).

---

## Pre-existing runtime-gated surfaces (cross-reference, not slice work)

These remain gated independent of the four slices — see [`known-gaps.md`](known-gaps.md):

| Surface | Gate |
|---|---|
| Store billing (Play / StoreKit) | App Store Connect / Play listing (blocked on **name clearance**) or a local `.storekit` config; **server-side** Apple/Google verification is now REAL (needs data) — see Surface 5 for the credential deploy inputs. `SandboxBillingService` is the exercised client path today |
| Passkey (WebAuthn) sign-in | Real HTTPS relying-party domain + `Passkey:RelyingPartyId`/origins, hosted `.well-known/{apple-app-site-association,assetlinks.json}`, iOS `associated-domains` entitlement + Android Digital Asset Links, paid signing |
| Apple HealthKit source | Physical iOS device + Apple Watch Ultra/Oceanic+ writing `underwaterDiving`; provisioning profile with HealthKit capability |
| API host `api.trimix.frostaura.net` | DNS + TLS + deploy of `Trimix.Api` behind name clearance |
| Name clearance ("Trimix") | Store + `.com`/`.app` + USPTO (Class 9/42) — blocking gate before any listing/domain/trademark, transitively gates billing + API host + passkey RP |

---

## Guardrail re-verification (release gate 2026-07-06)

Every binding rule was re-checked against the shipped code, not just the tests:

- **Reverse-eng connectors consent-gated, token-only, dark-by-default, truthful labels** — `GatedPushConnectorBase.SignInAsync` uses the raw password once for an ephemeral exchange and persists **only** the exchanged token via `ISecureTokenStore`; `AuthenticateAsync`/`PushDiveAsync` do zero network I/O before `IConnectorConsentStore.HasConsent`. Cloud + push families ship dark and are additionally gated by the deploy master switches.
- **Feature master switches are LIVE consumers (residual resolved).** `Features.EnableCloudConnectors` / `Features.EnablePushConnectors` flow `appsettings.json` → `ClientConfigBinder` → `TrimixClientConfig.Features` → `MauiProgram` maps onto `TrimixCoreOptions` (`MauiProgram.cs` L183–184) → `AddTrimixCore` conditionally registers the connector families (`ServiceCollectionExtensions.cs` L195 cloud / L225 push). With a flag OFF (ship default) the family is **not registered at all**. Pinned by `AddTrimixCoreTests` (`EnableCloudConnectors_flag_registers_the_three_reverse_eng_cloud_sources`, `EnablePushConnectors_flag_registers_the_two_reverse_eng_push_destinations`). These flags are **not** inert.
- **Privacy purge is wired at startup.** `ConnectorPurgeService` is registered *and* force-resolved once at boot (`MauiProgram.cs` L230 + L240), so its `AuthStateChanged` subscription is guaranteed live; on sign-out/GDPR-delete it calls `RevokeAsync` per connector (clears consent + token under every `ConnectorTokenKeys.AllFor` key shape).
- **BLE — only Shearwater's service UUID is presented as fact** (`fe25c237-0ece-443c-b0aa-e02033e7029d`, public via libdivecomputer/Subsurface). Suunto/Garmin/Mares/Oceanic are name-hint-only with null service UUID; their characteristics resolve by GATT property and their handshake bytes are labelled deploy inputs (`Ble:<Vendor>:HandshakeCommandsHex`) — never guessed.
- **Server receipt validation fails closed, no hard-coded secrets.** Apple `verifyReceipt` (prod→sandbox 21007 fallback, bundle + SKU check) and Google `purchases.products.get` (SA JWT→OAuth2, `purchaseState==0`) map every transport/parse/ambiguous outcome to `Invalid`, and return `NotConfigured`→501 when creds are absent. Production `appsettings.json` ships empty billing secrets, empty connection string, and no JWT secret (dev-only values live in `appsettings.Development.json`).
- **Free-tier cap counts RAW dives** (`EntitlementGate` over `FreeTier.FreeImportDivesPerSession` / `FreeLogbookVisibleDives`); entitlement authority is server-side; no deco computation anywhere; frozen funnel taxonomy is additive-only.
