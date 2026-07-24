# Trimix — Cloud-Aggregator Architecture

**Status:** Authoritative architecture contract for the aggregator pivot · **Date:** 2026-07-05 · **Owner:** Solutions architect (fa.trimix)

Binding spec source: `docs/prd.md` **Amendments set 2** (2026-07-05). Where this document and the older `docs/design/sonar-design-language.md` / `mockup-spec.md` conflict on data-acquisition or data-model concerns, **this document wins**; on visual/design-language concerns, `mockup-spec.md` wins. This file governs the *what* and the *seams*; connector internals (real BLE protocols, HealthKit queries, cloud clients, file parsers) are specified in their own later-phase branches.

---

## 0. The honest reframe (read first)

The pivot is framed as *"stop reading dive computers over BLE; pull dives from the clouds the computers already sync to."* The connector-landscape research (`tasks/wzcdzmbtu.output`, §0–§2) establishes that **the clean cloud layer does not exist in the dive industry**: no vendor publishes a cloud API that returns a dive *profile*; Garmin/Suunto official partner APIs structurally exclude or strip scuba data; every other "cloud pull" is reverse-engineering an auth-gated backend (a ToS/CFAA-gray path that knowingly fails gate-3 "integrated legally").

Therefore the target is **not** "cloud aggregator replaces BLE." It is a **multi-source normalization hub** whose legally-clean inputs are:

1. **Apple HealthKit** (iOS, on-device consent, depth+temp only) — the only first-party clean aggregation surface.
2. **User-supplied file import** (FIT / UDDF / DL7 / Suunto-JSON) — the diver moves their own data; the only clean Garmin path; a universal fallback for every closed vendor.
3. **BLE-direct from owned hardware** (Shearwater, Suunto, ScubaPro, Mares, Aqualung) — **retained, not deleted.** Reading the diver's own computer touches no vendor cloud or cloud ToS.

Vendor **cloud** connectors and cert-agency **push** (PADI/SSI) are relegated to opt-in-with-warning stubs on a BD/legal track. Per the **founder legal override (2026-07-05)** recorded in `docs/known-gaps.md`, reverse-engineered connectors *may* later ship as real features, but only gated: no plaintext vendor passwords (token in secure storage), per-user opt-in + disclosure, truthful labeling, legal sign-off before the flag flips. The architecture below makes that gating **structural** via `LegalPosture` + `EnabledByDefault` on every connector descriptor.

Corollary the pivot does **not** dissolve: the M3 kill-line (≥2 of 4 vendors reliable over BLE in real field conditions, ≥40% import success) is *inherited* by the aggregator, not removed. Adding cleaner sources alongside BLE makes gate-3's *legal* half easier; it does nothing for the *field-reliability* half.

---

## 1. Principle: keep the convergence point; BLE becomes one connector kind

The repo already has a clean seam and it does **not** change:

- **Canonical dive:** `DiveDto` + `ProfileSampleDto` — `src/shared/Trimix.Contracts/Dtos/DiveDtos.cs`.
- **Seam boundary DTO:** `ImportedDive` — `src/app/Trimix.Core/Abstractions/IDiveComputerProtocol.cs`.
- **Normalization + downstream:** `ImportService` — `src/app/Trimix.Core/DiveComputer/ImportService.cs`. Everything downstream of the line where it holds `IReadOnlyList<ImportedDive> fetched` (site auto-match → free-tier truncation → `ToDiveDto` → `store.UpsertDiveAsync` → `dive_logged_import` → `ImportResult`/paywall) is **already source-agnostic and is reused verbatim.**

The re-architecture is therefore **additive above the seam**. Every source connector's job is to *produce `ImportedDive`s*; from there the existing pipeline runs unchanged. The BLE transport/protocol abstraction (`IDiveComputerTransport` + `IDiveComputerProtocol`, vendor enum, `ShearwaterProtocol`, the three `NotSupported` placeholders) keeps its signatures and is **demoted to one `ConnectorKind`**, wrapped by `BleDeviceSourceConnector`.

---

## 2. The two ports (hexagonal)

New ports live in `src/app/Trimix.Core/Abstractions/`.

```csharp
public interface IDiveSourceConnector
{
    ConnectorDescriptor Descriptor { get; }
    Task<ConnectorAuthResult> AuthenticateAsync(CancellationToken ct = default);
    Task<IReadOnlyList<DiveRef>> ListDivesSinceAsync(SyncCursor cursor, CancellationToken ct = default);
    Task<ImportedDive> FetchProfileAsync(DiveRef diveRef, CancellationToken ct = default); // emits the EXISTING ImportedDive
}

public interface IDiveDestinationConnector
{
    ConnectorDescriptor Descriptor { get; }
    Task<ConnectorAuthResult> AuthenticateAsync(CancellationToken ct = default);
    Task<PushResult> PushDiveAsync(DiveDto dive, CancellationToken ct = default); // consumes the canonical DTO
}
```

`ImportedDive` (unchanged shape plus the new `Cylinders`, see §4) is the hand-off contract every source connector produces. A cloud / HealthKit / file connector maps its structured data **straight into `ImportedDive`**, skipping the byte-frame machinery — do **not** force a fake `IDiveComputerTransport`/`IDiveComputerProtocol` for a JSON/HealthKit source; the `ImportedDive` boundary is the intended seam.

### Descriptor + supporting types

```csharp
public sealed record ConnectorDescriptor(
    string ConnectorId,          // "ble_device", "apple_health", "garmin_fit", ...
    ConnectorKind Kind,          // BleDevice | HealthStore | FileImport | CloudApi
    LegalPosture Legal,          // CleanOfficial | OwnedHardware | UserFile | PartnerRequired | ToSRiskReverseEng
    ProfileFidelity Fidelity,    // FullProfile | DepthTempOnly | SummaryOnly
    AuthModel Auth,              // None | OnDeviceConsent | OAuth2 | UserCredential
    Platforms Platforms,         // Android | iOS | Both
    bool EnabledByDefault);      // false for every ToSRiskReverseEng connector (ships dark)

public enum ConnectorKind { BleDevice, HealthStore, FileImport, CloudApi }
public enum LegalPosture { CleanOfficial, OwnedHardware, UserFile, PartnerRequired, ToSRiskReverseEng }
public enum ProfileFidelity { FullProfile, DepthTempOnly, SummaryOnly }
public enum AuthModel { None, OnDeviceConsent, OAuth2, UserCredential }
[Flags] public enum Platforms { None = 0, Android = 1, iOS = 2, Both = Android | iOS }

public enum ConnectorAuthStatus { Granted, Denied, NeedsInteraction, Unavailable }
public sealed record ConnectorAuthResult(ConnectorAuthStatus Status, string? Message = null);

public sealed record SyncCursor(DateTime? Since, string? Token = null)
{ public static readonly SyncCursor None = new(null, null); }

public sealed record DiveRef(string ConnectorId, string SourceId, DateTime StartUtc, int DurationSec, string? Label = null);

public enum PushStatus { Pushed, Skipped, Failed, Unsupported }
public sealed record PushResult(PushStatus Status, string? DestinationId = null, string? Message = null);
```

