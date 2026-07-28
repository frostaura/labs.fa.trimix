# Trimix — Connector (Aggregator) Architecture

**Status:** Authoritative connector-layer contract, rewritten 2026-07-27 for the serverless / API-sync-only pivot (PRD Amendments set 3) · **Owner:** Solutions architect (fa.trimix)

Binding spec source: `docs/prd.md` Amendments set 3, which supersedes set 2 where they conflict. Earlier versions of this file described a multi-source hub (BLE + HealthKit + file import + cloud) over a client–server backend with cross-device sync and a moderated site DB; **all of that except the cloud-connector corridor is deleted**. On visual/design-language concerns, `mockup-spec.md` wins.

---

## 0. The honest posture (read first)

The connector-landscape research (2026-07-05) stands: **no dive-computer vendor publishes a usable public cloud API that returns a dive profile**, and PADI/SSI expose no logbook-write API. Garmin and Suunto have enterprise-gated partner APIs — documented here as a **future swap target**, not something this program has access to. Every shipping integration is therefore an **on-device reverse-engineered private-cloud client** operating with the diver's own credentials — the ToS/CFAA-gray posture knowingly accepted by the founder legal override (2026-07-05, recorded in `MEMORY.md` and `known-gaps.md`).

Post-pivot, this is no longer one channel among several: **API sync is the only way a dive enters the app.** BLE, file import/export, manual entry, and Apple HealthKit were all removed 2026-07-27.

Three guardrails stand regardless of the override, because they protect users rather than the ToS posture:

1. **Never persist a vendor password in plaintext** — ephemeral credential→token exchange; only the exchanged token persists, in iOS Keychain / Android Keystore (`ISecureTokenStore`).
2. **Per-user opt-in behind a plain-language ToS-risk disclosure**, shown before consent. Zero network I/O before `IConnectorConsentStore.HasConsent`.
3. **Truthful labelling** — "user-assisted personal export", never "official <Vendor> integration".

**Gating changed shape in the pivot, not strength:** connectors are now *registered and visible by default* (the Providers screen lists all seven), but availability comes from the per-user consent store, not from flipping a flag. Every `ToSRiskReverseEng` descriptor still ships `EnabledByDefault=false`. Connectors are **fixture-validated only** — their endpoint values are researched guesses, and they are never exercised against real vendor production servers from this environment.

---

## 1. The seam: everything converges on `ImportedDive`

- **Canonical dive:** `DiveDto` + `ProfileSampleDto` + `CylinderDto` — `src/shared/Trimix.Contracts/Dtos/`.
- **Seam boundary DTO:** `ImportedDive`. Every source connector's job is to produce `ImportedDive`s; from there `ImportService` runs unchanged: site auto-match (bundled atlas) → free-tier truncation → `ToDiveDto` → local upsert → `dive_logged_import` → paywall.
- **Deterministic dive identity:** `ImportService.DeterministicDiveId(vendor, startUtc, durationSec)` — per-source idempotent, so re-import after upgrade upserts onto the same rows. Cross-source duplicates are collapsed **at display time** by `Stats/DiveMergePolicy` (presentation union, richest-fidelity-per-field wins; rows never destructively merged).

## 2. The two ports (unchanged contract)

`src/app/Trimix.Core/Abstractions/IDiveSourceConnector.cs`:

```csharp
public interface IDiveSourceConnector
{
    ConnectorDescriptor Descriptor { get; }
    Task<ConnectorAuthResult> AuthenticateAsync(CancellationToken ct = default);
    Task<IReadOnlyList<DiveRef>> ListDivesSinceAsync(SyncCursor cursor, CancellationToken ct = default);
    Task<ImportedDive> FetchProfileAsync(DiveRef diveRef, CancellationToken ct = default);
}

public interface IDiveDestinationConnector
{
    ConnectorDescriptor Descriptor { get; }
    Task<ConnectorAuthResult> AuthenticateAsync(CancellationToken ct = default);
    Task<PushResult> PushDiveAsync(DiveDto dive, CancellationToken ct = default);
}

public sealed record ConnectorDescriptor(
    string ConnectorId, ConnectorKind Kind, LegalPosture Legal, ProfileFidelity Fidelity,
    AuthModel Auth, Platforms Platforms, bool EnabledByDefault);
```

`LegalPosture` (`CleanOfficial | OwnedHardware | UserFile | PartnerRequired | ToSRiskReverseEng`), `ProfileFidelity`, `AuthModel`, `SyncCursor`, `DiveRef`, `PushResult` are unchanged from the original contract — **legal gating remains structural**: UI badges, the disclosure gate and the ships-dark rule are driven from the descriptor. The `ConnectorKind` enum retains its historical members (`BleDevice`, `HealthStore`, `FileImport`, `FileExport`) even though only `CloudApi` connectors ship — enum values, like funnel event names, are not deleted.

