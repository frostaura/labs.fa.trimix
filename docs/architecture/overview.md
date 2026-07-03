# Trimix — Architecture (vertical slice)

Binding contract for all implementation work. When this doc and code disagree, stop and resolve the drift (spec-driven mandate).

## Solution layout

```
fa.trimix.slnx
Directory.Build.props            # net10.0 defaults, nullable enable, implicit usings;
                                 #   TreatWarningsAsErrors=true except Trimix.App (XAML tooling warnings exempt)
docker-compose.yml               # postgres:17 (port 5544 host — 5432/5433 taken on dev Mac)
src/
  shared/Trimix.Contracts/       # net10.0 — DTOs, enums, FROZEN funnel event names + required props,
                                 #   SKU ids, free-tier caps, API route constants. No dependencies.
  app/Trimix.Core/               # net10.0 — ALL client logic, no MAUI dependency:
                                 #   viewmodels, services, BLE vendor protocols, sandbox billing/transport,
                                 #   analytics queue, entitlement gate, local-store abstractions.
  app/Trimix.App/                # MAUI (net10.0-android;net10.0-ios) — XAML views, Shell nav,
                                 #   platform impls (SecureStorage, Preferences, BLE adapter, billing adapter),
                                 #   DI bootstrap, Resources (Sonar theme, icons, splash).
  backend/Trimix.Api/            # net10.0 — minimal API: auth, dives sync, sites + moderation,
                                 #   purchases/receipt validation, entitlements, analytics ingest.
tests/
  Trimix.Core.Tests/             # xUnit — viewmodels, protocol parser, caps/gates, analytics contract.
  Trimix.Api.Tests/              # xUnit — WebApplicationFactory integration tests (SQLite in-memory),
                                 #   including the END-TO-END FUNNEL RECONCILIATION test.
```

Project references: `App → Core → Contracts`; `Api → Contracts`; tests reference their targets (`Trimix.Api.Tests` may additionally reference `Trimix.Core` to drive the sandbox billing client in the funnel-reconciliation journey). Nothing references App.

## Cross-cutting decisions (locked)

