---
name: labs-fa-trimix-gotchas
description: "Never `git clean` here: the three vendor contracts came from credentialed captures that were then deleted, so they are irreplaceable — and a live capture holds the founder's plaintext vendor password until it is swept."
type: gotcha
last_verified: 2026-11-20
---

# Gotchas — durability and capture hygiene

- **iCloud mount — check UNTRACKED files, not just modified ones.** One clone still lives on the iCloud Drive path (the dev clone is `~/src/labs.fa.trimix` since 2026-11-20), where an eviction can strip locally-present files, and this repo has already been within one `git clean -fd` of disaster: the harness and all three `docs/reverse-engineering/` contracts sat **never added to the index** for weeks while every "the tree is committed" claim counted modified *tracked* files and silently excluded them. They are tracked and pushed now ([`state.md`](state.md)). Run `git status --porcelain --untracked-files=all` here, always.
- **The three vendor contracts under `docs/reverse-engineering/` are not reproducible.** Each came from a mitmproxy capture of a vendor's own iOS app through the founder's personal account, and every capture was then securely deleted because it held his plaintext vendor password. Re-deriving one is not a rebuild — it is repeating a credentialed reverse-engineering session. Treat them as irreplaceable, never as regenerable docs.
- **Capture hygiene: sweep the scratchpad for `*.mitm` at session-close.** mitmproxy flow files hold the founder's vendor password + tokens in plaintext (login/verifyPassword flows). Delete every capture the moment its contract is banked. On 2026-08-07 a **stale ScubaPro capture** (LogTRAK password) was found still sitting in scratchpad from an earlier session — always `find <scratch> -name '*.mitm'` and `grep -rlIa 'verifyPassword\|sessionkey'` before ending a capture session.
- **No CI.** What it costs *this* program: every "green" — including the Core suite this store calls real and green — rests on a local `dotnet build`/`test` on one Mac with the user-local toolchain, so a gate is never met on that basis, and the vendor-contract fixtures nothing re-runs are exactly where a silent regression would hide. The division states the shared fact once: [`../../../memory/watch.md`](../../../memory/watch.md).

_Which connectors are actually verified, and how far: [`gotchas-connectors.md`](gotchas-connectors.md). Build, deploy and on-device verification: [`gotchas-build.md`](gotchas-build.md). Firebase/Firestore: [`gotchas-firebase.md`](gotchas-firebase.md). MAUI runtime UI: [`gotchas-ui.md`](gotchas-ui.md)._
