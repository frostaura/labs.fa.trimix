# Trimix (working name)

The scuba dive log that fills itself: it pulls your dives from wherever they already live — Apple HealthKit, vendor clouds, BLE-direct from the computer, standards files (FIT/UDDF/DL7) — normalizes them, and enriches them from a shared, community-moderated dive-site database.

> FrostAura Labs program `fa.trimix` (Gaia bet APP-03, Stage 5 vertical slice). Working name pending trademark/store clearance — see `docs/prd.md`.

## Layout
- `fa.trimix.slnx` — solution root.
- `src/app/` — .NET MAUI client. `net10.0-android` is the default target; `net10.0-ios` is opt-in via `-p:EnableIos=true`.
- `src/backend/` — .NET 10 + EF Core + PostgreSQL 17 API: accounts, server-authoritative entitlements + receipt validation, shared site DB + moderation, dive sync, analytics ingest. No PostGIS — geo is a bounding-box prefilter plus in-memory haversine (`Sites/GeoMath.cs`), so integration tests run hermetically.
- `src/shared/` — contracts shared by app and backend (DTOs, frozen funnel event names, SKU ids).
- `tests/` — `Trimix.Core.Tests` and `Trimix.Api.Tests`.
- `docs/` — spec stack (`prd.md`), architecture, design language (`design/`).

## Build (Android)
```bash
export DOTNET_ROOT="$HOME/.dotnet" PATH="$HOME/.dotnet:$PATH"   # dotnet with maui workloads
export JAVA_HOME="/opt/homebrew/opt/openjdk@17/libexec/openjdk.jdk/Contents/Home"   # 17, not 21 — 21 does not work here
export ANDROID_HOME="$HOME/Library/Android/sdk"
dotnet build src/app/Trimix.App/Trimix.App.csproj -f net10.0-android
```

Backend + Postgres:
```bash
docker compose up -d postgres   # the service is named postgres, not db
dotnet run --project src/backend/Trimix.Api
```

Tests:
```bash
dotnet test
```

## License
MIT — see `LICENSE`.
