# Trimix — Mockup 1:1 Spec (authoritative)

> **Update 2026-07-27 (serverless rebuild).** The screen list in §4 was re-reconciled against the
> founder's interactive prototype `~/Desktop/Trimix Prototype.html` — **11 screens**, replacing the
> storyboard-era set (BLE import, email/passkey account, moderated site contribution are gone).
> Palette, typography and components (§1–§3) are **unchanged**: mint `#2FE3C4` on `#0A1013`,
> Space Grotesk / JetBrains Mono / Hanken Grotesk, 3-tab **Log · Sites · You**.

> **Provenance.** RECONCILED 2026-07-03 against the actual storyboard CSS — `Wreckline
> Storyboard.dc.html` (project `fb253415-935b-43f4-af5e-9f8f11d849a4`), pulled via DesignSync after
> `/design-login`. The exact Sonar-(A) values below are now wired into `Colors.xaml` / `Styles.xaml` /
> `MauiProgram.cs` and are the single source of truth for the visual layer. These **supersede** the
> earlier re-derived `sonar-design-language.md` (which guessed cyan-on-blue) AND the first-pass
> eyeballed values (mint `#5FE3C6`, Space Mono) — the real design is:
>
> - **Palette (dark only; app forced Dark):** mint **`#2FE3C4`** (token `Sonar`) on **`#0A1013`**
>   (`Abyss`); cards **`#0D171B`** (`Surface`) / border **`#1C2A2E`** (`GlassStroke`); raised
>   **`#15262B`** (`SurfaceRaised`); ink-on-mint **`#04231F`** (`MintInk`); text **`#E8F1F2`** /
>   **`#9FB3B7`** / **`#5C6E72`** (`TextPrimary`/`Secondary`/`Disabled`); warn amber **`#F5B54A`**
>   (`Warn`); highlighted-SKU accent = mint (`PaywallGold`→`#2FE3C4`).
> - **Type:** **Space Grotesk 700** = headings/titles/button labels; **JetBrains Mono** = wordmark,
>   tracked UPPERCASE eyebrows, badges, index (01/02/03) + all stat numerals/prices; **Hanken
>   Grotesk 400** = body/captions/quotes. (Archivo/Newsreader in the storyboard belong to the other
>   two directions, not Sonar A.)
>
> Colors/fonts flow through **existing resource keys** (values changed, keys preserved) so pages
> re-skin without rebinding. The token/type tables in §1–§2 below are the original eyeball pass and
> are retained for structure; where a hex or font differs from this callout, THIS callout wins.

---

## 1. Color tokens (resource keys unchanged; values = mockup). Dark theme primary.

| Key | Hex | Role in mockups |
|---|---|---|
| `Abyss` | `#0B0F0E` | App background — neutral near-black, faint cool-green. Button text ON mint. |
| `Depth` | `#0C100F` | Tab bar / nav bar (seamless with page; no visible seam). |
| `Surface` | `#141917` | Glass cards (The-loop cards, SKU cards, stat cards) — subtle lift only. |
| `SurfaceRaised` | `#1B211E` | Chips, selected fills, thumbnail placeholders. |
| `GlassStroke` | `#28302D` | 1px hairline card/chip borders, dividers. Very subtle. |
| `Sonar` | `#5FE3C6` | **THE mint.** All CTAs, active tab, sonar rings, chart line, key numerals, checkmark disc, links, badges' accent, selected SKU border. |
| `SonarDeep` | `#41B79E` | Pressed CTA / secondary accent. |
| `Ice` | `#BFF3E7` | Pale-mint highlight (rare); selected-chip text. |
| `TextPrimary` | `#F2F5F3` | Headlines, titles, primary values, white numerals. |
| `TextSecondary` | `#8A938F` | Body copy, subtitles, mono labels, inactive tabs. |
| `TextDisabled` | `#565E5B` | Disabled, faint section eyebrows. |
| `Signal` | `#5FE3C6` | Success = same mint (import complete, "You're Pro" check). |
| `Warn` | `#DDA85C` | Amber: "⚠ current", "FREE LIMIT REACHED", hazard chips. |
| `Alert` | `#F4766B` | Error/destructive (not shown in mockups; unchanged). |
| `PaywallGold` | `#5FE3C6` | **Now mint** — the highlighted (Lifetime/BEST VALUE) SKU card uses mint, not gold. |

Light-theme `Light*` counterparts are re-derived to a mint accent for safety but the app runs Dark.