## 3. The seven shipping connectors

| Connector | Direction | Class | Cloud it talks to | Notes |
|---|---|---|---|---|
| Garmin Dive | IN | `GarminCloudSourceConnector` | Garmin Connect (garth-style SSO ticket flow) | Dive payloads are FIT; decoded by the shared internal `IFitActivityParser` |
| Suunto | IN | `SuuntoCloudSourceConnector` | Suunto private cloud | FIT decode via the same shared parser |
| ScubaPro | IN | `ScubaProCloudSourceConnector` | **MySSI cloud** (shared corridor) | ScubaPro has no cloud of its own |
| Mares | IN | `MaresCloudSourceConnector` | **MySSI cloud** (shared corridor) | Same |
| Oceanic+ | IN | `OceanicCloudSourceConnector` | Oceanic+ token cloud | Endpoint constants explicitly placeholder-grade |
| PADI | OUT | `PadiPushDestinationConnector` | PADI private sync | Renders DAN DL7 internally (`Push/Dl7/Dl7DiveExporter`) — a payload codec, not a user-facing file export |
| SSI | OUT | `SsiPushDestinationConnector` | **MySSI cloud** (shared corridor) | Plus SSI QR summary (`SsiQrPayloadBuilder` + `BasicQrEncoder`) |

**The MySSI corridor:** Mares, ScubaPro and SSI all route through one shared reverse-engineered client, `Connectors/Infra/MySsiCloudClient.cs` — three connectors, one protocol surface. A breakage or a vendor-side change there takes out three of seven integrations at once; conversely, validating it live validates three.

Common infrastructure (`Connectors/Infra/`): `IHttpTransport`/`HttpClientTransport` (fixture-recordable), `ISecureTokenStore`/`SecureStoreTokenStore`, `IConnectorConsentStore`/`PreferencesConnectorConsentStore`, `ConnectorTokenKeys`. Credential flow: `GatedCloudSourceConnector.ProvideCredentials` / `GatedPushConnectorBase.SignInAsync` use the raw password once for the exchange and never store it. `ConnectorPurgeService` revokes consent + tokens for every connector when identity is reset. Display metadata (names, domains, marquee labels) lives in `Connectors/ProviderCatalog.cs`.

## 4. `SyncCoordinator` — the "Sync now" orchestrator

`DiveComputer/SyncCoordinator.cs` is the production caller of the import pipeline:

1. Enumerate every registered `IDiveSourceConnector`; skip any without consent (zero I/O) or without `Granted` auth.
2. For each remaining source, run `ImportService` incrementally from the persisted per-connector `SyncCursor` (stored in `ISecureTokenStore` under `ConnectorTokenKeys.SyncCursor`, cleared on disconnect).
3. Collect newly-imported dives (deduplicated by deterministic id), then push them to every consented destination connector.

**Cursor discipline:** the cursor only advances on a fully-imported run. When the free-tier cap truncates a fetch, it stays put, so the dives beyond the cap are re-fetched after upgrade — the paywall moment (`paywall_shown`, trigger `cloud_sync_cap`) loses nothing.

**Funnel events** are emitted by the import pipeline, not the coordinator: `source_connected`, `dives_synced`, `dive_logged_import`, `paywall_shown`. They go to the **local analytics sink** — there is no ingest server.

## 5. What was deleted (do not resurrect silently)

- **Server-side everything:** `Trimix.Api`, accounts, cross-device dive sync, server-authoritative entitlements + receipt validation, the shared/moderated site DB, moderation queue, analytics ingest. Sites are now derived from the user's dives + the bundled read-only `SiteAtlas` (14 seed sites, haversine matcher).
- **BLE:** transports, GATT profiles, vendor protocol parsers, the simulator fixture contract, and the M3 "BLE field reliability" framing. The M3 reliability question now applies to **API sync** (≥2 providers importing reliably in real post-dive conditions).
- **File import/export as user features:** FIT/UDDF/DL7/PDF pickers, share-sheet export. The FIT parser and DL7 renderer survive only as internal payload codecs inside the connectors (see §3).
- **Manual dive entry** and **Apple HealthKit**.

The retained-but-no-longer-emitted funnel names (`ble_*`, `dive_logged_manual`, `dives_fetched`, `site_contribution_*`, `account_created`, trigger `ble_import_cap`) remain in the frozen contract — no name was renamed or deleted.

## 6. Future swap: partner APIs

Garmin (Connect Developer Program / Health API) and Suunto (partner API) run enterprise-gated official APIs that currently exclude or strip scuba data for third parties. If partner access is ever granted, the swap is confined by design: a partner-backed connector implements the same `IDiveSourceConnector` port with `LegalPosture.PartnerRequired → CleanOfficial`, and nothing downstream of `ImportedDive` changes. This is the documented exit from the reverse-engineered posture — an aspiration, not a plan of record.
