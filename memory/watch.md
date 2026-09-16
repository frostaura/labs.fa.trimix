---
name: fa-trimix-watch
description: "Unpaid debts: no adversarial re-review of the rebuilt tree before M3, dead backend DTOs and Sites leftovers still shipping in Trimix.Contracts, and a physical-iPhone deploy path that is unblocked and has never been used."
type: watch
last_verified: 2026-09-16
---

# Watch list

- **No adversarial re-review of the rebuilt tree has ever been run**, and one is owed before any M3
  field testing — specifically over the six reverse-engineered read connectors and the on-device
  entitlement path. Three of the six have live-captured contracts; Mares, Oceanic+ and Shearwater are
  researched guesses or deliberate placeholders and must be reviewed as such, not as implementations.
- **Dead backend DTOs still ship in `Trimix.Contracts`.** Orphaned by a repo-wide reference grep on
  2026-09-16 — nothing outside each file, or outside another dead file, refers to them: `ApiRoutes.cs`
  (including `ApiRoutes.Sites`), `Dtos/PasskeyDtos.cs`, `Dtos/PrivacyDtos.cs`, `Dtos/ContributionDtos.cs`
  (`SiteContribution*`), **`Dtos/AuthDtos.cs`** and **`Dtos/HealthDtos.cs`**, plus the now-moot Android
  `network_security_config.xml` dev-loopback file. **Treat this as the set found so far, not a closed
  inventory** — it has already been short twice. `AuthDtos.cs` is the one that matters: an email +
  password account-auth shape (`RegisterRequest`, `LoginRequest`, `AuthResponse`, `UserDto`) sitting in a
  product whose invariant is that there is no auth of any kind, so it reads as though accounts once worked
  and could return. All are cleanup candidates; until they go the repo reads as though a backend exists.
- **Sites leftovers are dormant, not live.** `Dive.SiteId` and `ImportResult.SiteMatched` survive as
  always-null / always-false fields (dives serialize as JSON, so removing the capability needed no
  storage migration), and the `site_*` funnel constants stay because the taxonomy is frozen. Do not
  read any of them as a surviving Sites capability — there is none.
- **The physical-iPhone deploy path is open and unused.** Since 2026-07-29 the app deploys wirelessly
  to the founder's iPhone 17 Air under the `net.frostaura.trimix` profile (paid team, valid to 2027 —
  carried forward from that date; re-checking it needs the device and the developer account).
  That retired the long-standing provisioning blocker, so on-device StoreKit and real-vendor-endpoint
  testing are available and have still not been exercised. **The recipe is not written down anywhere
  in this tree:** the in-tree home for it is the division's
  [`maui-device-build`](../../../.claude/skills/maui-device-build/SKILL.md) skill, which defers the
  iOS device procedure to a Gaia plugin skill that is not installed on this Mac
  ([`../../../../memory/plugin-roster-drift.md`](../../../../memory/plugin-roster-drift.md)), so
  today the next deploy re-derives it. Write it into that skill when it is re-derived.