---

## 2. Typography — Space Grotesk + Space Mono

Font aliases (registered in `MauiProgram.cs`):
`SpaceGroteskLight`, `SpaceGroteskRegular`, `SpaceGroteskMedium`, `SpaceGroteskBold`, `SpaceMono`, `SpaceMonoBold`.

**Rule of thumb:** Space Grotesk = headings, body, big numerals. Space Mono = small UPPERCASE
letter-spaced labels, badges/pills, index numbers (01/02/03), the wordmark.

| Style key | Font | Size | Tracking | Color | Used for |
|---|---|---|---|---|---|
| `Display32` | SpaceGroteskBold | **40** | -0.5 | TextPrimary | Hero headline ("Your logbook fills itself.", "Unlock unlimited import.", "You're Pro."). Line-height tight (~1.05). |
| `Title24` | SpaceGroteskBold | **30** | -0.3 | TextPrimary | Screen titles ("The loop", "Logbook", "Create your logbook", "Dive #38"). |
| `Heading18` | SpaceGroteskBold | **19** | 0 | TextPrimary | Card titles ("Dive", "Sync", "Enrich", "Liberty Wreck", "Details"). |
| `Body15` | SpaceGroteskRegular | **16** | 0 | TextPrimary | Body copy. Use TextSecondary explicitly for muted paragraphs. Line-height ~1.4. |
| `Caption13` | SpaceGroteskRegular | **13** | 0 | TextSecondary | Sub-labels, field captions. |
| `MonoLabel` (NEW) | SpaceMono | **11.5** | **2** (CharacterSpacing) | TextSecondary | UPPERCASE eyebrows/labels: "MAX DEPTH", "HOW IT WORKS", "MARINE LIFE", "FOUND NEARBY", "DEPTH / TIME", "WRECK PHOTO", "NO SUBSCRIPTIONS". Set `TextTransform`/pass text already uppercased. |
| `MonoLabelDim` (NEW) | SpaceMono | 11.5 | 2 | TextDisabled | Faint section eyebrows ("02 · HOW IT WORKS" frame label). |
| `MonoIndex` (NEW) | SpaceMonoBold | **17** | 0 | Sonar | Card index numerals "01 / 02 / 03". |
| `Wordmark` (NEW) | SpaceMonoBold | **20** | **6** | Sonar | "TRIMIX" wordmark (letter-spaced, uppercase). |
| `Badge` (NEW, Label) | SpaceMono | **10.5** | 1.5 | context | Pill badges text ("IMPORTED", "RECOMMENDED", "BEST VALUE"). |
| `StatNumeral` | SpaceGroteskBold + `tnum` | **26** | 0 | context | Stat-card / list numerals ("28.4", "47:12", "41.2"). |
| `StatNumeralHero` | SpaceGroteskBold + `tnum` | **34** | 0 | context | Larger hero numerals. |
| `StatNumeralDisplay` | SpaceGroteskBold + `tnum` | **44** | 0 | context | Biggest depth readout. |
| `UnitSuffix` | SpaceGroteskMedium | 55% of numeral | 0 | TextSecondary | "m", "°C", "min" suffix after numerals. |

`StyleClass="tnum"` on numeral Labels keeps the tabular-figures handler (Android FontFeatureSettings).

---

## 3. Components

- **Primary CTA (`PrimaryButton`, implicit `Button`)** — mint (`Sonar`) fill, `Abyss` text, **SpaceGroteskBold ~17**, **pill** (`CornerRadius=30`), min-height **58**, full-width with 24px side margins, generous vertical presence. Pressed → `SonarDeep`.
- **Auth buttons (Account screen):**
  - `AuthButtonLight` (NEW) — **white** (`#FFFFFF`) fill, black text, rounded-rect (`CornerRadius=16`), height 56. "Continue with Apple" ( leading logo).
  - `AuthButtonOutline` (NEW) — transparent fill, 1px `GlassStroke` border, `TextPrimary` text, `CornerRadius=16`, height 56. "Continue with email".
