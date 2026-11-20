---
name: fa-trimix-watch-docs
description: "At least six in-repo documents still describe the pre-pivot product; two claim authority over code and are cited from build files. A README-fix PR was opened and closed unmerged 2026-10-27. Owner call, not an agent fix."
type: watch
last_verified: 2026-11-20
---

# Watch — the documents that still describe the deleted product

Verified by inspection 2026-09-16, re-checked 2026-10-22 and 2026-11-20: all six below are still stale,
and every test count they pin no longer matches the suite. **None of these is an agent's to rewrite** — they are the owner's, and the correction is
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
- **`docs/design/mockup-spec.md`** is the one half-handled: `CLAUDE.md` declares code authoritative over
  it, the pattern the others are missing. The file itself still titles itself "authoritative", its
  2026-07-27 banner still names a 3-tab **Log · Sites · You** bar, and its §1 token table is the
  first-pass palette its own provenance note supersedes — every listed hex differs from the shipped
  `Colors.xaml`, not only the mint (re-read 2026-11-20).

**A README fix was attempted once and abandoned.** PR #1 on the remote ("Update README to match current
serverless architecture", README only) was opened and closed unmerged within minutes on 2026-10-27; its
branch is gone and `main`'s README is unchanged since 2026-07-24 (`gh pr view`, `git log`, 2026-11-20).
Nothing records why it was closed — ask the owner before re-proposing the same rewrite.

**Open, owner unassigned:** which of these is still meant to bind. Until that is answered nothing here
can be closed, and no agent should treat the overview's or the aggregator doc's authority claim as live.
Delete this topic when the stale documents are rewritten or explicitly retired.
