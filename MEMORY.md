# MEMORY — fa.trimix

- [Live work — 2026-07-29 (You-tab consolidation + Sites capability removal)](memory/worklog.md) — Dated session logs 2026-07-29/30: You-tab consolidation + demo removal + nav-freeze fix; Sites capability removed; ALL auth removed (phone-is-the-account); sheet-overlay polish
- [Current state](memory/state.md) — Serverless, on-device, API-sync-only dive log; rebuilt 2026-07-27; pushed private; 322 Core tests green; verified on Android emu, iOS sim + physical iPhone 17 Air
- [What the rebuild did (2026-07-27, four phases)](memory/rebuild-2026-07-27.md) — The four-phase rebuild: backend/BLE/import teardown (201 files), connector data layer (7 providers via MySSI corridor), 11-screen UX rebuild, docs pass
- [Live decisions](memory/decisions.md) — Serverless pivot (reversed the parking); API-sync-only; connectors promoted to primary surface with guardrails; no auth of any kind; free tier = last 5 dives
- [Gotchas](memory/gotchas.md) — Commit + push promptly (iCloud eviction); no CI — every green is one Mac; connectors fixture-only, never imported a real dive; on-device entitlements piratable
- [Open questions](memory/questions.md) — M3 telemetry needs a thin beacon (the one plausible serverless exception); is the thin-TAM bet worth field-testing; name clearance; CAC placeholder unmeasured
- [Watch list](memory/watch.md) — Physical-iPhone deploy recipe retired the provisioning blocker; durability history of pushed SHAs; adversarial re-review recommended before M3; dead server DTOs to clean