1. **Server-authoritative entitlements.** The client never decides tier from a receipt; it POSTs the receipt, the server verifies + records, the client caches the server's answer (SecureStorage) and refreshes on launch/sign-in. Restore = same endpoint, idempotent.
2. **Billing abstraction.** `IBillingService` (Core) shaped to Play Billing semantics: `QueryProductsAsync(skuIds)`, `PurchaseAsync(skuId) → StoreReceipt{Platform, SkuId, Token}`, `RestoreAsync() → StoreReceipt[]`. Implementations: `SandboxBillingService` (Core; deterministic HMAC-signed tokens the API's `SandboxReceiptVerifier` validates — the slice's live path) and `PlayBillingService` (App/Android; real Play Billing wiring, compiled but inert until a Play listing exists — name-clearance gate). DI picks sandbox in Debug / `TRIMIX_SANDBOX_BILLING=1`.
3. **Receipt verification (Api).** `IReceiptVerifier` keyed by platform: `sandbox` (HMAC shared secret, default in dev), `google_play` / `app_store` (documented stubs returning NotConfigured — production wiring is post-slice). Verified receipt → upsert `Entitlement` keyed by user, record `entitlement_granted` or `entitlement_restored` (grant is first-time-per-receipt, restore is any subsequent reconciliation of the same receipt or a sign-in restore).
4. **BLE abstraction.** `IDiveComputerTransport` (scan → `DiscoveredComputer{Vendor, Model, Id}`, connect, read frame stream) with `SimulatorTransport` (deterministic fixture frames embedded in Core; default in Debug) and `BleTransport` (App/Android via Android BLE APIs; compiled, behind DI flag). `IDiveComputerProtocol` per vendor parses frames → `ImportedDive[]`; slice implements `ShearwaterProtocol` (pure C#, unit-tested against fixture frames; field-validation against real hardware is an M3 risk, stated honestly). `Suunto/Garmin/Mares` protocol classes exist as typed placeholders returning NotSupported so the abstraction is proven four-wide.
5. **Geo without PostGIS.** Sites store `lat`/`lng` doubles; search = bounding-box prefilter (SQL-translatable) + haversine ranking in memory. Rationale: slice-scale (≤ thousands of sites), keeps the EF model provider-portable so integration tests run hermetically on SQLite in-memory — no Docker dependency for QA. PostGIS upgrade documented as post-slice. PostgreSQL remains the canonical runtime DB (compose, Npgsql).
6. **Local-first dives.** Client saves dives to local SQLite (`sqlite-net-pcl`) immediately; `SyncService` pushes/pulls when online + authed (last-write-wins on `UpdatedAtUtc`). Guest mode = local-only; on account creation, local dives upload.
7. **Analytics.** `IAnalytics.Track(name, props)` validates against the frozen contract (unknown event name or missing required prop throws in Debug, drops+logs in Release), enqueues to local SQLite, batches to `POST /api/v1/analytics/events`. Server stores rows append-only. **Funnel reconciliation** is an Api integration test driving the full journey through public endpoints + client services and asserting the spine ordering per pseudo-user: `install → first_run_completed → dive_logged_* → paywall_shown → purchase_completed → entitlement_granted`, and reinstall: `install → entitlement_restored`.
8. **Auth.** Email+password: PBKDF2 via ASP.NET Core `PasswordHasher`, JWT bearer (HS256, 30-day access token — slice simplicity, no refresh tokens). Roles: `user`, `moderator` (seeded moderator account in dev). Guest = no token; local features only.
9. **Free-tier caps (Contracts constants).** `FreeImportDivesPerSession = 3`, `FreeLogbookVisibleDives = 25`, site contribution requires Pro/Lifetime (or verified — slice: Pro/Lifetime only). Cap enforcement lives in Core (`EntitlementGate`) so it's unit-testable; server enforces contribution gating authoritatively too.
10. **No map control in slice.** Sites = search list with distance from device location (JIT "while using" permission). Removes maps-API-key dependency; map view is post-slice.
11. **Profile chart** drawn with `GraphicsView`/`IDrawable` (Microsoft.Maui.Graphics) — no external chart package. Inverted-Y depth area chart per design language + accessible table alternative.
12. **Packages (pin majors; latest stable within major):** CommunityToolkit.Mvvm 8.x, sqlite-net-pcl 1.9.x, Npgsql.EntityFrameworkCore.PostgreSQL 10.x, Microsoft.AspNetCore.Authentication.JwtBearer 10.x, Microsoft.EntityFrameworkCore.Sqlite 10.x (tests), Microsoft.AspNetCore.Mvc.Testing 10.x, xunit 2.x. Avoid CommunityToolkit.Maui unless a concrete need appears. No Plugin.BLE / Plugin.InAppBilling — platform code is hand-rolled adapters (fewer compat risks on net10).

## API surface (v1, all under `/api/v1`)

| Method + route | Auth | Purpose |
|---|---|---|
| `POST /auth/register` `{email,password}` | — | Create account → `{token, user}` (`account_created` server-logged) |
| `POST /auth/login` `{email,password}` | — | → `{token, user}` |
| `GET  /entitlements/me` | user | `{tier}` — server truth |
| `POST /purchases/validate` `{platform,skuId,token}` | user | Verify receipt → upsert entitlement → `{tier, granted|restored}` |
| `POST /dives/sync` `{dives[], deletedIds[], since}` | user | Upsert client dives; returns server changes since |
| `GET  /sites?query=&lat=&lng=&limit=` | optional | Search (name/bounding box + haversine rank) |
| `GET  /sites/{id}` | optional | Site detail incl. approved community notes |
| `POST /sites/contributions` `{siteId?, payload}` | user+Pro | Enqueue moderation (`pending`) |
| `GET  /moderation/contributions?status=pending` | moderator | Queue |
| `POST /moderation/contributions/{id}/resolve` `{approved, note?}` | moderator | Apply/reject → `site_contribution_resolved` |
| `POST /analytics/events` `{events[]}` | optional | Append-only ingest (pseudo-id allowed pre-auth) |
| `GET  /healthz` | — | Liveness + db check |

## Data model (EF, provider-portable)

`User(Id, Email unique, PasswordHash, Role, CreatedAtUtc, Verified)` · `Entitlement(UserId PK/FK, Tier, SourceReceiptId, Platform, GrantedAtUtc)` · `ReceiptRecord(Id, UserId, Platform, SkuId, Token unique, FirstSeenUtc)` · `Dive(Id GUID client-generated, UserId, StartUtc, DurationSec, MaxDepthM, AvgDepthM, MinTempC, GasMix, Source, SourceVendor, SiteId?, ConditionsJson, GearCsv, BuddiesCsv, Notes, Rating, UpdatedAtUtc, Deleted)` · `ProfileSample(DiveId, TSeconds, DepthM, TempC?, PpO2?)` composite PK · `Site(Id, Name, Lat, Lng, DepthMinM, DepthMaxM, Type, HazardsCsv, MarineLifeCsv, AccessNotes, Status, Source)` · `SiteContribution(Id, SiteId?, UserId, PayloadJson, Status, ModeratorId?, ResolvedAtUtc?, Note?)` · `AnalyticsEvent(Id, UserPseudoId, UserId?, Name, PropsJson, ClientTimestampUtc, ReceivedAtUtc, Platform, AppVersion)`.

JSON columns are plain `string` (portable). CSV columns are simple joined lists (slice scale).

## App structure (Shell)

Tabs: **Logbook · Sites · Import (center action) · Settings**; modal routes: onboarding (first run), auth, manual-entry, dive-detail, site-detail, contribute, paywall. Every page = XAML view + Core viewmodel (constructor-injected services, `[ObservableProperty]`/`[RelayCommand]`). Accessibility: SemanticProperties on all interactive elements, dynamic font scaling, ≥44pt targets, profile table alternative.

## Definition of done (Silver, adapted)
`dotnet format --verify-no-changes` clean · `dotnet build` (full solution incl. net10.0-android) clean · `dotnet test` green (Core + Api incl. funnel reconciliation) · Debug APK produced · funnel spine + reinstall-restore proven by tests · README run-instructions accurate.
