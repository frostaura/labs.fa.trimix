# Garmin Dive private API — observed contract

_Established 2026-07-30 by mitmproxy capture of **Garmin Dive 3.6.1 (build 1) on iOS 27.0, iPhone18,4**, then
**verified live end-to-end** by `src/tools/Trimix.Harness`. Every endpoint and field below was observed on the
wire or returned to the harness — nothing here is inferred._

> This supersedes the researched-guess endpoints previously hardcoded in
> `Trimix.Core/Connectors/Cloud/GarminCloudSourceConnector.cs`. **All four of those were wrong**, and the FIT
> download path they assumed is unnecessary: Garmin exposes a first-class diving JSON API.

## Identifiers

| Thing | Value |
| --- | --- |
| SSO client id (web leg) | `DIVE_IOS_DARK` |
| OAuth client id (token leg) | `DIVE_MOBILE_IOS_DI` |
| Service URL | `https://mobile.integration.garmin.com/dive/ios` |
| Ticket grant type | `https://connectapi.garmin.com/di-oauth2-service/oauth/grant/service_ticket` |
| Basic auth | `base64("DIVE_MOBILE_IOS_DI:")` — client id, **empty secret** |

## Auth chain

### 1. `POST https://sso.garmin.com/mobile/api/login`

Query: `clientId=DIVE_IOS_DARK&locale=en-US&service=<urlencoded service URL>`
Content-Type: **`application/json`** (not a form; there is no CSRF token to scrape).

```json
{ "username": "...", "password": "...", "rememberMe": false, "captchaToken": "" }
```

Response:

```json
{
  "serviceURL": "https://mobile.integration.garmin.com/dive/ios",
  "serviceTicketId": "ST-0383050-LXbuWGrC5ql8Kl1eHFRC-sso",
  "responseStatus": { "type": "SUCCESSFUL", "message": "" },
  "responseReason": null,
  "customerMfaInfo": null,
  "consentTypeList": null,
  "captchaAlreadyPassed": false,
  "samlResponse": null,
  "authType": "CAS"
}
```

**`customerMfaInfo` and `captchaAlreadyPassed` are the interesting fields.** Both are benign for an
MFA-free account, and both are how Garmin would demand a second factor or a captcha. A pure-HTTP client
cannot satisfy either — see "Cloudflare" below.

The service ticket is **single-use and short-lived**. A ticket lifted from an old capture will not exchange.

### 2. `POST https://diauth.garmin.com/di-oauth2-service/oauth/token`

`Authorization: Basic base64("DIVE_MOBILE_IOS_DI:")`, `Content-Type: application/x-www-form-urlencoded`:

```
grant_type=<urlencoded ticket grant type>
&service_ticket=ST-...-sso
&service_url=<urlencoded service URL>
&client_id=DIVE_MOBILE_IOS_DI
```

Response: `access_token` (RS256 JWT), `refresh_token`, `token_type: bearer`,
`expires_in: 86399` (**24 h**), `refresh_token_expires_in: 2591999` (**30 days**), `scope`, `jti`.

JWT scopes include `DIVE_API_READ`, `DIVE_API_WRITE`, `CONNECT_READ`, `CONNECT_WRITE`;
`iss: https://diauth.garmin.com`, `garmin_guid` identifies the diver.

**The 30-day refresh token is what keeps the password out of storage** — exchange once, refresh for a month.

## Dive data — `https://gcs.garmin.com`

Auth on every call: `authorization: bearer <JWT>` (**lowercase `bearer`**, as the app sends).

| Endpoint | Returns |
| --- | --- |
| `GET /diving/v1/dive/summary?requestedPage=0&resultsPerPage=100` | paged dive list |
| `GET /diving/v1/dive/detail/{id}` | **one dive, full per-sample profile** |
| `GET /diving/v1/dive/thin/detail/bulk?diveActivityId=A&diveActivityId=B` | batched lightweight detail |
| `GET /diving/v1/dive/tags` | tag vocabulary + per-tag counts |
| `GET /diving/v1/gear/summary?...` | gear |
| `POST /diving/graphql/query` | Apollo GraphQL; operation in `x-apollo-operation-name` |

