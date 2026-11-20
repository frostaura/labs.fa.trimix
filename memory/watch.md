---
name: labs-fa-trimix-watch
description: "Unpaid debts: no adversarial re-review before M3; the 2026-11-20 rename not yet in product files; dead server contracts in Trimix.Contracts; push copy cites a missing file export; a Sites-era location permission."
type: watch
last_verified: 2026-11-20
---

# Watch list

- **No adversarial re-review of the rebuilt tree has ever been run**, and one is owed before any M3
  field testing — over the six reverse-engineered read connectors and the on-device entitlement path.
  Three of the six have live-captured contracts; Mares, Oceanic+ and Shearwater are researched guesses
  or deliberate placeholders and must be reviewed as such, not as implementations.
- **Dead server DTOs still ship in `Trimix.Contracts`.** Zero references outside each defining file (or
  another dead file), by a whole-word reference count over `src`/`tests` re-run 2026-11-20 — inference,
  no test pins it: `ApiRoutes.cs`, `Dtos/PasskeyDtos.cs`, `Dtos/PrivacyDtos.cs`, `Dtos/ContributionDtos.cs`,
  `Dtos/AuthDtos.cs`, `Dtos/HealthDtos.cs`, `Dtos/PurchaseDtos.cs`, `Dtos/EntitlementDtos.cs`, the
  `AnalyticsBatch*` and `DiveSync*` records, `TrimixHeaders.cs` (cited only by a doc-comment about
  server-emitted events), and — found 2026-11-20 — `UserRoles` (`User` / `Moderator`) in
  `WellKnownValues.cs`. **The inventory has now come up short four times — treat it as found-so-far.**
  Two read as live contradictions of invariants: `AuthDtos.cs` is an email + password account shape
  under a no-auth-of-any-kind product, and `PurchaseDtos.cs` is **server receipt validation** under a
  client-trust entitlement model. All are cleanup candidates; until they go the repo reads as though a
  backend exists. **One look-alike is not removable:** `ContributionStatus` in `WellKnownValues.cs` has no
  live consumer, but the frozen `site_contribution_*` status constants in `FunnelEvents.cs` alias it, so
  deleting it breaks the frozen taxonomy — inline the literals first if it is ever cleaned up.
- **Dormant push copy sends divers to a file export that does not exist.** The PADI push connector's
  pre-consent disclosure ends "prefer the clean UDDF/DL7 file export you upload yourself", and its
  no-endpoint errors (and SSI's, and `GatedPushConnectorBase`'s) say to use a UDDF/DL7/PDF file or "file
  export" instead — but user-facing file export is a founder-removed non-goal. Dormant only because PADI
  and SSI are outside `ProviderCatalog.LiveConnectorIds` (read 2026-11-20); promoting either would show a
  disclosure that is untrue under guardrail (iii). Fix the copy before promotion. Owner: unassigned.
- **`network_security_config.xml` is live, not orphaned — trim it, never delete it.** The Android
  manifest references it, and its `base-config cleartextTrafficPermitted="false"` is the app's only
  cleartext ban. Only the loopback `domain-config` (for the deleted docker-compose API) and its comment
  are moot. Min Android is 23, and on API 23-27 the platform default permits cleartext, so deleting the
  file as cleanup would weaken the posture (inference from platform defaults, untested). Owner of the
  trim: unassigned.
- **Sites leftovers are dormant, not live — but one is a store-review risk.** `DiveDto.SiteId` and
  `ImportResult.SiteMatched` survive as always-null / always-false fields (dives serialize as JSON, so
  removing the capability needed no migration), and the `site_*` funnel constants stay because the
  taxonomy is frozen. Also left behind: Android still declares `ACCESS_FINE_LOCATION` (manifest comment
  "dive-site match + search"), iOS `NSLocationWhenInUseUsageDescription` still says location is used "to
  find and match nearby dive sites", and `ILocationService` is registered in DI with no consumer. A
  permission and purpose string for a capability that does not exist is a privacy-disclosure and
  store-review risk (inference). Whether to strip them before store work: unassigned. Do not read any
  of these as a surviving Sites capability — there is none.
- **The 2026-11-20 rename is not in the product yet** (grep of tracked files, 2026-11-20): the
  solution is still `fa.trimix.slnx`, named so in `README.md` and the `docs/architecture/overview.md`
  layout; `README.md` and `docs/production-readiness.md` still call the program `fa.trimix`. A product
  change, not a context one. **`Trimix.Harness`'s `UserSecretsId` `fa.trimix.harness` keeps its
  spelling** — it keys the user-secrets store, so renaming it orphans any secrets kept under it. Bundle
  id `net.frostaura.trimix` and the `trimix.*` SKUs are product strings and unaffected.
- **The physical-iPhone deploy path is open and unused.** Since 2026-07-29 the app deploys wirelessly
  to the founder's iPhone 17 Air under the `iOS Team Provisioning Profile: net.frostaura.trimix`, which
  expires 2027-07-29 (re-read off the installed profile 2026-11-20). On-device
  StoreKit and real-vendor-endpoint testing are available and have still not been exercised. The
  generic device recipe lives in the plugins' `fa-engineering-stack-dotnet-maui` (it resolved in the
  installed skill list 2026-11-20; confirm again before relying on it);
  the only Trimix-specific inputs are that bundle id and profile.