- **`SecondaryButton`** — transparent, 1px `GlassStroke` border (NOT mint), `TextPrimary` text, pill or 16-radius per context. Used for "View as table", "Search", ghost actions. (Changed from mint-outline to neutral to match mockup ghost buttons.)
- **Glass card (`GlassCard`)** — `Surface` fill, 1px `GlassStroke`, **`RoundRectangle 18`**, padding 18/16. Very subtle — nearly flat, hairline edge only.
- **Selected/mint card** (Import-from-computer, selected SKU) — mint tint or 1.5px `Sonar` border on `Surface`. For the mint filled CTA-card ("Import from computer"), use `Sonar` fill + `Abyss` text.
- **Chips (`Chip`)** — `SurfaceRaised` fill, 1px `GlassStroke`, `RoundRectangle 14`, padding 12/7. Text `ChipLabel` (SpaceGroteskMedium 13). `ChipWarn` = `Warn` border + `ChipLabelWarn` amber text + "⚠ " glyph.
- **Badge pill (NEW `Badge`/`BadgeMint`/`BadgeWarn` Border styles)** — small rounded pill, `RoundRectangle 6`, padding 7/3:
  - `BadgeMint` — mint border + mint text ("IMPORTED", "ENTITLEMENT · PRO · ACTIVE", "MATCHED FROM ATLAS").
  - `BadgeMintFill` — mint fill + Abyss text ("RECOMMENDED", "BEST VALUE").
  - `BadgeWarn` — amber text, no fill ("FREE LIMIT REACHED · 5/5 DIVES").
- **Tab bar** — 3 tabs **Log · Sites · You**. Background `Depth` (seamless), active = `Sonar` text/tint, inactive = `TextSecondary`. Labels only (Space Grotesk). No raised center button.
- **Sonar rings motif** — concentric thin `Sonar` circles at low opacity (~0.5→0.15). Top-right on hero; centered behind the BLE/connect glyph and behind stat/empty states. Implement as `GraphicsView` (preferred, already have chart infra) or stacked `Ellipse` with `Stroke=Sonar, StrokeThickness=1.5, Fill=Transparent` at decreasing opacity.
- **Depth profile chart** — mint 2px line, subtle mint→transparent gradient fill under it, no axes chrome, faint baseline. (Core `ChartGeometry`/GraphicsView already exists — keep it, recolor to mint.)
- **Page dots (`IndicatorView`)** — active = elongated mint pill, inactive = small `GlassStroke` dots (already styled; recolor to mint).

---

## 4. Per-screen layouts — the 11 prototype screens (re-reconciled 2026-07-27 against `~/Desktop/Trimix Prototype.html`). **Preserve ALL existing bindings/commands/x:DataType/converters/SemanticProperties and funnel-event calls — restyle & restructure the visual layer only.**

The storyboard-era screens this section previously specced (BLE Import/Connect, email/passkey account creation, "Match the site" contribution flow) are **gone with their features** — the app is serverless and API-sync-only. Component styles from §3 carry over unchanged; screen-obsolete components (`AuthButtonLight`/`AuthButtonOutline` for Apple/email sign-in) are retired with their screens.

### 1 · Welcome (`OnboardingPage`, hero pane)
Full-bleed `Abyss`. "TRIMIX" wordmark (`Wordmark`, mint, top-left) · sonar rings bleeding off the top-right. Hero `Display32` white, left-aligned: "Your logbook fills itself." Body `Body15`/TextSecondary on the serverless promise: dives sync in from the platforms you already use — **no cloud, no servers; your logbook and links never leave this device.** Page dots, primary pill "Get started".

### 2 · How it works (`OnboardingPage`, pane 2)
Title `Title24`. Three `GlassCard`s with `MonoIndex` mint "01/02/03":
- 01 **Dive** — your computer records the profile and syncs to its own app, as always.
- 02 **Link** — connect the platforms where your dives already live (Garmin, Suunto, ScubaPro, Mares, Oceanic+).
- 03 **Sync** — Trimix pulls them in, enriches from the atlas, and can push out to PADI / SSI.
Bottom: primary pill "Continue".

### 3 · Account — biometric (`AuthPage`)
No account creation — there are no accounts. Title + body explain local unlock: your logbook is protected by your device's biometrics (Face ID / fingerprint). Single primary pill triggers `IBiometricUnlock`; fine print `Caption13`/TextDisabled: everything stays on this device.

### 4 · Logbook (`LogbookPage`, empty + populated states)
Header `Title24` "Logbook" + avatar. **Empty state:** mint chart glyph, `Heading18` "No dives yet", body pointing at linking a provider; CTA-card (mint fill) "Link a dive platform" → Providers. **Populated:** summary strip (three stat groups — `StatNumeral` mint DIVES · white MAX m · white BOTTOM), dive rows (`GlassCard`: index, site name + `Caption13` place/date, right `StatNumeral` depth + duration, provider `ShortLabel` badge). Tab bar Log·Sites·You.

