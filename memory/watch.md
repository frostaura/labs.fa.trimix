---
name: fa-trimix-watch
description: "Unpaid debts: no adversarial re-review of the rebuilt tree before M3, dead backend DTOs and Sites leftovers still shipping in Trimix.Contracts, and a physical-iPhone deploy path that is unblocked and has never been used."
type: watch
last_verified: 2026-09-11
---

# Watch list

- **No adversarial re-review of the rebuilt tree has ever been run**, and one is owed before any M3
  field testing — specifically over the six reverse-engineered read connectors and the on-device
  entitlement path. Three of the six have live-captured contracts; Mares, Oceanic+ and Shearwater are
  researched guesses or deliberate placeholders and must be reviewed as such, not as implementations.
- **Dead backend DTOs still ship in `Trimix.Contracts`** — confirmed present 2026-09-11: `ApiRoutes.cs`
  (including `ApiRoutes.Sites`), `Dtos/PasskeyDtos.cs`, `Dtos/PrivacyDtos.cs` and
  `Dtos/ContributionDtos.cs` (`SiteContribution*`), all orphan shapes from the deleted server, plus the
  now-moot Android `network_security_config.xml` dev-loopback file. Nothing references them; they are
  cleanup candidates, and until they go they read as though a backend still exists.
- **Sites leftovers are dormant, not live.** `Dive.SiteId` and `ImportResult.SiteMatched` survive as
  always-null / always-false fields (dives serialize as JSON, so removing the capability needed no
  storage migration), and the `site_*` funnel constants stay because the taxonomy is frozen. Do not
  read any of them as a surviving Sites capability — there is none.
- **The physical-iPhone deploy path is open and unused.** Since 2026-07-29 the app deploys wirelessly
  to the founder's iPhone 17 Air under the `net.frostaura.trimix` profile (paid team, valid to 2027);
  the repeatable recipe lives in the `fa-trimix-build-environment` user memory. That retired the
  long-standing provisioning blocker, so on-device StoreKit and real-vendor-endpoint testing are
  available and have still not been exercised.