**Legal gating is structural.** UI visibility, warning badges, and the "ships dark" rule are driven from `Descriptor.Legal` + `Descriptor.EnabledByDefault`. Any connector whose `Legal == ToSRiskReverseEng` **must** carry `EnabledByDefault == false` and is enabled only behind explicit per-user opt-in + disclosure + recorded legal sign-off. The foundation phase adds **no** such connector; it only guarantees the descriptor can model them so later reverse-engineered connectors ship gated by construction.

### Registry

Connectors self-register and are resolved as `IEnumerable<IDiveSourceConnector>` / `IEnumerable<IDiveDestinationConnector>`, mirroring today's multi-registration of `IDiveComputerProtocol`. Selection is by descriptor (`Kind` for the BLE UI path, `ConnectorId` for the generic path).

### How the pieces slot in

| Concrete connector | Kind | Wraps / uses | Legal | Fidelity | Platform | Default |
|---|---|---|---|---|---|---|
| `BleDeviceSourceConnector` | BleDevice | existing `IDiveComputerTransport` + `IDiveComputerProtocol` (unchanged) | OwnedHardware | FullProfile | Both | on |
| `HealthStoreSourceConnector` *(later)* | HealthStore | HealthKit bindings (net10.0-ios) | CleanOfficial | DepthTempOnly | iOS | on |
| `FileImportSourceConnector` *(later)* | FileImport | FIT/UDDF/DL7/JSON parsers + picker | UserFile | varies | Both | on |
| `*CloudSourceConnector` *(later, gated)* | CloudApi | OAuth / user-credential HTTP | ToSRiskReverseEng / PartnerRequired | varies | Both | **off** |
| `FileExportDestinationConnector` *(later)* | FileImport | UDDF/DL7/PDF writer | UserFile | — | Both | on |

The foundation ships **only** `BleDeviceSourceConnector`. The rest are named here so their descriptors are pre-decided; their code is later-phase.

---

## 3. Cross-source dive identity (decided)

`ImportService.DeterministicDiveId(vendor, startUtc, durationSec)` currently keys `SHA256(vendor|startUnixSec|durationSec) → GUID`. This is correct for **idempotent re-import from the same source** (re-import after unlocking Pro upserts onto the same ids — a behaviour pinned by `ImportViewModelTests.Reimport_after_upgrade_does_not_duplicate_dives`). But the same physical dive arriving via BLE **and** HealthKit **and** a FIT file produces three distinct ids (three different `vendor`/source values).

**Decision — Option A (keep provenance in the id; merge at the logbook layer):**

1. **`DeterministicDiveId` keeps the source token in the key, unchanged.** Every clean import channel is tagged `Source = "import"` (frozen) with a distinct `SourceVendor` (`shearwater`, `apple_health`, `garmin_fit`, …). This preserves per-source idempotency and provenance and requires **zero** change to upsert semantics in the foundation.
2. **A logbook-layer merge heuristic (later, stats branch) surfaces duplicates as one dive.** Two stored dives are *the same physical dive* when **|ΔStartUtc| ≤ 90 s AND |ΔDurationSec| ≤ 90 s AND max-depth agrees within 1.5 m** (tunable constants, defined once in a `DiveMergePolicy`). Merge is a **presentation-layer union**, not a row rewrite: the logbook shows one entry badged `sources: Shearwater + Apple Watch`, with **richest-fidelity-per-field wins** (a `FullProfile` source's samples/gas/tank beat a `DepthTempOnly` source's; non-null beats null). Underlying rows are retained for provenance and never destructively merged.

The foundation only needs to **record this rule and keep the id source-scoped**; implementing the heuristic is branch 6 (stats). `DiveSources` stays `{manual, import}` — no new source value, so the frozen `AllowedValues[source] = {import}` never grows a sibling.

---

## 4. Data-model extension

The presentation layer is production-grade; the binding constraint on richer stats is the **schema**. Per-sample vocabulary is only `{depth, temp, ppO2(null)}`, gas is one denormalized string, conditions are a free-text blob. Multi-source makes fidelity heterogeneous, which *sharpens* the requirement: the normalized model must carry the rich channels the full-fidelity sources record **and degrade gracefully to null** for depth-temp-only sources. Every aggregate must be null-tolerant.

### 4.1 `ProfileSampleDto` — add nullable channels

`ProfileSampleDto` (`DiveDtos.cs`) gains, all nullable, appended after the existing `PpO2`:
`Ndl`, `CeilingM`, `AscentRateMPerMin`, `Cns`, `TankPressureBar`, `Setpoint`, `HeartRate`.

Server `ProfileSample` entity (`Entities.cs`) gains the matching nullable `double?` columns. Composite PK `(DiveId, TSeconds)` is unchanged.

### 4.2 `CylinderDto` — new, attached to the dive

```csharp
public sealed record CylinderDto(
    double? O2, double? He, double? StartBar, double? EndBar,
    double? VolumeL, double? WorkingPressureBar);
```

Attached to **both** `DiveDto` (`IReadOnlyList<CylinderDto> Cylinders`) and `ImportedDive` (`IReadOnlyList<CylinderDto> Cylinders`). Enables SAC/RMV, multi-gas and gas-switch tracking. The denormalized `GasMix` string **stays** for display. Server gets a new `Cylinder` table (own PK + FK to `Dive`, cascade delete, ordinal index for stable order).

### 4.3 Conditions — structured, aggregatable

Add typed nullable fields to `DiveDto` and the server `Dive` entity: `Visibility`, `Current`, `Weather`, `EntryType`, `WaterType`, `ExposureSuit` (strings), `WeightKg` (`double?`).

