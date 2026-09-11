---
name: fa-trimix-decisions-sync
description: "Overlapping dives merge on padded time-interval overlap (0.30 x geometric mean, clamped 120-600 s); user-set priority picks the winner, fidelity only the tie-break; unlink hard-deletes; auto-sync on every link; flash fix reverted."
type: decision
last_verified: 2026-08-07
---

# Multi-provider sync & merge decisions

_Split out of [`decisions.md`](decisions.md), which stays product, economics and cross-cutting policy. All of
this landed 2026-08-07 and is committed and pushed._

- **Overlapping dives group by TIME-INTERVAL OVERLAP**, not the old start+duration+depth tolerances.
  `DiveMergePolicy.IsSamePhysicalDive` clusters two dives iff their padded windows overlap, where
  **padding = 0.30 × the geometric mean of the two durations (√(durA·durB) — the root of their *product*),
  clamped to [120 s, 600 s]**. Founder rationale, stated three times: two computers on one dive can start and
  stop minutes apart — up to ~10 min — so the margin must scale with dive length and reach ~10 min for a
  normal 30–60 min dive. The ceiling is load-bearing: it stops a large pad bridging a short surface interval
  and merging two genuinely separate repetitive dives, which is the one honest cost of "never miss a same-dive
  grouping" and the founder chose it. Depth still feeds `FidelityScore` but no longer gates the match.
  Constants are named and tunable in `DiveMergePolicy`.
- **The user sets provider PRIORITY, and priority decides which computer wins a merge.** `IProviderPriority` /
  `ProviderPriorityService` persist an order (CSV of connectorIds under `PreferenceKeys.ProviderPriority`;
  connectorId == `DiveDto.SourceVendor`). Its composite delegate — `RankOf(vendor)*1000 + FidelityScore` — is
  threaded into all four `DiveMergePolicy.Merge` call sites, so **priority dominates the primary pick and
  fidelity is only the intra-provider tie-break**: "priority leads, fill gaps" — the priority provider's stats
  lead and any field it lacks fills from the next provider down. `DiveMergePolicy` stays static and pure; the
  delegate seam already existed and was tested.
- **Merge stats and drill-in are surfaced.** Logbook rows show one badge per contributing provider in priority
  order (primary first, `Merged.Badges`, sorted by the same delegate). Dive-detail's "SOURCES · TAP TO COMPARE"
  badges are tappable and swap the whole detail view to that provider's own recording **in place**
  (`SelectSourceCommand`, no navigation — the cluster is already loaded).
- **Providers screen is grouped Connected / Not-connected, and only Connected is reorderable** (founder
  direction). Priority is only meaningful for linked providers, since only they carry dives, so only their
  order is persisted (`PersistConnectedOrder` on ReorderCompleted); the Not-connected group stays a static
  list with the connect-nudge.
- **Reorder UX is the native `CollectionView` drag — NOT a custom pan, NOT up/down arrows.** Both alternatives
  were built and rejected: arrows (founder: "terrible… make them amazing") and a Pan/Pointer-gesture drag
  handle inside the ScrollView, which received *zero* touch events. Native `CanReorderItems` handles the
  scroll/drag arbitration itself, but the reorderable list must live **outside** any ScrollView.
- **Unlinking a provider hard-deletes that provider's dives, and a LAUNCH sweep removes orphans.**
  `ILocalStore.DeleteDivesByVendorAsync` is a hard delete, not a tombstone — a tombstone would resurrect on the
  next sync of a still-consented provider. Wired into unlink (whose dialog copy used to promise dives stay) and
  into `AppStartupService.RunAsync`, which on every launch deletes dives whose vendor is a known source
  connector without current consent. The launch sweep exists because unlink cleanup only handles
  going-forward, and the founder hit dives orphaned by an unlink that predated it.
- **The flash-free-sync attempt was REVERTED — do not re-try it the same way.** The goal was to show the sync
  sheet's dimmed backdrop the instant the logbook appears after linking, with no bare-logbook flash. The
  attempt hoisted `IsSyncing=true` before the `//Logbook` nav and reconciled in `LogbookPage.OnAppearing`; it
  **broke the add-sheet → "Sync now" path** (the sheet vanished, founder-reported), because the same guard
  that stops a premature push while on Providers also blocks the push when the add-sheet modal covers the
  Logbook. Everything was reverted to the known-good nav-first ordering (`gotchas-ui.md`). Any re-attempt must
  treat from-Providers and on-Logbook-under-a-modal differently and be device-tested — the sheet is transient
  and `adb` screenshots cannot verify it on MAUI's opaque canvas. The flash stays an open cosmetic item.
- **Auto-sync fires on EVERY provider link, not just the first.** Linking ScubaPro second synced nothing; the
  cause was a deliberate `wasFirstLink && HasLinkedSources` gate in `ProviderOAuthViewModel` with a test
  pinning it. **Reversed by founder expectation** — the gate is now just `HasLinkedSources` and every link
  triggers `SyncSessionViewModel.AutoSyncAfterLinkAsync`. Safe because the run covers all linked sources and
  imports are idempotent upserts; the test was inverted.
