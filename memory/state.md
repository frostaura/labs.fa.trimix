---
name: fa-trimix-state
description: "Serverless, on-device, API-sync-only dive log; rebuilt 2026-07-27; pushed private; 322 Core tests green; verified on Android emu, iOS sim + physical iPhone 17 Air"
type: state
last_verified: 2026-07-30
---

# Current state

_Last verified: 2026-07-30 by the You-tab-consolidation + Sites-removal session. The Settings→You consolidation + demo-mode removal + nav-freeze fix are **committed `8ad5241` and pushed**; the **Sites-capability removal + You-page tweaks were committed and pushed** at the end of this session (exact SHA in the Watch list below). **322/322 Core tests pass** (343 → 336 after the You batch → 322 after Sites); both TFMs build 0 errors; deployed + verified on Android emulator, iOS sim, and physical iPhone 17 Air. Re-run `repo-durability-check` — the git line below decays._

**Rebuilt from a server-backed BLE aggregator into a serverless, on-device, API-sync-only dive log** matching the founder's 2026-07-27 prototype (`~/Desktop/Trimix Prototype.html`). **Committed and pushed 2026-07-28**: the rebuild is commit `2151827` on `main`, tracking `origin/main`, fully pushed. The GitHub repo **did not previously exist** (the `git@github.com:...` URL had been configured but never created); it was created **private** as `frostaura/fa.trimix` (default branch `main`) and `main` fast-forwarded over the two prior commits to include the rebuild. **The remote was switched from SSH to HTTPS** because this Mac has no SSH key — `gh` is authenticated as `frostaura` over HTTPS and is the working push path here; switch back to SSH once a key is added. Working tree clean; stray `.DS_Store` files removed.

Build evidence (local only, no CI): `Trimix.Contracts` + `Trimix.Core` + `Trimix.App`, **343 Core tests green** (was 345 before auth removal deleted `AuthViewModelTests`), Android + iOS TFMs both compile. Toolchain is user-local `~/.dotnet` (10.0.301) + OpenJDK 17 + Android SDK — the system dotnet cannot build the repo (missing workload manifests).
