# Suunto app cloud (Sports Tracker) — observed contract

_Established 2026-08-07 by mitmproxy capture of the **Suunto iOS app (build 23009) on iOS/Darwin 27.0**,
proxied through the founder's own account (`suunto@frostaura.net`). Every endpoint, header and field below
was observed on the wire. **Two legs are verified (login, list); the dive-profile leg is NOT** — see the
blocker._

> **Supported at summary fidelity (founder decision 2026-08-07: "support what we have").**
> `Trimix.Core/Connectors/Cloud/SuuntoCloudSourceConnector.cs` implements the **verified** login + list
> corridor, so `suunto_cloud` IS in `ProviderCatalog.LiveConnectorIds` (connectable). The diving-`activityId`
> filter and the per-dive profile-fetch endpoint are still **unconfirmed** (the captured account had no real
> scuba dive), so `FetchProfileAsync` **degrades to a summary-only dive** rather than failing. Confirm both
> against a real dive to lift fidelity from summary to full depth/temp — see "To finish".

> **This contradicts the placeholder that shipped before.** The old connector assumed
> `cloudapi.suunto.com` with an OAuth resource-owner-password grant (Bearer token) and a `/v2/workout/exportFit`
> download. **All three are wrong.** The Suunto app's real backend is **Sports Tracker**
> (`api.sports-tracker.com`, Suunto-owned), auth is a **session key in an `STTAuthorization` header** (not
> Bearer), and the workout list is `/apiserver/v1/workouts`. This is the same "verify by capture, do not
> assume a shared corridor" lesson that corrected ScubaPro.

## The backend is Sports Tracker

The Suunto app rides `api.sports-tracker.com/apiserver/…`. There is **no certificate pinning** on these hosts
— auth and all API JSON flowed cleanly through mitmproxy (the only non-decryptable traffic was vector map
tiles on `suuntomaps.b-cdn.net`, irrelevant to dives). The client `User-Agent` is `Suunto/23009 CFNetwork/…`.

## Auth — `STTAuthorization` session key

**VERIFIED.** Login is form-encoded, not OAuth:

```
POST https://api.sports-tracker.com/apiserver/v1/login2
Content-Type: application/x-www-form-urlencoded

l=<url-encoded email>&p=<url-encoded password>&version=2
```

Response (200, `application/json`) — abridged, secrets redacted:

```json
{
  "username": "dean360451",
  "userKey": "<32-char>",
  "sessionkey": "<32-char>",     // == userKey in this response
  "realName": "Dean",
  "country": "US",
  "emailVerified": true,
  "key": "6a75e7ffe61bfc503b404892"   // the user's document id (Mongo ObjectId), NOT the session key
}
```

The **`sessionkey`** (32 chars) is then sent on **every** subsequent request as:

```
STTAuthorization: <sessionkey>
```

(Not `Authorization: Bearer …`.) The password is used once at login and never again — the connector persists
only the session key, in the secure token store.

## Dive list — `GET /apiserver/v1/workouts`

**VERIFIED (shape).** The logbook loads via:

```
GET https://api.sports-tracker.com/apiserver/v1/workouts?since=<ms>&offset=0&limit=50
STTAuthorization: <sessionkey>
```

`since` is epoch **milliseconds** (0 for a full sync). Response:

```json
{
  "error": null,
  "payload": [
    {
      "workoutKey": "6a75ebc7e61bfc503b405d8a",   // the id used by /workouts/{key}/... sub-resources
      "activityId": 78,                            // numeric activity type (see blocker: diving id unknown)
      "startTime": 1786112916422,                  // epoch MILLIS
      "stopTime":  1786118316422,                  // epoch MILLIS
      "totalTime": 5400.0,                         // SECONDS (float)
      "startPosition": { "x": 31.0758, "y": -29.7223 },  // x=lon, y=lat
      "polyline": "~cltDiot|D",
      "isManuallyAdded": true
    }
  ],
  "metadata": { "workoutcount": "1", "until": "1786112971226" }
}
```

Incremental sync: the app feeds `metadata.until` (ms) back as the next `since`. Per-workout social
sub-resources exist and were observed but are irrelevant to dive import:
`GET /apiserver/v1/workouts/reactions/{key}`, `GET /apiserver/v1/workouts/comments/{key}`.

## The blocker — no real dive in the account

The captured account held **only manually-created test workouts** (`activityId: 78` "Hello description"
tagged COMMUTE/MARATHON, and `activityId: 1` "Example of non dive description"), both `isManuallyAdded: true`.
Consequently **two dive-critical facts were never observed** and remain unverified:

1. **The diving `activityId` value(s).** Needed to filter dives from runs/rides in the list.
   `SuuntoCloudSourceConnector.DivingActivityIds` currently holds `{82, 83}` as an UNVERIFIED placeholder.
2. **The per-dive depth/temperature sample-fetch endpoint and schema.** A manual entry has no profile, so the
   app never fetched one. `SuuntoCloudSourceConnector.FetchProfileAsync` hits an UNVERIFIED placeholder
   (`/apiserver/v1/workout/exportFit/{key}`) and decodes via the shared FIT parser — do not trust it.

## To finish (resume plan)

Re-run the capture with **a real scuba dive present** in the Suunto app account, then:
- Open the dive list and confirm the real diving `activityId` on the wire → set `DivingActivityIds`.
- Open the dive's **profile/graph** and capture the sample-fetch call → replace `FetchProfileAsync`'s endpoint
  + response mapping (depth/temp at minimum; confirm whether tank/gas are really stripped, which sets whether
  `Fidelity` stays `DepthTempOnly`).
- Only then add `suunto_cloud` to `ProviderCatalog.LiveConnectorIds` to lift it out of "Coming soon".

## Guardrails (unchanged)

Descriptor stays `ToSRiskReverseEng` + `EnabledByDefault=false`; zero network I/O before per-user consent
behind the plain-language disclosure; the password is never persisted (only the session key); labelling is
"Suunto app (personal export)", never an official Suunto integration.
