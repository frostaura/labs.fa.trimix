---
name: fa-trimix-watch-docs
description: "At least six in-repo documents still describe the pre-pivot product; two open by claiming authority over code and are cited from the build files, so a newcomer is told the opposite of what ships. Owner call, not an agent fix."
type: watch
last_verified: 2026-10-22
---

# Watch — the documents that still describe the deleted product

Verified by inspection 2026-09-16, re-checked 2026-10-22: the first four below are still stale, and two
more turned up. **None of these is an agent's to rewrite** — they are the owner's, and the correction is
recorded here so it survives rather than being applied. Code is authoritative over every one of them.
`docs/prd.md` is amendment-layered and has never been assessed whole, so the list may still be short.

- **`README.md` is false end to end.** It advertises HealthKit, BLE-direct, FIT/UDDF/DL7 file import
  and a community-moderated dive-site database; it lists a `src/backend/` and a `tests/Trimix.Api.Tests`,
  and prints `docker compose up -d postgres` and `dotnet run --project src/backend/Trimix.Api`, neither
  of which can run. `src/` holds only `app`, `shared`, `tools`. Every one of those is a *non-goal*.
- **`docs/architecture/overview.md` calls itself the "binding contract for all implementation work"**
  and is stale on six points: biometric-only auth via `IBiometricUnlock` / `AuthSession` (no such symbol
  exists); seven connectors with Mares + ScubaPro + SSI sharing `MySsiCloudClient` (six read connectors,
  ScubaPro rides Firebase); "never exercised against real vendor production servers" (three live
  captures are banked); a bundled `Sites/SiteAtlas.cs` (none exists); a 3-tab shell with `AuthPage` /
  `SitesPage` (two `ShellContent` entries); and a test count. It is **load-bearing while invisible to
  the context layer** — `Directory.Build.props`, `Trimix.Core.csproj` and `Trimix.Harness.csproj` cite
  it, and `Trimix.Core.csproj`'s "architecture overview §12" is a dead pointer: its locked decisions
  stop at 11.
- **`docs/known-gaps.md`, which `CLAUDE.md` names as the catalogue of runtime-gated surfaces, is
  falsified by the repo it describes**: "every provider integration is fixture-validated only … no
  request has ever been made to a real vendor production server" and "the MySSI corridor concentrates
  risk". It also pins a test count. This is the document a compliance or legal reader opens first.
- **`docs/production-readiness.md`** (re-grounded 2026-07-27, before the auth, Sites and live-capture
  changes): seven connectors incl. a shared `MySsiCloudClient`, "fixture-proven only"; a "Biometric
  unlock (the only auth)" surface marked REAL; a bundled `SiteAtlas` and Sites tab marked REAL; and a
  pinned test count in its evidence baseline.
- **`docs/design/aggregator-architecture.md`** heads itself "Authoritative connector-layer contract"
  yet says the Providers screen "lists all seven", connectors are "never exercised against real vendor
  production servers", Garmin and Suunto payloads are FIT, and ScubaPro, Mares and SSI ride the MySSI
  corridor — wrong on Garmin, Suunto and ScubaPro by live capture (`decisions-connectors.md`).
- **`docs/design/mockup-spec.md`** is the one already handled: `CLAUDE.md` flags it superseded and
  declares code authoritative, the pattern the others are missing. Its palette table still lists a
  different mint from the shipped `Colors.xaml`.

**Open, owner unassigned:** which of these is still meant to bind. Until that is answered nothing here
can be closed, and no agent should treat the overview's or the aggregator doc's authority claim as live.
Delete this topic when the stale documents are rewritten or explicitly retired.