Observed non-endpoints (probed, do not retry): `GET /diving/v1/dive/{id}` → 405 ·
`GET /diving/v1/dive/detail/bulk?...` → 400 (the `thin` segment is required) ·
`GET /diving/v1/dive/{id}/samples` → 404.

### `dive/summary`

```json
{ "totalCount": 4, "pageSize": 100, "pageNumber": 0, "diveActivities": [ { ... } ] }
```

Each entry: `id` (**this is the `diveActivityId` for detail calls**), `connectActivityId`, `name`,
`diveType` (`SINGLE_GAS` …), `entryLoc {latitude, longitude}`, `number` (diver's dive number),
`startTime` (ISO-8601 **with offset**), `timezone` (IANA), `totalTime` / `bottomTime` (seconds, fractional),
`maxDepth` (metres), `diveTags`, `activitySource` (`GARMIN_DEVICE`), `gases[]`, `surfaceInterval`,
`contentVisibility`, `connectPrivacy`, and `deco` / `isDeco` present only on deco dives.

### `dive/detail/{id}`

~758 KB for a 100-minute dive. Top-level groups: `profile` (maxDepth, averageDepth, surfaceInterval,
bottomTime) · `location` (entryLoc, exitLoc) · `environment` (max/min/avg temperature, `waterType`,
`waterDensity`) · `equipment.gases[]` · `performance` (percentStart/EndCNS, percentStart/EndN2,
`o2Toxicity`, avg/maxPo2) · `config[]` (tissueModel, gfLow, gfHigh, conservatism, CCR set points) ·
`records` · `tankSensors[]` (air integration) · `deviceInfo` / `devices[]` · `diveSiteSummary` · `media`.

**`records` is the sample profile** and is columnar:

- `recordMetaDatas`: `[{ position, key }]` — the column schema.
- `recordData`: array of rows, values positional per that schema, `null` where absent.
- `numRecords`: row count (5581 for a 100-minute dive → **1 Hz**).
- `alerts`: `[{ timestamp, alertType, data[] }]` — e.g. `DIVE_GAS_SWITCHED`, `MIN_TEMP`, `MAX_DEPTH`,
  `TANK_PRESSURE_RESERVE`.

Observed keys, in order: `TIMESTAMP` (epoch **ms**, as a float) · `HR` · `TEMPERATURE` (°C) · `DEPTH` (m) ·
`NEXT_STOP_DEPTH` · `NEXT_STOP_TIME` · `TIME_TO_SURFACE` · `NDL_TIME` · `PERCENT_CNS_LOAD` ·
`PERCENT_N2_LOAD` · `PSAC` · `RMV` · `VSAC` · `AIR_TIME_REMAINING` · `PO2` · `ASCENT_RATE`.

**Read by `key`, never by hardcoded index.** The schema is self-describing precisely so Garmin can add
columns; index-based parsing would silently corrupt on the next app release.

## Rate limiting

**Garmin throttles repeated sign-ins.** Four logins within a few minutes from one address returned
**HTTP 429** on `/mobile/api/login`. It clears on its own after a short wait.

Two consequences, both already handled:

- The connector maps 429 → "temporarily rate-limiting", never "wrong password". Telling a diver their
  password is wrong when Garmin merely throttled us sends them off changing a password that was fine.
- The harness supports `TRIMIX_TOKEN_CACHE`, so dev iterations reuse the exchanged token instead of
  re-authenticating on every run. Use it.

## GraphQL — `POST /diving/graphql/query`

Apollo, with the operation in `x-apollo-operation-name`. Observed operations: **`Feed`**,
`DiveReadinessByDate`, `PlayerProfile`, `PlayerProfileImages`. **None of them is the dive list** — the
logbook comes from the REST `dive/summary` endpoint above.

`Feed` is the interesting one:

```graphql
query Feed($from: OffsetDateTime, $limit: Int, $allowNullEntryLoc: Boolean = true, $maxClusterSize: Int!) {
  feed(from: $from, limit: $limit) { ...FeedDive }
}
```

Observed variables: `{"from": "<now, with offset>", "limit": 20, "allowNullEntryLoc": true, "maxClusterSize": 250}`.

`FeedDive on Activity` inlines `bottomGas`, `bottomTime`, `connectActivityId`, **`decoEnabled`**,
`diveTags`, `diveType`, `freeDiveType`, `location{entryLoc,exitLoc}`, `heatmap`, `connectPrivacy`.
`decoEnabled` is notable: it is the deco flag, which the detail endpoint does **not** carry.

**`from` is a page-backwards anchor, not a since-filter.** The app passes the current instant and walks
backwards, so `feed` paginates rather than filtering to "newer than X". It is therefore *not* a
server-side incremental-sync parameter, and switching the listing to GraphQL would not by itself reduce
work. Worth revisiting only for `decoEnabled` and the richer single-call summary.

Incremental sync is instead achieved client-side, in two places:

1. `ListDivesSinceAsync` stops paging as soon as a page falls entirely before the cursor (the feed is
   newest-first), so an up-to-date logbook costs one request.
2. `ImportService` skips the **profile fetch** for any dive already on device, decided from the `DiveRef`
   alone via the deterministic id. This is the big one — a profile is ~750 KB, so without it an unchanged
   100-dive logbook would transfer ~75 MB per sync to learn nothing.

## Cloudflare

`sso.garmin.com` sits behind Cloudflare Bot Management. In the iOS capture the login POST carried
`cf_clearance` (minted by the WebView executing a `cdn-cgi/challenge-platform` JS challenge), `__cf_bm`,
`__cflb` and a `SESSION` cookie.

**A bare .NET `HttpClient` was nonetheless not challenged** — the harness logs in and reads dives with no
cookies beyond what the SSO page sets, direct, with .NET's own TLS fingerprint. So `cf_clearance` is not
currently required.

Treat that as a **fact about today, not a guarantee**. Cloudflare posture can change per-tenant without
notice, and it is invisible until it fails. Two independent reasons a WebView login leg is still wanted:
Cloudflare tightening, and `customerMfaInfo` / `captchaAlreadyPassed` becoming non-null for any diver with
MFA enabled — neither of which a pure-HTTP client can answer.

## Consequences for `Trimix.Core`

1. **Replace all four endpoint constants** in `GarminCloudSourceConnector.cs`; delete the CSRF-scrape +
   form-POST + `TicketPattern` regex approach — the real flow is JSON in, JSON out.
2. **Drop the FIT path for Garmin.** `FetchProfileAsync` should map `dive/detail/{id}` straight to
   `ImportedDive`. `IFitActivityParser` stays for connectors that genuinely need it.
3. **`connectapi.garmin.com` is not needed for the logbook** — neither is `connect2_exchange` nor
   `oauth-service/oauth/exchange/user/1.0`. The app calls those for profile/onboarding only.
4. **The token contract must widen.** `ExchangeCredentialsForTokenAsync` returns a single `string`; Garmin
   yields an access token **plus** a 30-day refresh token and two expiries. Storing only the access token
   throws away the refresh and forces a re-login every 24 h.
5. **`HttpClientTransport` needs three fixes** for this connector: no `AutomaticDecompression` (gzip bodies
   would decode as mojibake), no cookie container (the SSO leg needs `SESSION`), and it flattens repeated
   response headers with `string.Join(",")` which mangles multiple `Set-Cookie` values.
6. **Pagination** is `requestedPage` / `pageSize` / `totalCount` — loop until covered.
7. **Incremental sync is unproven.** No `since`/date parameter was observed on `dive/summary`; the old
   connector's `startDate` was a guess. Either probe for one or page and filter client-side.

## Reproducing

`src/tools/Trimix.Harness` (console, `net10.0`, references `Trimix.Core`). Read-only — it only GETs dives.

```bash
export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$DOTNET_ROOT:$PATH"
GARMIN_USERNAME=... GARMIN_PASSWORD=... \
TRIMIX_DUMP=/tmp/garmin-dump \
dotnet run --project src/tools/Trimix.Harness/Trimix.Harness.csproj
```

`GARMIN_TICKET=ST-…` skips the login leg (must be fresh). `TRIMIX_PROXY=http://127.0.0.1:8080` routes the
run through mitmproxy for observation.
