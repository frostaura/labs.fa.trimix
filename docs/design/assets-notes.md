# Sonar design assets — authoring notes

What was authored under `src/app/Trimix.App/Resources/` to implement [`sonar-design-language.md`](sonar-design-language.md), plus every derivation/deviation. Authored 2026-07-03; XAML/SVG hand-verified well-formed with `xmllint` (no `dotnet build` run from this workstream — siblings were building concurrently).

## Files

| File | Content |
|---|---|
| `Resources/Styles/Colors.xaml` | All 15 canonical dark tokens + 15 `Light*` counterparts, convenience `SolidColorBrush`es, and the `DepthGradientHeaderBrush` / `LightDepthGradientHeaderBrush` (signature motif 4: Depth→Abyss vertical fade). |
| `Resources/Styles/Styles.xaml` | Full component style system — typography scale, buttons, glass cards, chips, list rows, implicit control theming, Shell/nav chrome. Every color is `AppThemeBinding`-wrapped (dark primary, light secondary). |
| `Resources/AppIcon/appicon.svg` | Background layer: Abyss `#06121F` rounded square (rx 100 / 456), no wordmark. |
| `Resources/AppIcon/appiconfg.svg` | Foreground layer: sonar-return blip + three concentric Sonar ping arcs radiating up-right from an off-center lower-left origin. |
| `Resources/Splash/splash.svg` | Abyss background + single sonar arc motif (blip + one arc). |

`Trimix.App.csproj` already wires these (`MauiIcon` with `ForegroundFile` + `Color="#06121F"`, `MauiSplashScreen Color="#06121F" BaseSize="128,128"`) — no csproj change was needed or made.

## Token → resource mapping

Dark tokens carry the **canonical name from the design doc, verbatim**; each has a `Light*` counterpart. Consume as:

```xml
{AppThemeBinding Light={StaticResource LightSurface}, Dark={StaticResource Surface}}
```

| Token (dark, canonical) | Hex | Light counterpart | Hex | Light derivation |
|---|---|---|---|---|
| `Abyss` | `#06121F` | `LightAbyss` | `#F3F8FC` | Doc-given ("Frost" background) |
| `Depth` | `#0B1E31` | `LightDepth` | `#E7F0F7` | Derived (section/header tint one step below Frost) |
| `Surface` | `#12293F` | `LightSurface` | `#FFFFFF` | Doc-given (white surfaces) |
| `SurfaceRaised` | `#1A3450` | `LightSurfaceRaised` | `#E9F1F8` | Derived — see deviation 2 |
| `GlassStroke` | `#2E4D6B` | `LightGlassStroke` | `#C9DBE9` | Derived (glass edge on white) |
| `Sonar` | `#4FD8EB` | `LightSonar` | `#2AA8C4` | Doc-given ("SonarDeep as accent" on light) |
| `SonarDeep` | `#2AA8C4` | `LightSonarDeep` | `#1E7A91` | Derived — see deviation 3 |
| `Ice` | `#BFE7F2` | `LightIce` | `#0F5E74` | Derived — see deviation 4 |
| `TextPrimary` | `#EAF4FB` | `LightTextPrimary` | `#0B1E31` | Doc-given (light-theme text) |
| `TextSecondary` | `#9FB8CC` | `LightTextSecondary` | `#4A6178` | Derived (6.4:1 on white) |
| `TextDisabled` | `#5F7A91` | `LightTextDisabled` | `#8DA4B8` | Derived (intentionally sub-AA; disabled text is WCAG-exempt) |
| `Signal` | `#3DDC97` | `LightSignal` | `#0F7B52` | Derived (5.3:1 on white) |
| `Warn` | `#F5B84B` | `LightWarn` | `#8F6400` | Derived (5.3:1 on white) |
| `Alert` | `#F4766B` | `LightAlert` | `#C43E32` | Derived (5.1:1 on white) |
| `PaywallGold` | `#F2C879` | `LightPaywallGold` | `#8A651A` | Derived (5.3:1 on white) |

Brushes: `AbyssBrush`, `DepthBrush`, `SurfaceBrush`, `SurfaceRaisedBrush`, `GlassStrokeBrush`, `SonarBrush`, `IceBrush`, `PaywallGoldBrush` + `Light*` counterparts, for `Brush`-typed properties.

All AA-relevant pairings were verified numerically (WCAG 2.1 relative-luminance formula): TextPrimary/Surface 13.3:1, TextSecondary/Surface 7.2:1, Abyss-on-Sonar 11.1:1 (dark) / 6.7:1 (light), LightSonarDeep-on-white 4.9:1, all light semantic colors ≥5.1:1 on white, chip text ≥6.4:1 on chip fills. Only `TextDisabled` pairs are below 4.5:1, by design.

## Style catalogue (`Styles.xaml`)

**Typography (keyed `Label` styles):** `Display32`, `Title24`, `Heading18`, `Body15`, `Caption13` (Caption defaults to TextSecondary). Instrument numerals: `StatNumeral` (28pt — cards/stats strip), `StatNumeralHero` (40pt) and `StatNumeralDisplay` (48pt — dive detail), all `CharacterSpacing=0`, `NoWrap`. `UnitSuffix` (TextSecondary, default 15pt = 55% of the 28pt numeral; override FontSize to 22/26 beside Hero/Display).

