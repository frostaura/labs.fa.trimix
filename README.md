# Trimix (working name)

A scuba dive log that fills itself — **serverless and on-device**. Your logbook and links never leave the phone.

Trimix links each dive platform's cloud directly from the device (per-user credential or OAuth), pulls your own dives in, normalizes them into one canonical dive, shows Oceanic+-grade stats, and pushes back out to certification logbooks (PADI, SSI).

> FrostAura Labs program `fa.trimix` (Gaia bet APP-03). Working name pending trademark/store clearance — see `docs/prd.md`.

## What it is

- **API-sync-only import** — no BLE, no file import, no manual entry, no HealthKit.
- **No backend, no accounts** — the phone is the account. Entitlements are on-device (StoreKit / Play Billing).
- **Read connectors** — Garmin and ScubaPro verified live; Suunto summary-only; Mares, Oceanic+ and Shearwater are fixture-only and gated as "Coming soon". Availability is `ProviderCatalog.LiveConnectorIds`.
- **Logbook, not deco** — imported NDL/ceiling/CNS are displayed, never computed.
- **Pricing** — free tier keeps the most recent dives; a single purchase moment offers $14.99 Pro or $29.99 Lifetime (one-off, no auto-renew).

Connectors are reverse-engineered private-cloud clients, labelled as user-assisted personal export (never "official"), opt-in behind a ToS-risk disclosure. Contracts live in `docs/reverse-engineering/`.

## Layout

| Path | Purpose |
|---|---|
| `src/app/Trimix.App` | .NET MAUI client (Android default; iOS via `-p:EnableIos=true`) |
| `src/app/Trimix.Core` | Domain, connectors, sync/merge, entitlements, analytics sink |
| `src/shared/Trimix.Contracts` | Shared contracts, frozen funnel event names, SKU ids |
| `src/tools/Trimix.Harness` | Dev-only console harness (not in the solution) |
| `tests/Trimix.Core.Tests` | Test suite |
| `docs/` | PRD, architecture, design, reverse-engineering contracts, known gaps |

## Build & test

```bash
export DOTNET_ROOT="$HOME/.dotnet" PATH="$HOME/.dotnet:$PATH"
export JAVA_HOME="/opt/homebrew/opt/openjdk@17/libexec/openjdk.jdk/Contents/Home"   # JDK 17
export ANDROID_HOME="$HOME/Library/Android/sdk"

dotnet test tests/Trimix.Core.Tests/Trimix.Core.Tests.csproj
dotnet build src/app/Trimix.App/Trimix.App.csproj -f net10.0-android -p:EmbedAssembliesIntoApk=true
```

`EmbedAssembliesIntoApk=true` is required when deploying to a device or emulator.

## License
MIT — see `LICENSE`.
