---
name: fa-trimix-untracked-artifacts
description: "RED: the harness and all three captured vendor API contracts have NEVER been tracked by git — not ignored, just never added; losing them means re-capturing three vendor clouds with the founder's credentials"
type: alert
last_verified: 2026-08-21
---

# ⚠ The least reproducible files in this repo are not in git at all

Verified 2026-08-21 with `git ls-files` and `git status --porcelain --untracked-files=all`. Two whole directories have **never been added to the index**. They are not gitignored — `.gitignore` covers only build output, `.env` and IDE dirs — they were simply never committed:

- **`src/tools/Trimix.Harness/`** — 5 files (`Program.cs`, `ConnectorRun.cs`, `GarminDiveProbe.cs`, `HarnessStores.cs`, `Trimix.Harness.csproj`). This is the tool that drove every live vendor verification. `CLAUDE.md` documents it as though it exists in the repo. It is also absent from `fa.trimix.slnx`, so no build or restore touches it and nothing will notice it is gone.
- **`docs/reverse-engineering/`** — `garmin-dive-api.md`, `scubapro-logtrak-api.md`, `suunto-sports-tracker-api.md`. `CLAUDE.md` declares these the authoritative contract that connector fixtures are copied from and that a drifting fixture is a bug against. **All three exist only in this working tree.**

**Why this is the worst item in the program.** Every one of those contracts was obtained by a mitmproxy capture of a vendor's own iOS app, proxied through the founder's personal account, and every capture was then securely deleted because it held his plaintext vendor password. Re-deriving them is not a rebuild — it is repeating three credentialed reverse-engineering sessions. On an iCloud-synced volume where eviction is a known hazard and a catch-all commit has already recorded an eviction as a deletion elsewhere in this division, "exists only in the working tree" is not a safe state.

**Consequence for anything you read elsewhere:** every previous "the tree is committed / N paths dirty" claim about this repo counted modified *tracked* files and silently excluded these. Read `git status --porcelain --untracked-files=all` here, always.

Adding them is a normal `git add`, but committing this repo at all is waiting on founder authorisation (see `state.md`). Flag it; do not decide it.