### 5 · Providers (`ConnectedAccountsPage`)
The seven provider rows from `ProviderCatalog` — **IN:** Garmin Dive, Suunto, ScubaPro, Mares, Oceanic+; **OUT:** PADI, SSI — each a `GlassCard` row: name `Heading18`, legal-posture badge ("Unofficial · personal export"), linked/unlinked state, Connect/Disconnect. Connecting a `ToSRiskReverseEng` provider first shows the plain-language ToS-risk disclosure (consent gate) before any I/O. "Sync now" entry point (`SyncCoordinator`) with per-provider progress.

### 6 · Provider sign-in (`ProviderOAuthPage`)
In-app OAuth-style sheet with browser chrome: provider monogram "logo", vendor domain (`ProviderCatalog.DomainFor`), credential fields. Password is used once for the token exchange and never persisted (guardrail; token → Keychain/Keystore). Truthful labelling — never "official <Vendor> integration".

### 7 · Dive detail (`DiveDetailPage`)
Header `Title24` "Dive #n" + `BadgeMint` provider badge; subtitle date · time · source. 2×2 stat grid (`MonoLabel` + `StatNumeral` + `UnitSuffix`): MAX DEPTH (mint) · BOTTOM TIME · MIN TEMP (mint) · GAS. "DEPTH / TIME" `GlassCard` with the mint profile chart — **touch-to-scrub**: crosshair + "depth · time" tooltip follows the finger (keep the view-as-table a11y alternative). Below: **"NEAR THIS SITE · <coords>"** carousel — 3 auto-rotating cards from the bundled atlas (`SiteAtlas`). Notes block (editable, local).

### 8 · Paywall — purchase moment (`PaywallPage`, state 1)
Top `BadgeWarn` "⚠ FREE LIMIT REACHED · 5/5 DIVES" (binds to the `FreeTier` constant — free keeps your last 5; the 6th synced dive fires this). Hero `Display32` "Unlock unlimited import." Two SKU cards: Pro `GlassCard` ("Unlimited import · full logbook", white "$14.99") · Lifetime (mint 1.5px border, `BadgeMintFill` "BEST VALUE", mint "$29.99"). Primary pill "Unlock — one-time"; footer `MonoLabel` "NO SUBSCRIPTIONS" · "Restore purchases" (mint link). Keep price-visible-before-store-sheet rule, single dismiss.

### 9 · Unlocked (`PaywallPage`, state 2 — post-purchase)
Large mint disc with `Abyss` checkmark, `Display32` "You're Pro.", body: all dives synced in, unlock is active — **restorable through the store** (no account; the store owns the receipt). `BadgeMint` "ENTITLEMENT · PRO · ACTIVE". Primary pill "Go to logbook".

### 10 · Sites (`SitesPage` / `SiteDetailPage`)
**Derived from the diver's own synced dives** — no community DB, no contribution, no moderation. List of sites the diver has actually dived (name, region, dive count, depth range); detail enriched from the bundled atlas where matched (blurb, `Chip` depth/entry chips, `ChipWarn` hazards). Read-only.

### 11 · You (`StatsPage`, with `SettingsPage` behind the gear)
Avatar initials + **inline-editable name** (local-only) + cert line. Career stat cards (`DiveStats`, each with its completeness caption). **Units toggle** (m/ft · °C/°F). **Nitrox MOD planner** — O₂% slider (21–40) with live MOD readout at ppO₂ 1.4; display math only (MOD = (1.4/FO₂ − 1) × 10), never deco computation. Gear → Settings (entitlement state, Restore purchases, about/licenses).

---

## 5. Hard constraints for implementers
- Do **not** touch ViewModels, commands, converters, DI, or funnel-event names. Visual layer only.
- Keep every `SemanticProperties.*`, `AutomationProperties.*`, and the "view as table" a11y alternative.
- Keep the "← Back" CloseCommand bar already added to modal pages (restyle to match: ghost mint "←").
- Every tappable ≥44pt. Numerals carry `StyleClass="tnum"`.
- All color via `{StaticResource <Key>}` / `AppThemeBinding` — no raw hex in pages.
- Match spacing/scale to the mockups; when unsure, favor generous whitespace + big type as shown.
