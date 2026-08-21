# ScubaPro / Uwatec LogTRAK cloud — observed contract

_Established 2026-08-07 by mitmproxy capture of **LogTRAK 2 v1.13.1 (build 402) on iOS 27.0, iPhone18,4**,
then **verified live end-to-end** by calling the backend directly with the captured session. Every endpoint,
header and field below was observed on the wire or returned by a live read — nothing here is inferred._

> **Implemented.** `Trimix.Core/Connectors/Cloud/ScubaProCloudSourceConnector.cs` (backed by the reusable
> `Connectors/Infra/FirebaseCloudClient.cs`) implements this contract and was verified live against the
> founder's own account via `src/tools/Trimix.Harness` (`TRIMIX_MODE=scubapro`): 22 real dives listed, a full
> 311-sample profile fetched. This document is the contract; the connector fixtures are copied from it.

> **This contradicts the project's stated architecture.** `Trimix.Core` currently assumes Mares, ScubaPro
> and SSI all route through a shared `Connectors/Infra/MySsiCloudClient.cs` (the "MySSI corridor"). That is
> **wrong for ScubaPro ingestion.** LogTRAK has its **own Firebase backend** (`logtrak2-production`); SSI is a
> *push target* it integrates with via OAuth, not the source of its dives. See "Consequences" below.

## The backend is Firebase — and why the proxy alone was not enough

LogTRAK is a Firebase app. The capture immediately showed **Firebase Auth** (Google Identity Toolkit) and
**Firebase Storage** (profile picture), but **no dive data at all** — and, tellingly, **no TLS failure** for a
data host either. That combination is the signature of **Cloud Firestore over gRPC**: the native Firebase
Firestore SDK speaks gRPC/HTTP-2, which **does not honour the system HTTP proxy**, so the dive traffic bypassed
mitmproxy entirely rather than failing.

The dives were recovered by calling the **Firestore REST API** directly with the captured refresh token — the
same read-only, founder's-own-account method sanctioned for Garmin. This is actually *more* reliable than a
proxy capture would have been, because Firestore's REST surface is a stable, documented Google API.

There is **no certificate pinning** (auth flowed through the proxy cleanly). The only obstacle is the
API-key bundle restriction (below).

## Identifiers (all public — embedded in the app binary, not secrets)

| Thing | Value |
| --- | --- |
| Firebase project id | `logtrak2-production` |
| Firebase project number / GMP sender | `367468051507` |
| iOS GMP app id | `1:367468051507:ios:b01cd5cd7305d94328b746` |
| Web API key | `AIzaSyB1RncixeW_92_GtPiLlef7PL1l34mgXcM` |
| Storage bucket | `logtrak2-production.appspot.com` |
| iOS bundle id | `com.uwatec.LogTrak2` |

**The Web API key is bundle-restricted.** A bare call returns `403 API_KEY_IOS_APP_BLOCKED`. It is unblocked by
sending the header the app sends:

```
X-Ios-Bundle-Identifier: com.uwatec.LogTrak2
X-Firebase-Gmpid: 1:367468051507:ios:b01cd5cd7305d94328b746
```

There is no App Check token on these calls — the bundle header alone is sufficient. (An `X-Firebase-AppCheck`
header was never observed.)

## Auth chain — Firebase Auth email/password

### 1. Sign in — `POST https://www.googleapis.com/identitytoolkit/v3/relyingparty/verifyPassword?key=<API_KEY>`

The app uses the legacy `relyingparty` surface; the modern equivalent is
`https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=<API_KEY>` and is interchangeable.

Request body:
```json
{ "email": "<email>", "password": "<password>", "returnSecureToken": true, "clientType": "CLIENT_TYPE_IOS" }
```
Response (trimmed): `{ "localId": "<uid>", "idToken": "<jwt, ~950 chars>", "refreshToken": "<~247 chars>", "expiresIn": "3600", "registered": true }`

- `idToken` is a 1-hour Firebase JWT; it is the **Bearer** for every Firestore/Storage call.
- `localId` is the Firestore **`userId`** used to filter dives.
- MFA was not present on the test account; the response carried no `mfaPendingCredential`. Unknown whether
  LogTRAK offers MFA — treat its presence as unsupported-by-design until observed, as with Garmin.

