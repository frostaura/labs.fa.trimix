---
name: labs-fa-trimix-decisions-sync
description: "Merge pad is 0.30 x the SHORTER duration, clamped 120-600 s, though the class XML doc claims a geometric mean; priority picks the winner, fidelity tie-breaks; every link auto-syncs; unlink hard-deletes."
type: decision
last_verified: 2026-11-20
---

# Multi-provider sync & merge decisions

_Split out of [`decisions.md`](decisions.md), which stays product, economics and cross-cutting policy. All of
this landed 2026-08-07; its durability is recorded once, in [`state.md`](state.md)._

- **Overlapping dives group by TIME-INTERVAL OVERLAP**, not the old start+duration+depth tolerances.
  `DiveMergePolicy.IsSamePhysicalDive` clusters two dives iff their padded windows overlap. **The shipped
  padding is `0.30 × Math.Min(durA, durB)` — the SHORTER dive — clamped to [120 s, 600 s]**, read off
  `Stats/DiveMergePolicy.cs` (re-read 2026-11-20; never the geometric mean). Founder rationale: two computers on one dive start and stop minutes apart, so the margin
  scales with length and should reach ~10 min on a normal 30–60 min dive — which `Math.Min` misses at the
  low end (a 30 + 60 min pair pads 540 s, short of the 600 s ceiling). **Do not "correct" the code by
  inference:** its class-level XML doc claims the geometric mean while its method doc says the shorter dive,
  so which one was intended is a founder call — [`watch-docs.md`](watch-docs.md). The ceiling is load-bearing
  either way: it stops a large pad bridging a short surface interval and merging two separate repetitive
  dives. Depth still feeds `FidelityScore` but never gates the match; constants are named and tunable.
- **The user sets provider PRIORITY, and priority decides which computer wins a merge.** `IProviderPriority` /
  `ProviderPriorityService` persist an order (CSV of connectorIds under `PreferenceKeys.ProviderPriority`;
  connectorId == `DiveDto.SourceVendor`). Its composite delegate — `FidelityDelegate()`, rank × `PriorityWeight`
  (1000) + `FidelityScore`, with the order snapshotted once per merge pass so one pass ranks consistently — is
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
