---
name: labs-fa-trimix-gotchas-firebase
description: "Firebase/Firestore traps from the LogTRAK capture: the native SDK speaks gRPC so an HTTP proxy sees nothing, keys are bundle-restricted, orderBy+where needs a composite index, list is denied but a filtered query is not."
type: gotcha
last_verified: 2026-10-14
---

# Gotchas — Firebase/Firestore

_Learned capturing ScubaPro's LogTRAK app; they apply to any Firebase-backed vendor reached through
[`FirebaseCloudClient`](../src/app/Trimix.Core/Connectors/Infra/FirebaseCloudClient.cs). Durability and
connector-verification traps: [`gotchas.md`](gotchas.md)._

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
