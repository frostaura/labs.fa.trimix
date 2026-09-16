---
name: fa-trimix-watch-docs
description: "Four in-repo documents still describe the pre-pivot client-server product; one opens by claiming precedence over code and is cited from the build files, so a newcomer is told the opposite of what ships. Owner call, not an agent fix."
type: watch
last_verified: 2026-09-16
---

# Watch — the documents that still describe the deleted product

Verified by inspection 2026-09-16. **None of these is an agent's to rewrite** — a README, an
architecture document and a gap catalogue are the owner's, and the correction is recorded here so it
survives rather than being applied. Code is authoritative over every one of them.

- **`README.md` is false end to end.** It advertises HealthKit, BLE-direct, FIT/UDDF/DL7 file import
  and a community-moderated dive-site database; it lists a `src/backend/` (EF Core + PostgreSQL 17,
  accounts, server receipt validation, site moderation, analytics ingest) and a `tests/Trimix.Api.Tests`,
  and it prints two build commands — `docker compose up -d postgres` and `dotnet run --project
  src/backend/Trimix.Api` — that cannot run. None of it exists: `src/` holds only `app`, `shared`, `tools`.
  Every one of those is a *non-goal* under this program's invariants, so the file inverts the product.
- **`docs/architecture/overview.md` calls itself the "binding contract for all implementation work"**
  and is stale on six points: biometric-only auth via `IBiometricUnlock` / `AuthSession` (no such symbol
  exists anywhere); seven connectors with Mares + ScubaPro + SSI sharing `MySsiCloudClient` (there are six
  read connectors and ScubaPro rides Firebase); "never exercised against real vendor production servers"
  (three live captures are banked under `docs/reverse-engineering/`); a bundled 14-site `Sites/SiteAtlas.cs`
  (no `*site*` file exists under `src/`); a 3-tab `Log · Sites · You` shell with `AuthPage`, `SitesPage`,
  `SiteDetailPage`, `SettingsPage` (two `ShellContent` entries, none of those pages); and a test count.
  It is **load-bearing while invisible to the context layer** — `Directory.Build.props` and both
  `Trimix.Core.csproj` and `Trimix.Harness.csproj` cite it, and `Trimix.Core.csproj`'s "architecture
  overview §12" is itself a dead pointer: the document's locked decisions stop at 11.
- **`docs/known-gaps.md`, which this program's `CLAUDE.md` names as the catalogue of runtime-gated
  surfaces, is falsified by the repo it describes**: "every provider integration is fixture-validated
  only … no request has ever been made to a real vendor production server from this environment" and "the
  MySSI corridor concentrates risk — Mares, ScubaPro and SSI all ride one shared client". It also pins a
  test count. This is the document a compliance or legal reader opens first.
- **`docs/design/mockup-spec.md`** is the one already handled: `CLAUDE.md` flags it superseded and
  declares code authoritative, which is the pattern the three above are missing. Worth keeping — the spec's
  own palette table still lists a different mint from the shipped `Colors.xaml`.

**Open, owner unassigned:** which of the README, the architecture overview and the gap catalogue is still
meant to bind. Until that is answered nothing here can be closed, and no agent should treat the overview's
precedence claim as live. Delete this topic when the three are rewritten or explicitly retired.
