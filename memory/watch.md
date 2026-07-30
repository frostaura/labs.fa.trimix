---
name: fa-trimix-watch
description: "Physical-iPhone deploy recipe retired the provisioning blocker; durability history of pushed SHAs; adversarial re-review recommended before M3; dead server DTOs to clean"
type: watch
last_verified: 2026-07-30
---

# Watch list

- **Runs on the founder's physical iPhone (2026-07-29).** Deployed wirelessly to "Dean's iPhone 17 Air" — the `net.frostaura.trimix` provisioning profile was minted (paid team UX32XL7Q96, valid to 2027) and the signed device build installed + launched via `devicectl`. Full repeatable recipe (throwaway-Xcode-project → `xcodebuild -allowProvisioningUpdates` → `dotnet -p:CodesignProvision` → `devicectl install/launch`; unlock-the-phone trap) is in the `fa-trimix-build-environment` memory. This retires the long-standing "Apple paid provisioning + physical device" blocker for on-device StoreKit/biometric… — note biometric is now moot (auth removed), but StoreKit/real-vendor-endpoint testing on-device is now unblocked.
- **Durable (2026-07-30, verified by inspection):** the You-tab consolidation + demo removal + nav-freeze fix (`8ad5241`) and the Sites-capability removal (`399110b`) are committed and **pushed** — `main` == `origin/main` == `399110b`, 0 ahead / 0 behind, working tree clean (this memory-stamp commit aside), no `.git/*.lock` debris. Origin is HTTPS via `gh` (no SSH key on this Mac; switch back to SSH once a key is added). Repo is **private** `frostaura/fa.trimix` (Labs stealth-by-default satisfied). Re-run `repo-durability-check` before asserting git safety in a later session — this line decays.
- **Durability history:** `2151827` (serverless rebuild, pushed 2026-07-28) → `cb1d6c7` (memory) → `750ba82` (auth removal + visual overhaul + sheet polish, pushed 2026-07-29) → `8ad5241` (Settings→You consolidation + demo removal + nav-freeze fix, pushed 2026-07-30) → `399110b` (Sites capability removal, pushed 2026-07-30).
- **A fresh adversarial re-review of the rebuilt tree has not been run** — recommended before any M3 field testing, especially over the seven reverse-engineered connectors and the on-device entitlement path.
- **The whole Sites capability is gone (2026-07-29)** — no Sites tab, atlas, site detail, enrichment or auto-match. `Dive.SiteId` / `ImportResult.SiteMatched` linger as dormant always-null/false fields (no storage migration, since dives serialize as JSON); the frozen `site_*` funnel constants stay. See the Live-work section above.
- **Dead server DTOs** (`ApiRoutes.cs` incl. `ApiRoutes.Sites`, `PasskeyDtos`, `PrivacyDtos`, `ContributionDtos`/`SiteContribution*`) remain in `Trimix.Contracts` as frozen/orphan shapes; `network_security_config.xml` (dev-loopback) is now moot — cleanup candidates.
