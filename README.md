# Trimix (working name)

The scuba dive log that fills itself: auto-imports dives from your dive computer over Bluetooth and enriches them from a shared, community-moderated dive-site database.

> FrostAura Labs program `fa.trimix` (Gaia bet APP-03, Stage 5 vertical slice). Working name pending trademark/store clearance — see `docs/prd.md`.

## Layout
- `fa.trimix.slnx` — solution root.
- `src/app/` — .NET MAUI client (`net10.0-android` first-class; `net10.0-ios` kept compiling).
- `src/backend/` — .NET 10 + EF Core + PostgreSQL (PostGIS) API: accounts, server-authoritative entitlements + receipt validation, shared site DB + moderation, dive sync, analytics ingest.
- `src/shared/` — contracts shared by app and backend (DTOs, frozen funnel event names, SKU ids).
- `docs/` — spec stack (`prd.md`), architecture, design language (`design/`).

## Build (Android)
```bash
export DOTNET_ROOT="$HOME/.dotnet" PATH="$HOME/.dotnet:$PATH"   # dotnet with maui workloads
export JAVA_HOME="/opt/homebrew/opt/openjdk@21/libexec/openjdk.jdk/Contents/Home"
export ANDROID_HOME="$HOME/Library/Android/sdk"
dotnet build src/app/Trimix.App/Trimix.App.csproj -f net10.0-android
```

Backend + Postgres:
```bash
docker compose up -d db
dotnet run --project src/backend/Trimix.Api
```

Tests:
```bash
dotnet test
```

## License
MIT — see `LICENSE`.