### 2. Refresh — `POST https://securetoken.googleapis.com/v1/token?key=<API_KEY>`

Body `{ "grant_type": "refresh_token", "refresh_token": "<token>" }` → `{ "id_token", "refresh_token", "expires_in": "3600", "user_id" }`.
**The refresh token rotates** on some responses — always persist the returned one. Holding the refresh token
means durable access without re-authenticating (avoids any sign-in rate limit).

## Dive data — Cloud Firestore REST

Base: `https://firestore.googleapis.com/v1/projects/logtrak2-production/databases/(default)/documents`
Auth: `Authorization: Bearer <idToken>` + the two bundle headers above.

### The user document — `GET {base}/users/{uid}`

Readable directly. Holds preferences (units, temperature scale, `initialLogbookNumber`), the device list, and
two integration blocks: `ssi.oauth` and `avelo.oauth`, each `{ authorizationState, authorizationDate,
lastSyncDate }`. **`ssi.oauth.authorizationState` confirms SSI is an OUTBOUND integration LogTRAK pushes to** —
not the corridor Trimix pulls ScubaPro through.

### The dives — `POST {base}:runQuery`, collection `dives`, filtered by `userId`

**Security rules forbid listing.** A bare `GET {base}/dives` (or `listCollectionIds`) returns
`403 PERMISSION_DENIED`; `listCollectionIds` is admin-only. What the rules **do** allow is a query filtered to
your own `userId`. This is the load-bearing call:

```json
{ "structuredQuery": {
    "from": [{ "collectionId": "dives" }],
    "where": { "fieldFilter": {
        "field": { "fieldPath": "userId" }, "op": "EQUAL", "value": { "stringValue": "<uid>" } } }
} }
```

Returns one JSON object per dive in an array (`[{ "document": { "name": ".../dives/<docId>", "fields": {…} } }, …]`),
interleaved with document-less stream frames (`{ "readTime": … }`) that must be skipped. The test account
returned **22 dives**.

- **Do NOT add `orderBy` to this query.** An equality filter on `userId` combined with `orderBy(startTime)`
  needs a composite index that `logtrak2-production` does not expose, and Firestore rejects it with **HTTP
  400** (confirmed live). The unfiltered `where userId == uid` query returns everything in one shot, so the
  connector pulls the whole logbook and orders/filters client-side.
- **The full profile is IN the list document** — every `…Values` channel and the tank maps are already
  present, so there is **no separate detail call**. The connector caches the list documents and maps each
  dive from cache. No server-side `since` filter was found; the cursor is applied client-side by `startTime`.

## Dive document schema

**91 top-level fields.** The profile is already decoded: each `…Values` field is a **`stringValue` containing a
JSON array** (not a Firestore array), and parallel channels share one index. A raw `diveLogBase64` blob (~3 KB,
the Uwatec log) is also present but **not needed** — the decoded channels are provided, exactly as Garmin
returns decoded JSON instead of requiring FIT parsing.

Key fields for the connector:

