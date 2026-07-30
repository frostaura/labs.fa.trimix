---
name: fa-trimix-gotchas
description: "Commit + push promptly (iCloud eviction); no CI — every green is one Mac; connectors fixture-only, never imported a real dive; on-device entitlements piratable"
type: gotcha
last_verified: 2026-07-30
---

# Gotchas

- **iCloud mount — commit + push promptly.** The whole tree lives on the iCloud Drive path, where an eviction can strip locally-present files. Everything through `399110b` is pushed (see Watch list), but any new uncommitted work is one eviction from loss. Run `repo-durability-check` before asserting git safety.
- **No CI.** Every "green" rests on a local `dotnet build`/`test` on one Mac with the user-local toolchain. Never mark a gate met on that basis.
- **Connectors are fixture-validated only.** No live vendor server was ever touched. Endpoints (Garmin SSO, Suunto `/v1`, Oceanic+, MySSI for Mares/ScubaPro/SSI, PADI GraphQL) are **researched-guess defaults**, config-injectable, and will likely need real endpoints at first live contact. Garmin's unofficial path (`garth`) died March 2026; `python-garminconnect`-style Android SSO is the surviving reverse-eng route. **No connector has ever imported a real dive.**
- **On-device entitlements are piratable** (no server receipt validation) — the accepted serverless tradeoff.
- **Dive-row push destinations reflect *currently linked* push providers**, not a per-dive push ledger (same approximation the prototype makes).
