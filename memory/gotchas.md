---
name: fa-trimix-gotchas
description: "Durability + connector gotchas: commit/push promptly (iCloud eviction) and two dirs never tracked; no CI; which vendors are verified vs guessed; provider allowlist; capture hygiene; Firebase/Firestore reverse-engineering traps"
type: gotcha
last_verified: 2026-08-21
---

# Gotchas

- **iCloud mount — commit + push promptly.** The whole tree lives on the iCloud Drive path, where an eviction can strip locally-present files. Everything through `399110b` is pushed (see Watch list); everything after it is not, and two whole directories have never been tracked at all — read [`untracked-artifacts.md`](untracked-artifacts.md) before you believe any "the tree is committed" claim about this repo. Run the repo-durability check before asserting git safety.
- **No CI.** Every "green" rests on a local `dotnet build`/`test` on one Mac with the user-local toolchain. Never mark a gate met on that basis.
- **Garmin + ScubaPro verified end-to-end; Suunto supported at summary fidelity; the rest fixture-only.** Garmin (`docs/reverse-engineering/garmin-dive-api.md`) and ScubaPro/LogTRAK (`docs/reverse-engineering/scubapro-logtrak-api.md`, Firebase) ran end-to-end against the founder's own account (full profiles). **Suunto** has a live-VERIFIED sign-in + dive-list corridor (Sports Tracker `api.sports-tracker.com` + `STTAuthorization`; `docs/reverse-engineering/suunto-sports-tracker-api.md`) and is **supported**, but its depth/temp profile leg + diving activityId are UNVERIFIED (no real dive available) so `FetchProfileAsync` degrades to a **summary-only** dive — an account with no real dives lists nothing. Still fixture-only / researched-guess and Coming soon: Oceanic+, **Mares** (its MySSI-corridor assumption is now suspect — ScubaPro's identical assumption proved wrong), PADI/SSI push GraphQL. Do not generalise "full-fidelity" beyond Garmin + ScubaPro.
- **Provider availability is an allowlist, not a per-connector flag.** `ProviderCatalog.LiveConnectorIds` (currently `garmin_connect`, `scubapro`, `suunto_cloud`) is the ONLY thing that makes a provider connectable; everything else renders a disabled "COMING SOON" pill and `ConnectAsync` no-ops. Promote a connector by adding its id there once verified live — do not confuse this with the descriptor's `EnabledByDefault` (which is a legal-dark flag, always false for ToSRiskReverseEng).
- **A vendor cloud is a captured contract, not a guess — verify by capture, never assume a shared corridor.** Suunto's placeholder was wrong on host (`cloudapi.suunto.com` → really `api.sports-tracker.com`), auth (Bearer → really `STTAuthorization` session key) AND download path — exactly like ScubaPro's MySSI assumption was wrong. The diving `activityId` and per-dive sample endpoint can ONLY be learned from a real dive on the wire; an account with only manual test workouts cannot finish a dive connector.
- **Capture hygiene: sweep the scratchpad for `*.mitm` at session-close.** mitmproxy flow files hold the founder's vendor password + tokens in plaintext (login/verifyPassword flows). Delete every capture the moment its contract is banked. On 2026-08-07 a **stale ScubaPro capture** (LogTRAK password) was found still sitting in scratchpad from an earlier session — always `find <scratch> -name '*.mitm'` and `grep -rlIa 'verifyPassword\|sessionkey'` before ending a capture session.
- **On-device entitlements are piratable** (no server receipt validation) — the accepted serverless tradeoff.
- **Dive-row push destinations reflect *currently linked* push providers**, not a per-dive push ledger (same approximation the prototype makes).

## Added 2026-08-07 — Firebase / Firestore reverse-engineering (ScubaPro / LogTRAK)

- **A Firebase app's dive data will NOT appear in an HTTP proxy capture.** The native Firestore SDK speaks
  gRPC, which ignores the system HTTP proxy — so you see Firebase Auth and Storage flows but zero data, and
  crucially **no TLS failure either** (that combination is the signature, not a broken capture). Recover the
  data by calling the Firestore **REST** API directly with the captured refresh token — read-only, the
  founder's own account. This is why LogTRAK looked empty in the proxy while having 22 dives.
- **Firebase Web API keys are bundle-restricted.** A bare `identitytoolkit`/`securetoken` call returns
  `403 API_KEY_IOS_APP_BLOCKED`; it is unblocked by sending `X-Ios-Bundle-Identifier` (+ `X-Firebase-Gmpid`)
  headers matching the app. The key and project id are public (embedded in the app), not secrets.
- **Firestore `runQuery` with a `where` equality filter AND `orderBy` on a different field needs a composite
  index → HTTP 400** if the project hasn't defined one. Drop the `orderBy` and sort client-side; the
  unfiltered `where userId == uid` query returns the whole collection in one call.
- **Firestore security rules forbid an unfiltered collection LIST but allow a filtered query.** `GET /dives`
  and `:listCollectionIds` return `403 PERMISSION_DENIED` (list is admin-only); a `:runQuery` filtered to
  `userId == <your uid>` succeeds. Every owned-collection read must be that filtered query.
- **Firestore integers arrive as JSON STRINGS** (`{"integerValue":"22"}`), and LogTRAK stores several numbers
  and whole per-sample channels as `stringValue` (e.g. `o2Mixture` `"21"`, depth as `"[0,2.6,…]"`). Parse
  accordingly (`FirestoreValue.cs` handles the typed-value unwrap + JSON-string arrays).
- **A rotating refresh token must be persisted on every refresh.** securetoken may return a new
  `refresh_token`; the connector writes the merged envelope back each time. The captured token also rotates,
  so a stale copy stops working — mint fresh from the latest.

_MAUI runtime UI and interaction traps live in [`gotchas-ui.md`](gotchas-ui.md); build, deploy and on-device verification traps in [`gotchas-build.md`](gotchas-build.md). This file is durability, product, connector and reverse-engineering._