**`ConditionsJson` is retained for back-compat** in the foundation (no data migration; the App's `AppConverters` and `ManualEntryViewModel` keep working untouched). Population/read of the typed fields is wired in the stats branch. This keeps the MAUI App compiling with zero App-side edits this phase.

### 4.4 Null-tolerance + completeness

All lifetime/career aggregates are computed only over dives (or samples) that carry the relevant channel — e.g. SAC only over dives with tank pressure. Every stats surface carries a **data-completeness** signal (e.g. "SAC available for 42 of 120 dives") so heterogeneous-fidelity sources don't silently skew aggregates. **Deco stays non-computed**: displaying imported NDL/ceiling/CNS a source *recorded* is data, not computation — the liability line (we never compute tissue loading) is unchanged.

---

## 5. Additive taxonomy delta (zero renames)

The entire money half of the funnel is trigger-agnostic and **does not move**: `paywall_shown` (generic `trigger` + `entitlement_state`), all `purchase_*`, `entitlement_granted`/`entitlement_restored`, the `ServerOnly` set, both SKUs, the receipt→entitlement path. The `ble_*` events and `dives_fetched`/`dive_logged_import` are semantically fine for a multi-source world and are **retained-and-deprecated**, never renamed (renaming a frozen event invalidates buffered offline events and historical dashboards).

Binding change is PRD Amendments set 2 **item 10**. The additive delta (applied identically in client `FunnelEvents.cs` and server `FunnelContract.cs`):

- **Trigger value:** add `Values.TriggerCloudSyncCap = "cloud_sync_cap"` and include it in `AllowedValues[Props.Trigger]`.
- **Events:** add `source_connected` (required props `source`, `platform`) and `dives_synced` (required props `source`, `count`) — the multi-source analogues of `ble_device_connected` / `dives_fetched`.
- **Reuse `dive_logged_import` with `source=import` for every clean import channel** (BLE, HealthKit, file, cloud); differentiate origin via the existing **free-text `vendor` prop** (`apple_health`, `garmin_fit`, `shearwater`, …). No new prop is required (`Props.SourceKind` is **not** added — `vendor` free-text suffices).
- **Never** add a sibling to the frozen `AllowedValues[Props.Source] = {import}`.

**Purchase-moment reframe (item 9):** the gate is *"unlimited multi-source import + cross-device cloud sync."* The multi-source import/sync cap fires `paywall_shown` with `trigger=cloud_sync_cap`; the **BLE-only path keeps `trigger=ble_import_cap`**. Updated spine: `install → first_run_completed → dive_logged_* → dives_synced → paywall_shown(cloud_sync_cap) → purchase_completed → entitlement_granted`.

---

## 6. Build sequencing

**Foundation (this phase) — three contract-level changes, each ending on a green fast-path build + test:**

1. **Connector framework** — the two ports, descriptor + enums, registry, `BleDeviceSourceConnector` wrapping the existing transport+protocol; `ImportService` selects a source connector by descriptor and reuses its whole downstream. Pure refactor; behaviour unchanged; tests stay green. The §3 identity rule is recorded here.
2. **Data-model extension** — `ProfileSampleDto` channels + `CylinderDto` + structured conditions; server entity columns + `Cylinder` table + EF migration (Postgres); server mapping + tests updated.
3. **Additive taxonomy** — `cloud_sync_cap` + `source_connected` + `dives_synced` in both validators; reframe the cap emit; zero renames.

Connectors/parsers themselves are **later phases** and out of scope here: `feat/healthkit-source`, `feat/file-import-sources`, `feat/ble-vendor-expansion` (the real M3 kill-line work), `feat/export-destinations`, `feat/stats-surface`. Reverse-engineered cloud/push connectors stay `stub-pending-legal`: descriptor exists, ships dark (`EnabledByDefault=false`, `Legal=ToSRiskReverseEng`), enabled only on legal/BD sign-off + per-user opt-in.

## 7. Verification (fast path)

The MAUI Android app build is deferred to the release gate. The foundation is verified by:

```
dotnet build src/shared/Trimix.Contracts   # + src/app/Trimix.Core + src/backend/Trimix.Api (one project per invocation)
dotnet test  tests/Trimix.Core.Tests/Trimix.Core.Tests.csproj
dotnet test  tests/Trimix.Api.Tests/Trimix.Api.Tests.csproj
```

Baseline at time of writing: **117 Core + 56 Api = 173 tests green.** The API test host builds its schema via `EnsureCreated` from the model (not migrations), so entity/DbContext changes are exercised by tests automatically; the EF migration is required for the Postgres runtime path and its correctness is **not** covered by the fast-path tests — verify it applies separately.

---

## 8. Stats surface (branch: `feat/stats-surface`)

This section is the authoritative contract for the analytics/UX layer that sits on the extended data-model (§4). It realises the §3 merge heuristic and turns the rich nullable channels into the "beautiful stats" the app is judged on. **Guardrail carried through verbatim:** we *display* imported NDL/ceiling/CNS/ascent that a source **recorded** (it is data); we **never** compute tissue loading or deco ourselves. Everything here is import-and-display or arithmetic over stored values.

The surface is built as **two disjoint code slices + an integration**, so two writers work in parallel without collision:

- **Slice A — chart overlays.** Core fast-path surface: `src/app/Trimix.Core/Charts/` only (additive to `ChartGeometry`; the existing `BuildDepthChart` is frozen byte-for-byte so the 9 `ChartGeometryTests` + `DiveDetailViewModel` keep passing). Release-gate surface: the single file `src/app/Trimix.App/Controls/DepthChartDrawable.cs` (additive draws after the existing depth line/area — **no restyle**).
- **Slice B — career analytics + merge.** A new `src/app/Trimix.Core/Stats/` folder (pure, null-tolerant compute) + new test files. Touches nothing else.
- **Integration.** `LogbookViewModel` / `DiveDetailViewModel` + a new `StatsViewModel`/page, plus one line of DI. Owned by the integrator; the two Build slices do **not** edit ViewModels, `ServiceCollectionExtensions`, or App XAML.

### 8.1 Null-tolerance + completeness contract (both slices)

Every career aggregate returns a value **paired with a data-completeness signal** so a depth-temp-only source (HealthKit) never silently skews a stat that only full-fidelity sources can populate. Canonical types (Slice B, `Stats/Completeness.cs`):

```csharp
public readonly record struct Completeness(int Available, int Total)
{
    public double Fraction => Total <= 0 ? 0 : (double)Available / Total;
    public static readonly Completeness None = new(0, 0);
}
public readonly record struct StatValue<T>(T Value, Completeness Completeness)
{
    public bool HasData => Completeness.Available > 0;
}
```

A metric counts a dive in `Available` only when it carries the channel the metric needs (e.g. SAC needs a cylinder with `StartBar>EndBar` + a usable average depth + `DurationSec>0`). The UI renders the completeness verbatim: *"SAC available for 42 of 120 dives."*

### 8.2 Cross-source merge — `DiveMergePolicy` + `MergedDive` (Slice B, realises §3)

Constants live **once** here; nothing else redefines them.

```csharp
public static class DiveMergePolicy
{
    public const double StartToleranceSeconds    = 90;
    public const double DurationToleranceSeconds  = 90;
    public const double MaxDepthToleranceMeters   = 1.5;

    public static bool IsSamePhysicalDive(DiveDto a, DiveDto b);   // the §3 heuristic (all three within tolerance)
    public static IReadOnlyList<MergedDive> Merge(IReadOnlyList<DiveDto> dives,
        Func<DiveDto, int>? fidelity = null);                      // default = empirical FidelityScore
}
```

Clustering is deterministic and non-destructive: sort by `StartUtc`; each dive joins an existing cluster iff `IsSamePhysicalDive(dive, cluster.Anchor)` (anchor = the cluster's first, highest-fidelity dive), else it opens a new cluster. Underlying rows are **retained** — merge is a presentation union, never a row rewrite.

```csharp
public sealed record MergedDive(
    DiveDto Primary,                          // synthesized union (see rule); Id = highest-fidelity contributor's Id
    IReadOnlyList<DiveDto> Sources,           // provenance, retained
    IReadOnlyList<DiveSourceBadge> Badges);   // ["Shearwater", "Apple Watch"]
public sealed record DiveSourceBadge(string Source, string? Vendor, string Label);
```

**Union rule (richest-fidelity-per-field wins).** For every scalar field: non-null beats null; when both non-null, the value from the higher-`FidelityScore` dive wins. `ProfileSamples` and `Cylinders`: take them wholesale from the highest-fidelity contributor (a `FullProfile` source's samples/gas/tank beat a `DepthTempOnly` source's). `Primary.Id` is set to that same highest-fidelity contributor's real id so dive-detail navigation still resolves. `FidelityScore(DiveDto)` is an **empirical** rank (count of distinct non-null rich sample channels + cylinder presence + gas presence) so it works with zero dependency on the App/connector layer; the optional `fidelity` delegate lets the integrator instead feed `ProfileFidelity` from the connector descriptor. Badge labels come from a small vendor→friendly map (`shearwater`→"Shearwater", `apple_health`→"Apple Watch", `garmin_fit`→"Garmin", `suunto_json`→"Suunto", `manual`→"Manual", null→"Import").

### 8.3 Career analytics — `DiveStats` (Slice B, pure static, null-tolerant)

All over `IReadOnlyList<DiveDto>`; storage stays metric, formatting is the UI's `UnitFormatter` job.

| Method | Returns | Completeness basis |
|---|---|---|
| `Summarize` | `CareerSummary` (total dives, max depth, total bottom time — the 3 the logbook already shows) | all active dives |
| `DepthHistogram(dives, binM=5)` | bins of dive **count** by max-depth band | all dives (max-depth always present) |
| `TimeAtDepth(dives, binM=5)` | seconds **per depth band** summed from samples | dives with ≥1 sample |
| `DivesByMonth` / `PerYear` | count/rollup per calendar month / year | all dives |
| `Deepest` / `Longest` | `StatValue<DiveDto?>` | all dives |
| `AverageDepthM` / `AverageDurationSec` | `StatValue<double>` | dives with the field |
| `TemperatureRange` | `StatValue<TempRange>` (min/max °C) | dives with `MinTempC`/sample temp |
| `GasMixBreakdown` | slices Air / Nitrox / Trimix / Unknown | classified from `Cylinders` (O2/He) first, `GasMix` string fallback |
| `MostDivedSites(top=10)` | `SiteFrequency[]` by `SiteId` | dives with a `SiteId` |
| `SacOverTime` / `SacSummary` | per-dive + mean SAC/RMV | dives with tank pressure + (RMV) cylinder volume |
| `Streaks` | current + longest streak over distinct dive-days | all dives |
| `CalendarHeatmap(year)` | per-day counts (GitHub-style grid) | all dives in year |

**Exact SAC/RMV formula** (`Stats/SacFormula.cs`, `MetersSeaWaterPerBar = 10.0`). Per cylinder with `StartBar`, `EndBar` (both non-null, `StartBar>EndBar`):

```
consumedBar = StartBar − EndBar
Pavg (ATA) = 1 + avgDepthM / 10                      // avgDepthM = DiveDto.AvgDepthM, else mean of sample DepthM
durationMin = DurationSec / 60
SAC (bar/min) =  consumedBar               / (Pavg × durationMin)     // volume-independent
RMV (L/min)   = (consumedBar × VolumeL)    / (Pavg × durationMin)     // needs cylinder VolumeL; L = water-vol × bar
```

SAC is available when `StartBar`,`EndBar`,`DurationSec>0`, and a usable average depth exist; RMV additionally needs `VolumeL`. Deriving `avgDepthM` from the profile samples when `AvgDepthM` is null is arithmetic over recorded depth — **not** deco computation — and is allowed.

### 8.4 Chart overlays — additive `ChartGeometry` primitives (Slice A)

`BuildDepthChart` and `DepthChartPath` are **unchanged**. New, all null-tolerant (a series is emitted only when ≥2 samples carry the channel):

```csharp
// Secondary channel sharing the depth chart's X (elapsed/duration); Y normalized to its OWN [Min,Max] axis.
public sealed record ChartSeries(IReadOnlyList<ChartPoint> Points, double MinValue, double MaxValue,
    string ChannelKey, ChartAxisSide Axis);          // temp | tank_pressure | ndl | hr
public enum ChartAxisSide { Left, Right }

// Shaded region. Ceiling band: Upper = surface (Y=0), Lower = recorded-ceiling depth curve (depth axis) = do-not-ascend zone.
public sealed record ChartBand(IReadOnlyList<ChartPoint> Upper, IReadOnlyList<ChartPoint> Lower, string ChannelKey);

// Point event on the shared time axis.
public sealed record ChartMarker(double X, double Y, ChartMarkerKind Kind, string Label);
public enum ChartMarkerKind { GasSwitch, DecoOnset, AscentWarning, Setpoint }

public sealed record ProfileOverlays(ChartSeries? Temperature, ChartSeries? TankPressure,
    ChartBand? CeilingBand, ChartSeries? Ndl, IReadOnlyList<ChartMarker> Markers)
{ public static readonly ProfileOverlays Empty = new(null, null, null, null, []); public bool HasAny => /* any non-null */; }

public static partial class ChartGeometry
{
    public static ProfileOverlays BuildProfileOverlays(IReadOnlyList<ProfileSampleDto> samples, DepthChartPath depth);
    public static ProfileTable   BuildProfileTable(IReadOnlyList<ProfileSampleDto> samples);   // WCAG alt, columnar
}
```

Markers read **recorded** channels only: `Setpoint`/`GasSwitch` from `Setpoint` transitions between samples; `DecoOnset` at the first sample with recorded `Ndl<=0` **or** `CeilingM>0`; `AscentWarning` where recorded `AscentRateMPerMin` exceeds `ChartThresholds.AscentWarnMPerMin` (=10). None of these compute deco — they surface what the computer stored. (OC per-cylinder switch *times* are not in the per-sample schema, so multi-cylinder OC dives get a single "multi-gas" note rather than per-switch markers; the gas identities still flow through `GasMixBreakdown`.)

**WCAG table extension.** The existing fixed 3-column `ProfileTableRow(Elapsed, Depth, Temperature)` is replaced by a **columnar** `ProfileTable(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows)` where a column appears **only** if ≥1 sample carries that channel — so the accessible alternative grows exactly with the drawn overlays and never shows empty columns for a depth-temp-only dive. Built in Core (pure/testable); the integrator binds the page table to it and formats via `UnitFormatter`.

**Rendering (`DepthChartDrawable`, release-gate).** Add an `Overlays` property and draw, *after* the existing grid/area/line, only the non-null members: ceiling band as a translucent `Warn` fill; temperature + tank-pressure as 1.5px secondary lines on the right axis (`Ice`/`TextSecondary` tokens via `ThemeTokens.Get`); markers as short vertical ticks (mint for `GasSwitch`/`Setpoint`, `Warn` for `DecoOnset`/`AscentWarning`). The existing 2px mint depth line/area/gradient are untouched.

### 8.5 Integration

- **`LogbookViewModel`** — the 3 lifetime aggregates become `DiveStats.Summarize` (same `ObservableProperty` names, mockup S10 strip unchanged). Display uses `DiveMergePolicy.Merge(active)` so cross-source duplicates collapse to one badged entry; **the free-tier cap keeps counting raw active dives** (entitlement semantics + pinned tests unchanged) — merge is display-only.
- **`DiveDetailViewModel`** — `Overlays = ChartGeometry.BuildProfileOverlays(dive.ProfileSamples, Chart)`; `ProfileTable = ChartGeometry.BuildProfileTable(dive.ProfileSamples)`; if the dive is in a merge cluster, surface its `Badges` ("sources: Shearwater + Apple Watch") and any recorded NDL/ceiling/CNS readouts (display-only).
- **`StatsViewModel` + `StatsPage` (new, the "You" surface)** — renders the §8.3 metrics, each card captioned with its completeness fraction; trend / SAC-over-time / histogram / heatmap reuse Slice A geometry patterns (new small drawables are release-gate; the compute is pure Core).
- **DI** — `DiveStats`, `DiveMergePolicy`, `ChartGeometry`, `SacFormula` are static → **no DI**. The only registration is `services.AddTransient<StatsViewModel>();`; the `StatsPage` route is added in `AppShell` (release-gate).
- **Funnel events — none.** Stats is read-only over the local store and is not a purchase moment; the frozen taxonomy (§5) does not grow. If a stat card ever gates behind Pro it reuses the existing `paywall_shown` with an existing trigger — no new event, no new prop.

### 8.6 Verification (fast path, per slice)

Each Build slice self-verifies green before hand-off: `dotnet build src/app/Trimix.Core` + `dotnet test tests/Trimix.Core.Tests/Trimix.Core.Tests.csproj`. Test counts only grow (264 Core + 59 Api baseline). Slice A adds `ChartGeometryOverlayTests` / `ProfileTableTests`; Slice B adds `DiveMergePolicyTests` / `DiveStatsTests` / `SacFormulaTests` — distinct new files, so the shared test project globs both in with no `.csproj` edit and no cross-slice file collision. The `DepthChartDrawable` render + new stats drawables + `StatsPage` XAML are the MAUI release-gate, not the fast path.

---

## 9. Production wiring (four parallel slices) — makes the app production-real

**Status:** Production-hardening phase, 2026-07-06. Baseline **371 Core + 61 Api green, 0 errors; signed Android APK builds.** These four slices turn the aggregator from "compiles + fixture-tested" into "runs against the real OS/BLE/config", staying inside the seams above. The real→runtime-input boundary per surface is tabulated in [`../production-readiness.md`](../production-readiness.md).

**Rules for all four writers (non-negotiable):**
- **Disjoint files.** Each slice owns the files listed under it and edits nothing another slice owns. Shared touchpoints — `ServiceCollectionExtensions.cs`, `MauiProgram.cs`, `AppShell.xaml` / `AppShell.xaml.cs`, `App.xaml.cs`, `*.csproj`, `CoreConstants.cs` — are **integrator-owned**: writers do **not** edit them; they emit the exact lines to add under "Reported wiring".
- **Hexagonal.** Core (`net10.0`) holds ports + testable logic only. Platform effects (Android SAF / iOS share sheet / CoreBluetooth / Android BLE / `FileSystem` / `Share`) live in `src/app/Trimix.App/**` behind `#if`.
- **Guardrails stay intact (all test-pinned):** reverse-eng connectors stay consent-gated + token-only (never persist a plaintext password) + truthfully labelled; frozen funnel taxonomy is additive-only; the free-tier cap counts RAW dives; **no deco/tissue computation** — displaying a *recorded* NDL/ceiling/CNS is data, not computation.
- **Never guess a secret as verified.** Reverse-eng endpoint values, undocumented BLE UUIDs/command bytes, store creds, the passkey RP domain, and hardware are config-injectable deploy inputs with inert defaults and a labelled TODO.
- **Self-verify before returning:** fast path = `dotnet build` of `Trimix.Contracts` → `Trimix.Core` → `Trimix.Api` (one project per invocation) + `dotnet test` both test projects; test counts only grow. If you touched `Trimix.App/**`, also run the Release APK build (recipe in the intro of this section's PR / `known-gaps.md`).

Route + option-instance registrations use the pattern already in `MauiProgram`: **App registers real impls BEFORE `AddTrimixCore`**, so Core's `TryAdd` fallbacks never engage; `AddSingleton` (not `TryAdd`) wins for the real impl.

---

### 9.1 Slice 1 — real `IExportFileSink` + export entry point

**Goal.** Bind `IExportFileSink` to a real platform sink so an exported UDDF/DL7/PDF leaves the device, and add the UI that runs an export connector and hands the file to the OS. Today Core `TryAdd`s only `InMemoryExportFileSink` (`ServiceCollectionExtensions.cs` line ~187) — the gap.

**Owned files (write + edit):**
- `src/app/Trimix.App/Services/MauiExportFileSink.cs` **(new, App)** — `IExportFileSink`. `WriteAsync(ExportedDive)`: write `file.Content` bytes to `Path.Combine(FileSystem.CacheDirectory, file.FileName)`, then hand to the OS via `Share.Default.RequestAsync(new ShareFileRequest { Title = "Trimix dive export", File = new ShareFile(path, file.ContentType) })` — this is the iOS `UIActivityViewController` and the Android system share/save sheet (same proven pattern as `SettingsPage.xaml.cs OnExportClicked`). Return the cache path. Add a `partial Task<string> SaveViaSafAsync(...)` **override hook** for Android true `ACTION_CREATE_DOCUMENT`; leave a labelled TODO noting it needs a `MainActivity.OnActivityResult` forward — do **not** wire that unless in-budget.
- `src/app/Trimix.Core/ViewModels/ExportDiveViewModel.cs` **(new, Core, testable)** — ctor injects `ILocalStore`, `IEnumerable<IDiveDestinationConnector>`, `IUserDialogs`, `INavigator`. Public `const string Route = "export-dive"`. `LoadAsync(Guid diveId)` loads the dive from the store. Filters connectors to `Descriptor.Kind == ConnectorKind.FileExport` (ids `export_uddf`/`export_dl7`/`export_pdf`) and exposes them for the picker. `[RelayCommand] ExportAsync(string connectorId)`: resolve by id → `PushDiveAsync(dive)` → on `PushStatus.Pushed` toast success (the sink already surfaced the OS sheet), on `Failed`/`Unsupported` alert `Message`.
- `src/app/Trimix.App/Pages/ExportDivePage.xaml` + `.xaml.cs` **(new, App)** — three format buttons (UDDF/DL7/PDF) bound to `ExportCommand` with the connector id as parameter; Sonar `GlassCard` styling per `mockup-spec.md`. `QueryProperty(diveId)` → `LoadAsync`.
- `src/app/Trimix.Core/ViewModels/DiveDetailViewModel.cs` **(edit, additive only)** — add `[RelayCommand] Task Export() => navigator.GoToAsync(ExportDiveViewModel.Route, new Dictionary<string,object>{ ["diveId"] = Dive!.Id });`. Do not alter existing members (protects its pinned tests).
- `src/app/Trimix.App/Pages/DiveDetailPage.xaml` **(edit, additive only)** — add an "Export" `SecondaryButton` bound to `ExportCommand` (near the pinned "Match the site" pill / DETAILS card).
- `tests/Trimix.Core.Tests/ViewModels/ExportDiveViewModelTests.cs` **(new)** — assert format→connector selection, that a `RecordingExportFileSink`-backed connector receives the dive, and failure surfaces via `IUserDialogs`. Reuse `DiveFixture` from `Connectors/Export/ExportTestDoubles.cs`.

**Reported wiring (integrator adds; do NOT edit these files):**
- `MauiProgram.cs`, before `AddTrimixCore`: `builder.Services.AddSingleton<IExportFileSink, MauiExportFileSink>();`
- `MauiProgram.cs`, with the other page/VM registrations: `builder.Services.AddTransient<ExportDiveViewModel>(); builder.Services.AddTransient<ExportDivePage>();`
- `AppShell.xaml.cs` `RegisterModalRoutes()`: `Routing.RegisterRoute(ExportDiveViewModel.Route, typeof(ExportDivePage));`

**Must NOT touch:** `ServiceCollectionExtensions.cs`, `MauiProgram.cs`, `AppShell.*`, `SettingsViewModel/Page` (slice 4), connector infra (slice 2), BLE (slice 3), `.csproj`. **No deploy input required — export is fully REAL at runtime.**

---

### 9.2 Slice 2 — connector activation UI + endpoint options binding

**Goal.** A Settings "Connected Accounts" surface over the connector registry (sources + destinations) with legal-posture badges, consent + ToS-risk disclosure, a credential sign-in sheet feeding `ProvideCredentials`/`SignInAsync` (never persisting the password), disconnect, and an SSI QR render surface; plus making cloud/push endpoints config-injectable.

**Owned files (write + edit):**
- `src/app/Trimix.Core/ViewModels/ConnectedAccountsViewModel.cs` **(new, Core, testable)** — ctor injects `IEnumerable<IDiveSourceConnector>`, `IEnumerable<IDiveDestinationConnector>`, `IConnectorConsentStore`, `IUserDialogs`, `IQrEncoder`, `SsiQrPayloadBuilder`. Public `const string Route = "connected-accounts"`. Builds a `ConnectorRow` list from every connector's `Descriptor`; the legal badge maps `LegalPosture` → label (`OwnedHardware`→"Your device", `CleanOfficial`→"Official", `UserFile`→"Personal file", `ToSRiskReverseEng`→"Unofficial · personal export", `PartnerRequired`→"Partner"). `Connect(row)`: for a `ToSRiskReverseEng` connector, first `AuthenticateAsync` returns `NeedsInteraction` + the disclosure → show it via `IUserDialogs.ConfirmAsync`; on accept `consent.GrantAsync(id)`, then reveal the credential sheet. `SubmitCredentials(row, user, pass)`: cloud → `((GatedCloudSourceConnector)c).ProvideCredentials(user,pass)` then `AuthenticateAsync`; push → `((GatedPushConnectorBase)c).SignInAsync(user,pass)`. `Disconnect(row)`: `consent.RevokeAsync(id)` (already severs the exchanged token). `ShowSsiQr()`: `IQrEncoder.Encode(ssiQrPayloadBuilder.Build(dive))` → expose `QrMatrix` for the drawable. Never hold the password beyond the call.
- `src/app/Trimix.Core/Connectors/Push/IQrEncoder.cs` + `BasicQrEncoder.cs` + `QrMatrix.cs` **(new, Core, pure/testable)** — `QrMatrix(int Size, bool[,] Modules)` with `bool IsDark(int x,int y)`; `BasicQrEncoder` byte-mode, ECC level M, versions 1–10, all four masks evaluated. Behind the `IQrEncoder` port so a vetted QR NuGet can swap in later via `.csproj` (integrator).
- `src/app/Trimix.Core/Connectors/Cloud/CloudSourceOptions.cs` **(new, Core)** — nullable per-vendor endpoint overrides (`GarminSigninUrl`, `GarminTokenExchangeUrl`, …, `SuuntoTokenUrl`, `OceanicLoginUrl`, `OceanicDivesUrl`), all default null.
- `src/app/Trimix.Core/Connectors/Cloud/{Garmin,Suunto,Oceanic}CloudSourceConnector.cs` **(edit)** — take `CloudSourceOptions` as a ctor param; replace each `private const` URL read with `options.<X> ?? DefaultConst` (keep the existing const as the documented reverse-eng default; Oceanic's stays flagged placeholder). Behaviour is identical when unconfigured, so existing tests stay green after their ctor calls pass `new CloudSourceOptions()`.
- `src/app/Trimix.App/Pages/ConnectedAccountsPage.xaml` + `.xaml.cs` **(new, App)** — grouped `GlassCard` list (one row per connector: name, legal badge `BadgeMint`/dim, fidelity caption, Connect/Disconnect); an `IsVisible`-toggled inline credential sheet (two `Entry`s, the second `IsPassword=True`) per the `ImportPage` multi-state pattern; an SSI card hosting a `GraphicsView` bound to the QR drawable.
- `src/app/Trimix.App/Controls/QrCodeDrawable.cs` **(new, App)** — `IDrawable` drawing `QrMatrix` as black/white modules (mirrors `DepthChartDrawable`).
- `tests/Trimix.Core.Tests/ViewModels/ConnectedAccountsViewModelTests.cs` **(new)** — pre-consent `Connect` shows disclosure + performs zero connector I/O; accept → `GrantAsync`; credentials reach `ProvideCredentials`/`SignInAsync`; password never lands in `FakeSecureTokenStore.Entries`; disconnect revokes. Reuse `Connectors/Cloud/CloudTestDoubles.cs` / `Connectors/Export/ExportTestDoubles.cs`.
- `tests/Trimix.Core.Tests/Connectors/Push/BasicQrEncoderTests.cs` **(new)** — encode 2–3 known payloads to reference matrices; assert size/timing/finder patterns.
- `tests/Trimix.Core.Tests/Connectors/Cloud/{Garmin,Suunto,Oceanic}CloudSourceConnectorTests.cs` **(edit)** — pass `new CloudSourceOptions()`; add one test that an override URL is used.

**Config keys (bound by slice 4's config surface; see 9.4):** `Connectors:Padi:AuthUrl|PushUrl`, `Connectors:Ssi:AuthUrl|PushUrl`, `Connectors:Cloud:{Garmin,Suunto,Oceanic}*`. All default null ⇒ connector stays DARK / falls back to in-source consts.

**Reported wiring (integrator adds):**
- `ServiceCollectionExtensions.cs`, beside the existing `TryAddSingleton<PadiPushOptions>()`/`<SsiPushOptions>()`: `services.TryAddSingleton<CloudSourceOptions>();` and `services.TryAddSingleton<IQrEncoder, BasicQrEncoder>();` (or pre-register configured instances in `MauiProgram`).
- `MauiProgram.cs`: `builder.Services.AddTransient<ConnectedAccountsViewModel>(); builder.Services.AddTransient<ConnectedAccountsPage>();`
- `AppShell.xaml.cs`: `Routing.RegisterRoute(ConnectedAccountsViewModel.Route, typeof(ConnectedAccountsPage));`
- Entry-point row on the Settings surface (integrator or slice 4, who owns `SettingsPage.xaml`): a `SecondaryButton` "Connected accounts" → `GoToAsync(ConnectedAccountsViewModel.Route)`.

**Must NOT touch:** `SettingsViewModel/Page` (slice 4), export sink (slice 1), BLE (slice 3), `ServiceCollectionExtensions.cs`, `MauiProgram.cs`, `AppShell.*`, `.csproj`. Keep the four guardrails — the tests enforce them.

---

### 9.3 Slice 3 — real BLE GATT: scan→vendor→handshake→notify→frame

**Goal.** Turn the compiled BLE skeletons into a vendor-parameterised pipeline: identify a discovered device's `DiveComputerVendor` by advertised service-UUID + name, then connect / resolve the write+notify characteristics / enable notifications / **write the download handshake** / surface notification bytes as `DiveComputerFrame`s into the existing parsers. Shearwater is implemented from public Subsurface/libdivecomputer facts; other vendors get the pipeline + documented override hooks.

**Owned files (write + edit):**
- `src/app/Trimix.Core/DiveComputer/BleGattProfiles.cs` **(new, Core, pure/testable)** — `record VendorGattProfile(DiveComputerVendor Vendor, IReadOnlyList<string> AdvertisedServiceUuids, string? WriteCharacteristicUuid, string? NotifyCharacteristicUuid, IReadOnlyList<byte[]> HandshakeCommands, IReadOnlyList<string> NameHints)`. Static `All` table: **Shearwater** = service `fe25c237-0ece-443c-b0aa-e02033e7029d` (public, already in-repo), char UUIDs left null ⇒ resolve by GATT property (candidate `27b7570a/b-359e-45a3-91bb-cf7e70049bd2` documented as *verify-before-pin*), `HandshakeCommands` empty with a labelled TODO (transcribe the real PC-protocol init from libdivecomputer `shearwater_common.c`, hardware-validate). Other vendors: `NameHints` only, null char UUIDs. `static DiveComputerVendor? TryIdentify(IReadOnlyList<string> advertisedServiceUuids, string? name)` — service-UUID match first, then name hints. This subsumes the two duplicated private `GuessVendor` methods.
- `src/app/Trimix.Core/DiveComputer/BleGattOverrides.cs` **(new, Core)** — `static void Apply(BleGattOptions)` merges operator overrides (UUIDs / handshake bytes) into the profile table; `BleGattOptions` = nullable per-vendor fields. Config-injectable, default no-op.
- `src/app/Trimix.App/Platforms/Android/Ble/BleTransport.cs` **(edit)** — scan filters/identifies via `BleGattProfiles` (advertised service UUIDs from `ScanResult`, then name); after `OnServicesDiscovered`, resolve the **write** characteristic (profile UUID else first with `Write`/`WriteNoResponse`) and **notify** characteristic (profile UUID else first with `Notify`/`Indicate`), enable notifications (existing CCCD path), then **write the profile's `HandshakeCommands`** to the write characteristic to start the download; keep surfacing notification bytes as frames.
- `src/app/Trimix.App/Platforms/iOS/Ble/BleTransport.cs` **(edit)** — CoreBluetooth twin: `ScanForPeripherals` with the profile service UUIDs; on `OnDiscoveredCharacteristics` resolve write+notify by profile UUID/properties, `SetNotifyValue(true, …)`, then `WriteValue(handshake, writeChar, CBCharacteristicWriteType.WithResponse)`; keep `UpdatedCharacterteristicValue`→frame.
- `tests/Trimix.Core.Tests/Connectors/Ble/BleGattProfilesTests.cs` **(new)** — identify Shearwater by service UUID and by name hint; unknown → null; override merge changes the resolved UUID/handshake; Shearwater service UUID present.

**Config keys (bound by slice 4):** `Ble:<Vendor>:{ServiceUuid,WriteCharUuid,NotifyCharUuid,HandshakeCommandsHex}` — default absent ⇒ in-source profile. Applied via `BleGattOverrides.Apply(...)` at startup.

**Reported wiring (integrator adds):**
- `MauiProgram.cs`, once at startup (before/after `AddTrimixCore`): `BleGattOverrides.Apply(config.Ble);` (no DI change to `BleTransport`, which keeps its `(ILogger<BleTransport>)` ctor and reads the static profile table).

**Must NOT touch:** `IDiveComputerTransport` / `SimulatorTransport` / the six protocol parsers / `ShearwaterFixtures` (the fixture-frame contract is frozen — the fixture parser stays the shipped decoder; the real Shearwater *log-format* parser is hardware-gated M3 work, out of this slice), `ServiceCollectionExtensions.cs`, `MauiProgram.cs`, `.csproj`. **Runtime input:** physical device + real computer; the exact Shearwater handshake bytes and any undocumented char UUIDs are labelled deploy inputs, never guessed.

---

### 9.4 Slice 4 — client config surface + privacy purge

**Goal.** An external config surface (API base URL / connector endpoints / feature flags / BLE UUID overrides) bound from a shipped asset, and GDPR export/delete **plus a sign-out/delete purge of connector consent + exchanged tokens** (cylinders + structured conditions are already carried in `DiveDto` and cleared with dives).

**Owned files (write + edit):**
- `src/app/Trimix.Core/Configuration/TrimixClientConfig.cs` **(new, Core)** — POCO tree: `Api { BaseUrl }`, `Connectors { Padi{AuthUrl,PushUrl}, Ssi{AuthUrl,PushUrl}, Cloud{…} }`, `Ble { <Vendor>{ServiceUuid,WriteCharUuid,NotifyCharUuid,HandshakeCommandsHex} }`, `Features { EnableCloudConnectors, EnablePushConnectors }`. All nullable/defaulted; **no secret defaults**.
- `src/app/Trimix.Core/Configuration/ClientConfigBinder.cs` **(new, Core, pure/testable)** — `TrimixClientConfig Parse(string json)` (System.Text.Json, web options, missing keys → defaults). Keeps the App decoupled from binding logic.
- `src/app/Trimix.Core/Privacy/ConnectorPurgeService.cs` **(new, Core, testable)** — ctor injects `IAuthSession`, `IEnumerable<IDiveSourceConnector>`, `IEnumerable<IDiveDestinationConnector>`, `IConnectorConsentStore`, `ILogger`. In the ctor, subscribe `session.AuthStateChanged`; on `!isAuthenticated`, `await consent.RevokeAsync(id)` for every connector id (`RevokeAsync` already deletes the exchanged token via `ConnectorTokenKeys.AllFor`). Mirrors how `SyncService` already purges the local dive store on the same event, so **GDPR delete** (which ends in `session.SignOutAsync()`) triggers the connector purge for free. Idempotent + exception-swallowing per-connector.
- `src/app/Trimix.App/Services/AppConfigLoader.cs` **(new, App)** — `Task<TrimixClientConfig> LoadAsync()` reads `appsettings.json` via `FileSystem.OpenAppPackageFileAsync("appsettings.json")` and returns `ClientConfigBinder.Parse(...)`; missing asset → defaults (never throws at boot).
- `src/app/Trimix.App/Resources/Raw/appsettings.json` **(new asset)** — dev defaults (`Api.BaseUrl` loopback, all connector endpoints null, features off). Already covered by the `Resources\Raw\**` MauiAsset glob — **no `.csproj` edit**.
- `tests/Trimix.Core.Tests/Configuration/ClientConfigBinderTests.cs` **(new)** — parse full/partial/empty JSON → correct defaults; unknown keys ignored.
- `tests/Trimix.Core.Tests/Privacy/ConnectorPurgeServiceTests.cs` **(new)** — grant consent + store tokens for 2 connectors; fire `AuthStateChanged(false)`; assert all consent cleared and `ISecureTokenStore` empty; guest/no-connector paths are no-ops.

**Reported wiring (integrator adds):**
- `MauiProgram.cs`, top of `CreateMauiApp`: `var cfg = AppConfigLoader.LoadAsync().GetAwaiter().GetResult();` then feed it: `AddTrimixCore(o => o.ApiBaseAddress = new Uri(cfg.Api.BaseUrl ?? <existing default>))`, and pre-register configured option instances before `AddTrimixCore` — `new PadiPushOptions{ AuthUrl=cfg.Connectors.Padi.AuthUrl, PushUrl=cfg.Connectors.Padi.PushUrl }`, likewise `SsiPushOptions`, `CloudSourceOptions`; and `BleGattOverrides.Apply(cfg.Ble)`.
- `MauiProgram.cs`: `builder.Services.AddSingleton<ConnectorPurgeService>();` and resolve it once at startup (e.g. in `App`/`AppStartupService`) so its ctor subscription is live.
- The Release `ApiBaseAddress` (`https://api.trimix.frostaura.net`) and connector endpoint values come from the deployed `appsettings.json`, gated by name clearance / legal sign-off — never committed as real secrets.

**Must NOT touch:** `SyncService` (its `AuthStateChanged` local-store purge is already REAL — do not duplicate it), `AuthSession`, connector infra internals (slice 2 owns cloud options), BLE transports (slice 3), export sink (slice 1), `ServiceCollectionExtensions.cs`, `MauiProgram.cs`, `.csproj`. Slice 4 may edit `SettingsViewModel/Page` only if it adds an export-includes-cylinders assertion or the Connected-Accounts entry row (it owns those files); the purge itself needs **no** `SettingsViewModel` change.

---

### 9.5 File-ownership matrix (collision-free parallel execution)

| Area | Slice 1 (Export) | Slice 2 (Activation) | Slice 3 (BLE) | Slice 4 (Config/Privacy) |
|---|---|---|---|---|
| New Core VMs | `ExportDiveViewModel` | `ConnectedAccountsViewModel` | — | — |
| New Core services/types | — | `IQrEncoder`/`BasicQrEncoder`/`QrMatrix`, `CloudSourceOptions` | `BleGattProfiles`, `BleGattOverrides`/`BleGattOptions` | `TrimixClientConfig`, `ClientConfigBinder`, `ConnectorPurgeService` |
| Edited Core files | `DiveDetailViewModel` (additive) | `{Garmin,Suunto,Oceanic}CloudSourceConnector` | — | — |
| New App files | `MauiExportFileSink`, `ExportDivePage` | `ConnectedAccountsPage`, `QrCodeDrawable` | — | `AppConfigLoader`, `Resources/Raw/appsettings.json` |
| Edited App files | `DiveDetailPage.xaml` | — | Android + iOS `BleTransport` | (opt) `SettingsPage` |
| New tests | `ExportDiveViewModelTests` | `ConnectedAccountsViewModelTests`, `BasicQrEncoderTests` (+ edit 3 cloud tests) | `BleGattProfilesTests` | `ClientConfigBinderTests`, `ConnectorPurgeServiceTests` |
| Integrator-only (report, never edit) | `MauiProgram`, `AppShell.*` | `SCE`, `MauiProgram`, `AppShell.*` | `MauiProgram` | `SCE`, `MauiProgram`, `App` startup |

No two slices write or edit the same file. `ServiceCollectionExtensions.cs`, `MauiProgram.cs`, `AppShell.*`, `App.xaml.cs`, `CoreConstants.cs`, and every `.csproj` are integrator-owned — the writers emit the exact additions and never touch them, so the four branches merge without conflict and the integrator applies one consolidated wiring diff.
