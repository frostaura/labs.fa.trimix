# MEMORY — fa.trimix

_Last verified: 2026-07-24 by the Labs tree-audit pass (git log, remote, working tree, solution and test-project layout, docs set and README claims inspected directly)._

## Current state
**Built heavy, versioned late, and now deliberately parked before the gate that was supposed to come first.** Branch `main`, working tree clean, two commits: `c5b0144` (2026-07-03, scaffold) and `8c24f91` (2026-07-24, which finally committed the whole ~42.6k-LOC product that had been sitting untracked). A private remote `git@github.com:frostaura/fa.trimix.git` is configured and **has never been pushed** — `main` has no upstream, so the program still exists on one Mac plus iCloud.

Build milestones M1 (rails) and M2 (vertical slice), plus the aggregator and production-wiring extensions, are met **on local evidence only**: `Trimix.Core` + `Trimix.App` + `Trimix.Api` + `Trimix.Contracts`, 476 `[Fact]`/`[Theory]` test attributes across `tests/Trimix.Core.Tests` and `tests/Trimix.Api.Tests`, five real EF migrations verified migrate-from-zero against real `postgres:17`, a signed Android APK + AAB. **M3 and M4 — the external gates that actually answer the research question — are untouched.**

## Live decisions
- **Nothing further gets built until the free community-demand gate (M3) runs** — founder decision, 2026-07-24, at the portfolio triage. This program is the reason the Labs "cheap kill gates before expensive builds" rule exists: ~42.6k LOC were spent *before* a free gate that exists precisely to kill the bet cheaply. Running M3 on what already exists is the next action; more code is not. **If you are here to implement a feature, stop and check with the founder first.**
- **Aggregator pivot (2026-07-05, founder-directed):** import is a multi-source normalization hub, not BLE-only.
- **Legal override on reverse-engineered connectors (2026-07-05).** A 13-agent landscape sweep found **no clean published cloud API returns a dive profile** from any major vendor, and **PADI/SSI have no logbook-write API**. Apple HealthKit is the only clean cloud surface; file import and BLE-direct-from-owned-hardware are the other clean channels. Vendor-cloud pull and PADI/SSI push therefore require reverse-engineering credential-gated backends — a ToS/CFAA-gray area that knowingly **fails the program's own gate-3 "integrated legally"**. Flagged explicitly; the founder directed shipping them as real features. **A documented, accepted risk, not an oversight.** The three user-protecting guardrails in `CLAUDE.md` are independent of the override and stand.
- **Name "Trimix" was founder-picked 2026-07-03** from the PRD candidate list, which itself flags it as high-collision in the dive space. Predecessors: DeepLog (taken — live iOS competitor since 2026-04) and Wreckline.
- **Android first** (2026-07-03) because the host macOS is a beta OS with Xcode-toolchain risk.
- **The .NET MAUI client override was root-reviewed 2026-07-03**, superseding both the React mandate and the PRD's Swift/Kotlin dual-native exception. Settled.
- **PostGIS was explicitly rejected** in favour of a bounding-box prefilter plus in-memory haversine in `Sites/GeoMath.cs`, so integration tests run hermetically on SQLite.

## Gotchas
- **Never pushed, and the repo's `.git` carries stale lock files this workspace cannot delete** (`HEAD.lock`, `index.lock`; `Operation not permitted` on the iCloud mount). No agent working from the cloud mount can commit here until the founder clears them with `rm -f .git/*.lock` on the Mac. Until the push lands, a single iCloud eviction is still the whole program's failure mode.
- **`.github/` does not exist.** Zero CI, zero CD, zero reproducible build. Every "gate PASSED" claim rests on a local `dotnet build`/`dotnet test` on one Mac with an ad-hoc user-space toolchain.
- **BLE is real plumbing over an unvalidated protocol.** Real CoreBluetooth and Android LE transports plus clean-room fixture-tested parsers for Suunto/ScubaPro/Aqualung/Mares — but **`ShearwaterProtocol.cs`, the flagship vendor, parses the *simulator fixture* frame format, not the real Shearwater protocol**, and says so in its own header. Exactly one value is pinned as public fact: the Shearwater service UUID `fe25c237-0ece-443c-b0aa-e02033e7029d`. All other vendors are name-hint-only with null service UUIDs; handshake bytes are deploy inputs and **explicitly never guessed**. **No BLE code has ever touched a dive computer.**
- **A revenue-killing bug worth remembering:** Apple StoreKit bundle receipts are multi-KB and overflowed the `varchar(2048)` unique btree key on `Receipts.Token` — every real iOS purchase and restore would have 500'd with no entitlement granted. Fixed by widening to `text` with uniqueness on a fixed-length `TokenSha256` (migration `WidenReceiptToken`). Do not narrow that column again.
- Other hostile-QA finds, fixed and worth not re-introducing: the connector master-switch was inert (reverse-eng connectors were one consent-tap from live despite the flag reading false); GDPR delete never called the server; unbounded substring vendor matching made "Lantern" match "Tern"; a committed DB credential sat in base `appsettings.json`.
- `Trimix.Core/Protocols/`, `Services/` and `ViewModels/` contain only `.gitkeep` — dead scaffold dirs. 27 NU1608 warnings come from Xamarin.AndroidX transitive drift, deferred as cosmetic.
- **`[CAC_PER_PAYER = TBD]` is still a literal placeholder.** No CPI test has ever been run. Net-of-fee math is done ($14.99 → $12.74 at 15% / $10.49 at 30%, blended ~$13/payer), so the whole LTV:CAC gate rests on an unmeasured input.

## Open questions
- **Whether to run M3 on what exists, or reconsider the bet entirely.** The parking decision above settles what happens *next*; it does not settle whether the heaviest-build, thinnest-TAM bet in the slate is worth field-testing at all. Founder.
- **Name clearance** is unresolved and transitively gates the store listing, the API domain, the passkey relying party and therefore all real billing.
- **Site-DB seed licensing** — the 64 seeded sites are honest approximations; a licensed dataset is a launch blocker.

## Watch list
- **The deferred adversarial re-review is the most important open QA item.** `known-gaps.md`: the skeptic-verification pass on the first review run was cut off by a rate limit and findings were triaged directly; a fresh independent adversarial re-review of the hardened tree has **not** been run and is the recommended step before any M3 field testing.
- **`docs/architecture/overview.md` violates its own stop-and-resolve-the-drift rule.** §3 calls `google_play`/`app_store` "documented stubs returning NotConfigured"; both are real verifiers (616 LOC). §8 describes auth as "Email+password: PBKDF2 … JWT bearer"; auth is **passkey-only** (WebAuthn/FIDO2 via Fido2 4.0.1).
- Test-count docs disagree: `known-gaps.md` says 454 Core tests, `production-readiness.md` says 447 — a 7-test delta explained in one doc and never refreshed in the other.
- **`net10.0-ios` is not in the default TFM list** (opt-in via `-p:EnableIos=true`). iOS did run 1:1 on the iPhone 17 / iOS 26.5 simulator on 2026-07-03, with three durable csproj fixes recorded in the `fa-trimix-build-environment` memory.
