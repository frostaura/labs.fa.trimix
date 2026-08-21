---
name: fa-trimix-decisions-connectors
description: "Per-vendor reverse-engineering decisions with rationale: Garmin verified and needs no FIT, ScubaPro is LogTRAK/Firebase not MySSI, Suunto is Sports Tracker not cloudapi, Aqualung/DiverLog evaluated and rejected, the sign-in page must not impersonate a vendor"
type: decision
last_verified: 2026-08-07
---

# Connector / reverse-engineering decisions

_Split out of [`decisions.md`](decisions.md). One rule generalises: **every shared-corridor assumption made in this program has
turned out wrong when finally captured** — treat any vendor without a document under `docs/reverse-engineering/` as
unimplemented. Original dates; not re-verified 2026-08-21._

## Garmin — 2026-07-30, the first live capture

- **Connectors stopped being fixture-only.** The Garmin Dive private API was captured and verified live against the founder's
  own account; `docs/reverse-engineering/garmin-dive-api.md` is the contract and fixtures are copied from it, never invented.
  **This reversed the "never exercise a connector against a real vendor production server" invariant** — a founder decision,
  taken because the alternative was shipping connectors nobody had ever seen work. Read-only, founder's own dives only.
- **Garmin needs no FIT decode** — the diving API returns the 1 Hz profile as JSON, so `FetchProfileAsync` maps it directly.
  `IFitActivityParser` stays for connectors that genuinely need it.
- **Auth stays `HttpClient` for now, WebView next.** Raw `HttpClient` login provably works (Cloudflare did not challenge .NET's
  TLS fingerprint). The response carries `customerMfaInfo` and `captchaAlreadyPassed`, neither satisfiable by a pure-HTTP
  client, so **any diver with MFA is unsupported by design**, reported as such rather than failed opaquely.
- **The provider sign-in page no longer impersonates the vendor** — legal risk, not cosmetics. Fake browser chrome around a
  native Trimix form capturing the password into our process misrepresents origin, breaches both stores' deceptive-behaviour
  rules and reads as credential phishing. A test asserts the caption does NOT contain "own sign-in page"; chrome may return
  with the WebView, driven by its real URL.

## ScubaPro — 2026-08-07, LogTRAK / Firebase, not MySSI

- **ScubaPro has its own Firebase backend.** Captured live from LogTRAK 2 v1.13.1: project `logtrak2-production`, Firebase Auth
  + Cloud Firestore (`dives`, filtered by `userId`). This **reversed the previous design**, in which the connector
  authenticated MySSI credentials through the shared `MySsiCloudClient` — an assumption never verified and simply wrong:
  LogTRAK never touches MySSI, and SSI is a *push* target it exports to. **Consequence: the Mares-via-MySSI assumption is now
  also suspect** and must be captured before first live contact. Contract: `docs/reverse-engineering/scubapro-logtrak-api.md`.
- **`FirebaseCloudClient` (`Connectors/Infra/`) is the reusable seam** — Firebase sign-in + securetoken refresh + Firestore
  `runQuery`, parametrized by a `FirebaseAppConfig`, so a second Firebase-backed vendor is a new config, not a new client.
- **The connector id stays `scubapro`** even though the backend is LogTRAK: the product identity is ScubaPro, and keeping the
  id avoids churning `ProviderCatalog`, the frozen funnel and existing consent state. The ToS disclosure names LogTRAK.

## Suunto — 2026-08-07, Sports Tracker; founder promoted it to SUPPORTED

- **The real backend is Sports Tracker** (`api.sports-tracker.com`), **not** `cloudapi.suunto.com`, and auth is a **session key
  in an `STTAuthorization` header**, not OAuth Bearer — the placeholder was wrong on host, auth AND download path, the same
  lesson as ScubaPro. Verified corridor: `POST /apiserver/v1/login2` → `sessionkey`, then
  `GET /apiserver/v1/workouts?since={ms}&offset=0&limit=50`. Contract: `docs/reverse-engineering/suunto-sports-tracker-api.md`.
- **Founder decision, "support what we have" → SUPPORTED, not Coming soon.** A data-completeness gap is not the same as
  unsupported: sign-in and dive-list are verified, so it connects and syncs, and `FetchProfileAsync` **degrades to a
  summary-only dive** instead of failing. The founder's account held no real scuba dive, so the **diving activityId** and the
  **depth/temp sample endpoint** stay UNVERIFIED guesses — confirm both against a real dive to lift fidelity summary→full.

## Aqualung / DiverLog — 2026-08-07, evaluated and REJECTED

After a full mitmproxy capture the founder abandoned it and its only code trace (the dead `["aqualung"]` badge label in
`DiveMergePolicy`) was removed. The app is **DiverLog+** (Pelagic) on `divecloud.net`, a backup-sync CGI API rather than a read
API: `init.py` login puts email+password **in a plain-HTTP URL**, `fileList.py` only echoes what you send so **no server-side
"list my dives" call exists for a fresh client**, and the DL7 profile `download.py` rejects every parameter shape
(`ERRCODE=2`), revealing itself only during a true restore that cannot be forced. Hardest backend hit, marginal value, not in
the slate → **do not re-attempt without a genuine empty-app-restores-from-cloud capture.**
