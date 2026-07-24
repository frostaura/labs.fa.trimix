# Trimix — Mockup 1:1 Spec (authoritative)

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

## 4. Per-screen layouts (match 1:1). **Preserve ALL existing bindings/commands/x:DataType/converters/SemanticProperties and funnel-event calls — restyle & restructure the visual layer only.**

### S1 · Onboarding — hero (`OnboardingPage`, pane 1)
Full-bleed `Abyss`. Top row: "TRIMIX" wordmark (`Wordmark`, mint, top-left) · sonar rings top-right (concentric mint circles bleeding off the top-right corner). Big gap. Hero headline `Display32` white, 2 lines, left-aligned: "Your logbook fills itself." Body `Body15`/TextSecondary: "Auto-import from every major dive computer over Bluetooth, on a dive-site atlas built by divers." Push to bottom: page dots (3, first active, mint pill). Primary pill "Get started". Below, centered ghost text button "I already have an account" (SpaceGroteskBold, TextSecondary).

### S2 · Onboarding — the loop (pane 2)
Title `Title24` "The loop". Three `GlassCard`s stacked (spacing ~14). Each card: `MonoIndex` mint number ("01"/"02"/"03") left, then title `Heading18` + body `Body15`/TextSecondary:
- 01 **Dive** — "Your computer records the profile, as always."
- 02 **Sync** — "Open Trimix near it — dives pull over Bluetooth."
- 03 **Enrich** — "Site, hazards and marine life auto-attach from the atlas."
Bottom: primary pill "Continue".

### S3 · Onboarding — account (pane 3) → maps to `AuthPage` content
Title `Title24` 2-line "Create your logbook". Body TextSecondary: "Your dives sync across devices and survive a reinstall — bound to your account, not your phone." Spacer. Buttons stacked: `AuthButtonLight` " Continue with Apple" (Apple glyph) · `AuthButtonOutline` "Continue with email" · centered ghost "Continue as guest" (SpaceGroteskBold). Fine print `Caption13`/TextDisabled centered: "Guest dives stay on this device until you create an account."
(Keep the real email/password entries + Register/Login/Guest commands — this hero/button treatment sits above them, or the entries appear after "Continue with email".)

### S4 · Logbook — empty (`LogbookPage`, empty state)
Header row: title `Title24` "Logbook" left, circular avatar (mint-ring circle) right. Centered empty block: mint line-chart glyph, `Heading18` "No dives yet", `Body15`/TextSecondary "Log your first one to start the streak." Bottom: mint filled CTA-card "Import from computer" with `BadgeMintFill` "RECOMMENDED" pill on its right; below it a ghost/outline "Log a dive manually". Tab bar Log·Sites·You (Log active).

### S5 · Import/Connect (`ImportPage`)
Back header "← Import" (mono/ghost). Centered: large sonar-ring cluster with a mint filled disc holding a device+Bluetooth glyph. Body `Body15` TextSecondary centered: "Trimix uses Bluetooth to read dives straight off your computer. Parsing happens on-device — nothing leaves your phone." `MonoLabel` "FOUND NEARBY". Device row card: `Heading18` "Shearwater Perdix AI" + `Caption13` "Paired · signal strong", right-aligned mint "Connect" action. (Keep scan/connect/import commands + progress + cap→paywall.)

### S6 · Profile / Dive detail (`DiveDetailPage`)
Header: `Title24` "Dive #38" with `BadgeMint` "IMPORTED" to its right; subtitle `Caption13` "14 Jun 2026 · 09:18 · Perdix AI". 2×2 stat grid of `GlassCard`s, each: `MonoLabel` label + big `StatNumeral` value + `UnitSuffix`:
- MAX DEPTH → value **mint** "28.4" + "m"
- BOTTOM TIME → value **white** "47:12"
- MIN TEMP → value **mint** "26" + "°C"
- GAS → value **white** "EAN32"
Then `GlassCard` "DEPTH / TIME" (`MonoLabel`) with the mint profile chart. Bottom primary pill "Match the site →". (Keep the profile GraphicsView + view-as-table a11y alternative + real bindings.)

### S7 · Site atlas / Site detail (`SiteDetailPage`)
Top: mint dot + `MonoLabel` "MATCHED FROM ATLAS · 98%". Hero photo card (`GlassCard`, `SurfaceRaised` placeholder with `MonoLabel` "WRECK PHOTO"): `Heading18` "Liberty Wreck" + `Caption13` "Tulamben, Bali · shore". Chip row: `Chip` "5–30m", `ChipWarn` "⚠ current", `Chip` "shore access". `MonoLabel` "MARINE LIFE" + row of 3 `SurfaceRaised` thumbnail squares. Quote `Body15` italic "“Bumphead parrotfish at dawn on the wreck's stern.”" + `Caption13` "— 24 divers". Bottom primary pill "Confirm site". (Keep site data bindings + contribute/open commands + back-nav CloseCommand.)

### S8 · Purchase moment (`PaywallPage`)
Top `BadgeWarn` "⚠ FREE LIMIT REACHED · 5/5 DIVES". Hero `Display32` "Unlock unlimited import." Body `Body15`/TextSecondary "3 more dives are waiting on your Perdix. Free keeps your last 5." Two SKU cards:
- Pro (`GlassCard`): `Heading18` "Pro" + `Caption13` "Unlimited import · full logbook" left, `StatNumeral` white "$14.99" right.
- Lifetime (`GlassCardGold`→ now mint 1.5px border): `BadgeMintFill` "BEST VALUE" top-left, `Heading18` "Lifetime" + `Caption13` "+ offline packs · all future perks", `StatNumeral` **mint** "$29.99".
Bottom primary pill "Unlock — one-time". Footer row `MonoLabel`: "NO SUBSCRIPTIONS" · "Restore purchases" (mint link). (Keep SKU bindings, purchase/restore commands, price-visible-before-store rule, single dismiss.)

### S9 · Unlocked / "You're Pro" (post-purchase confirmation)
Centered: large mint filled disc with `Abyss` checkmark. `Display32` "You're Pro." centered. Body `Body15`/TextSecondary centered "All dives imported and multi-vendor unlock is active — bound to your account, restorable forever." `BadgeMint` "ENTITLEMENT · PRO · ACTIVE" centered. Bottom primary pill "Go to logbook". (Wire to the real entitlement-granted state after purchase.)

### S10 · Logbook — populated (`LogbookPage`, list state)
Title `Title24` "Logbook". Summary strip: three stat groups — `StatNumeral` **mint** "38" + `MonoLabel` "DIVES"; white "41.2" + "MAX m"; white "31h" + "BOTTOM". Dive list rows (`GlassCard`/row): left `MonoIndex`-ish number ("38"), `Heading18` site name + `Caption13` "Tulamben · 14 Jun"; right `StatNumeral` depth "28.4m" + `Caption13` duration "47min". Tab bar Log·Sites·You.

---

## 5. Hard constraints for implementers
- Do **not** touch ViewModels, commands, converters, DI, funnel-event names, or backend. Visual layer only.
- Keep every `SemanticProperties.*`, `AutomationProperties.*`, and the "view as table" a11y alternative.
- Keep the "← Back" CloseCommand bar already added to modal pages (restyle to match: ghost mint "←").
- Every tappable ≥44pt. Numerals carry `StyleClass="tnum"`.
- All color via `{StaticResource <Key>}` / `AppThemeBinding` — no raw hex in pages.
- Match spacing/scale to the mockups; when unsure, favor generous whitespace + big type as shown.