**Buttons:** `PrimaryButton` (Sonar fill, Abyss text, CornerRadius 14, 52pt tall; Pressed→SonarDeep, Disabled→Surface+TextDisabled), `SecondaryButton` (transparent, 1px Sonar stroke, Sonar text), `DestructiveButton` (Alert outline + Alert text). The **implicit** `Button` style is `BasedOn PrimaryButton` (CTAs are the default).

**Glass cards (keyed `Border` styles):** `GlassCard` (Surface fill, 1px GlassStroke, `RoundRectangle 16`, `Padding 20,16` — the doc's "generous 16/20px"), `GlassCardRaised` (SurfaceRaised), `GlassCardGold` (PaywallGold stroke — Lifetime SKU card only).

**Chips:** `Chip` / `ChipSelected` / `ChipWarn` (`Border`, pill via `RoundRectangle 18`, SurfaceRaised fill, min height 36) with matched label styles `ChipLabel` / `ChipLabelSelected` (Ice text) / `ChipLabelWarn` (hazards).

**List rows:** `x:Double` metrics `ListRowHeightCompact=56` / `ListRowHeightDefault=64` / `ListRowHeightTall=72`, plus `ListRow` / `ListRowTall` `Grid` styles (min heights 56/72, `Padding 20,8`).

**Implicit theming:** Page (Abyss bg), Label (Body 15 / TextPrimary, Disabled→TextDisabled), Border (glass stroke), BoxView (1px GlassStroke divider), Entry/Editor/SearchBar/SearchHandler/Picker/DatePicker/TimePicker (TextPrimary, TextSecondary placeholders, transparent bg, ≥44pt), ActivityIndicator/ProgressBar/CheckBox/Switch/Slider/RefreshView/IndicatorView (Sonar accents), Shadow (8% black max — per motif rule 3), Shell/NavigationPage/TabbedPage (Depth chrome, Sonar active tab, TextSecondary unselected). Keyed `CapMeterWarn` ProgressBar for the free-tier cap meter near the cap.

## Icon / splash geometry

- **Foreground safe area:** the Android adaptive-icon safe zone is a centered circle of 66/108dp = radius ~139px on the 456px canvas. All foreground content (blip at 168,288 r14; arcs r48/88/128 sweeping −15°→105° up-right) stays within ~119px of canvas center, inside the safe circle with margin.
- **Resizetizer safety:** all three SVGs use only `rect`/`circle`/`path`, no filters, no gradients, no text elements, no opacity attributes. Arc dimming is **pre-blended** into solid strokes over Abyss: `#399DAE` (Sonar @70%) and `#276B7B` (Sonar @45%).
- **Splash:** blip + one arc (single-arc motif per the task/design doc); full-canvas Abyss rect blends seamlessly into the `MauiSplashScreen Color="#06121F"` backdrop.

## Deviations / judgment calls vs the design doc

1. **No monochrome icon variant file.** The doc asks for one; MAUI's `MauiIcon` has no monochrome slot (Android 13 themed icons need a platform-specific `@drawable` monochrome layer). `appiconfg.svg` is already single-hue and serves as the source for that layer when Android-13 themed-icon support is wired (post-slice; platform folder is outside this workstream's ownership).
2. **`LightSurfaceRaised` is a tint (`#E9F1F8`), not white.** The doc only says "#FFFFFF surfaces" for light; with white cards on a Frost background, raised elements (chips, dialogs) need a visible fill of their own. Elevation on light is carried by the tint + border instead of dark-theme-style lightening.
3. **`LightSonarDeep` darkened to `#1E7A91`.** The doc maps SonarDeep to "links on light", but `#2AA8C4` is only 2.8:1 on white — it fails the doc's own AA mandate. `LightSonar` keeps `#2AA8C4` for fills (buttons/active states, where the Abyss label carries contrast at 6.7:1); `LightSonarDeep` is the AA-safe (4.9:1) pressed/link color.
4. **`LightIce` inverts to a deep ice-teal (`#0F5E74`).** Ice is selected-chip *text* on dark; a pale `#BFE7F2` counterpart would be unreadable on light chips. The light counterpart preserves the *role* (selected-chip text, 6.4:1 on the chip tint), not the lightness.
5. **Tabular figures (`tnum`) are a hint, not a XAML property.** MAUI has no cross-platform `FontFeatures`; `StatNumeral*` styles set `CharacterSpacing=0`/`NoWrap`, and true `tnum` must be applied by a handler mapping (Android `Paint.SetFontFeatureSettings("tnum")`, iOS `UIFontDescriptor` number-spacing feature) targeting labels that carry these styles. Noted in the XAML comment; wiring belongs to the app-code workstream.
6. **No `FontFamily` setters anywhere** — deliberate, per the doc ("system stack, no custom font in the slice"). The template's OpenSans fonts remain registered in `MauiProgram.cs` but nothing references them; removing them is app-code cleanup, not a Resources concern.
7. **Icon arc opacities exceed the 12–4% motif spec.** The doc's 12–4% opacity applies to in-app ping-ring *motifs* (scanning/empty states); at app-icon size that would be invisible. The icon uses 100/70/45% equivalents (pre-blended), consistent with the doc's separate icon spec ("three concentric Sonar ping arcs"), which sets no opacity.
8. **Button font size 16 bold** — the doc doesn't specify button typography; 16pt bold on a 52pt button splits Body 15 / Heading 18 and keeps the Abyss-on-Sonar label comfortably above large-text AA.
9. **Pressed states** use SonarDeep per the token table ("pressed/secondary accent"); disabled states use Surface fill + TextDisabled — both are interpolations the doc implies but doesn't spell out.