| Field | Meaning |
| --- | --- |
| `userId` | Firebase uid — the query filter |
| `startTime` / `endTime` / `diveLogStartTime` | ISO-8601 UTC; `utcDifferenceMinutes` gives the local offset (e.g. 120) |
| `depthMetersValues` | JSON-string array; the depth profile. Test dive = **928 samples** over 56 min ⇒ ~3.6 s cadence. `intervalMinutes` was `0` — derive cadence from `(endTime−startTime)/(n−1)`, do not trust `intervalMinutes` |
| `depthMetersMax` / `depthMetersAvg` | doubles, metres |
| `waterTempCelsiusValues` | JSON-string array, **same length as depth** (parallel channel); `waterTempCelsiusMin/Max` scalars |
| `skinTempCelsiusValues`, `heartRateBpmValues`, `spmValues`, `altitudeMeterValues`, `compassBearing` | more parallel channels — **all empty on the test dive** (Galileo without HR belt); the connector must tolerate `"[]"` |
| `tankData` | map `tank1..tank10`; each non-null tank: `{ startPressure, endPressure (bar), o2Mixture, heMixture, maxPp02Limit, minPp02Limit, size, type, aggregatedAmbientPressure, pairingAddress }`. **`o2Mixture` is the gas** ("21" = air) |
| `tankPressureValues` | map `tank1..` → JSON-string array of the per-sample tank pressure (Galileo T with a transmitter) |
| `diveMode` | e.g. `scuba` (also apnea/gauge expected) |
| `diveComputerId` | Firestore id of the paired computer (a separate doc, not fetched) |
| `cnsO2Before/After`, `otuAfter` | oxygen tox tracking |
| `noFlyBeforeMinutes`, `repetitiveNumber`, `mbLevel`/`finalMbLevel` (microbubble level), `altitudeClass`, `saltwaterCalibrated` | dive-planning / physiology context |
| `warnings` | array of strings, e.g. `["ascent"]` |
| `flags` | map of parallel per-sample event arrays (`attentions`, …) — Uwatec's alarm lane, the analogue of Garmin's event list |
| `decoAlgorithm`, `gfLow/HighStart/Final` | deco model + gradient factors — **all null on the test dive** (Uwatec's ZH-L16 ADT MB uses microbubble levels, not GF; expect these populated only in GF mode) |
| `diveSiteId/Name/Source`, `locationLat/Lng/Name`, `notes`, `rating`, `buddyNames`, `guideNames`, `images` | user metadata — mostly null/empty on a freshly-synced dive |
| `parseVersion` (18), `triggerMigrationAt`, `railsId`, `lastSendToSsi`, `lastSendToDiveWithBuddy` | LogTRAK bookkeeping — ignore |

The full 91-field type map is preserved beside this doc's session capture; the table above is the subset the
connector needs.

## Consequences for `Trimix.Core`

1. **ScubaPro is its own connector, not a MySSI passthrough.** The current design (`MySsiCloudClient` serving
   Mares + ScubaPro + SSI) is wrong for ScubaPro. LogTRAK/Uwatec runs on Firebase (`logtrak2-production`);
   build a `LogTrakCloudSourceConnector` (or a shared `FirebaseFirestoreClient` seam) against the contract
   above. **Whether Mares also has its own Firebase backend is now an open question** — the MySSI assumption
   should not be trusted for Mares either until captured.
2. **SSI is a push destination, not a pull source.** `ssi.oauth` on the user doc and `lastSendToSsi` per dive
   are LogTRAK→SSI export state. This aligns with SSI being an OUT provider in the slate, but means the MySSI
   corridor — if it exists at all — is about *pushing to SSI*, not *pulling ScubaPro*.
3. **No FIT/blob decode needed.** Read the `…Values` JSON-string arrays directly; ignore `diveLogBase64`.
4. **Reuse the Firebase auth seam.** Auth is generic Firebase (verifyPassword → idToken → securetoken refresh)
   and would serve **any** Firebase-backed vendor. The only per-vendor values are the project id, API key,
   GMP app id and bundle id in the Identifiers table.
5. **`HttpClientTransport`**: send JSON, send the two bundle headers on the auth calls, and Firestore responses
   are gzipped — decode content-encoding (the same trap the Garmin connector documents).
6. **Read by key, tolerate absence.** Every channel except depth/water-temp was empty on the test dive; treat
   all `…Values` and every nullable scalar as optional.

## Reproducing

Read-only; the founder's own account only, per the connector guardrails and the 2026-07-30 live-vendor carve-out.

1. Point the iPhone's Wi-Fi proxy at a mitmproxy instance; trust its CA. Sign out of LogTRAK, then sign in and
   open the logbook. The **auth** flow (verifyPassword + securetoken + a `getAccountInfo`) appears in the proxy.
2. The **dives do not** — they arrive over Firestore gRPC. Extract the `refresh_token` from the captured
   securetoken/verifyPassword body, then call `securetoken` → `idToken` → the `dives` `runQuery` above with the
   two bundle headers. That is the whole ingestion path.
