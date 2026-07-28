# Trimix — "Sonar" Design Language (direction A)

> **Superseded historical document.** `mockup-spec.md` is the authoritative visual contract (different palette/fonts), and the screens/flows described below (BLE import, 4-tab bar with raised Import button, manual entry, site contribution, Settings-tab account) predate the 2026-07-27 serverless / API-sync-only pivot and no longer exist. Kept for provenance only.

> **Provenance.** The selected direction is **Sonar (A)** from the claude.ai design project (`Wreckline Storyboard.dc.html`, project `fb253415-935b-43f4-af5e-9f8f11d849a4`). The storyboard could not be imported at scaffold time (design MCP unauthorized, Chrome extension disconnected); this document re-derives the direction under founder-granted creative liberty, aligned with FrostAura brand posture (frosty/glass, light blue on dark blue, clean, minimalist, elegant). When the storyboard becomes importable via `/design-login` + DesignSync, reconcile this doc against it and record deltas here.

## Concept
A dive computer's sonar screen, elevated: **dark abyssal surfaces, ice-cyan signal accents, radial "ping" motifs, glassy depth-layered cards.** Data is the hero — depth numbers, profiles, and site intel render like instrument readouts. Calm, precise, credible to a technical diver; never gamified or toy-like.

## Color tokens (dark theme is primary; names are canonical for code)

| Token | Hex | Use |
|---|---|---|
| `Abyss` | `#06121F` | App background (deepest layer) |
| `Depth` | `#0B1E31` | Screen sections / scrolled headers |
| `Surface` | `#12293F` | Cards, sheets, inputs |
| `SurfaceRaised` | `#1A3450` | Elevated cards, dialogs |
| `GlassStroke` | `#2E4D6B` | 1px card borders, dividers (glass edge) |
| `Sonar` | `#4FD8EB` | Primary accent: CTAs, active states, profile line, ping rings |
| `SonarDeep` | `#2AA8C4` | Pressed/secondary accent, links on light |
| `Ice` | `#BFE7F2` | Accent-adjacent highlights, selected chips |
| `TextPrimary` | `#EAF4FB` | Primary text on dark |
| `TextSecondary` | `#9FB8CC` | Secondary text, captions (4.6:1 on Surface) |
| `TextDisabled` | `#5F7A91` | Disabled text/icons |
| `Signal` | `#3DDC97` | Success (import complete, contribution approved) |
| `Warn` | `#F5B84B` | Warnings (cap approaching, hazards) |
| `Alert` | `#F4766B` | Errors/destructive (never sole encoding) |
| `PaywallGold` | `#F2C879` | Lifetime SKU highlight only |

Light theme (secondary, auto-derived): `Frost #F3F8FC` background, `#FFFFFF` surfaces, `#0B1E31` text, `SonarDeep` as accent. Both themes must hold WCAG 2.1 AA (≥4.5:1 body text).

## Typography
- System stack (SF Pro / Roboto). No custom font in the slice.
- **Instrument numerals:** depth/time/temperature stats use large, light-weight numerals with tabular figures (`FontFeatures tnum` where supported), e.g. `32.4 m` — 40/48pt on dive detail, 28pt on cards.
- Scale: Display 32, Title 24, Heading 18, Body 15, Caption 13 (all respond to platform Dynamic Type).
- Unit suffixes render at 55% size, `TextSecondary`.

## Signature motifs
1. **Ping rings** — concentric expanding circles (Sonar at 12–4% opacity) used for: BLE scanning state, the logbook empty state, and the paywall hero. Animation ≤3 rings, 2.4s period, respects reduced-motion.
2. **Depth profile chart** — the dive's depth-over-time curve as a downward area chart: Sonar 2px line, vertical gradient fill Sonar→transparent, inverted Y (0 at top), grid lines `GlassStroke`, no chart junk. Always paired with a data-table alternative for accessibility.
3. **Glass cards** — `Surface` fill, 1px `GlassStroke` border, 16px radius, generous 16/20px padding. No shadows heavier than 8% black.
4. **Depth gradient headers** — screen headers fade `Depth`→`Abyss` downward, echoing descent.

## Component rules
- Tap targets ≥44pt; list rows 56–72pt.
- Primary button: Sonar fill, `Abyss` text, 14px radius, 52pt tall. Secondary: transparent, 1px Sonar stroke, Sonar text. Destructive: Alert outline.
- Chips (conditions, gas, hazards): pill, `SurfaceRaised` fill, Ice text when selected.
- Bottom tab bar, 4 tabs: **Logbook · Sites · Import · Settings** — Import is a raised circular Sonar action button (the loop's heartbeat).
- State encoding never by color alone (icons + labels accompany).

## Screen specs (slice)
- **Onboarding (3 panes):** full-bleed Abyss, ping-ring hero, one line per PRD beat (logbook fills itself / site DB gets smarter / your computer speaks Bluetooth), skip allowed. Final pane → account/guest choice.
- **Logbook (home):** running-stats strip (dives · max depth · bottom time) as instrument numerals, then dive cards (site name, date, depth/duration numerals, source badge `manual|import`). Free tier shows cap meter near the cap. Empty state: ping rings + "Log your first dive" dual CTA (Import / Manual).
- **Dive detail:** depth gradient header with hero numerals, profile chart (+ "view as table"), site block (tap → site detail), conditions/gas/gear chips, notes, rating.
- **Manual entry:** single scrolling form, numeric keypads, site search field with DB results + "create draft site", required: date/site/depth/duration.
- **Import flow:** permission rationale pane → scanning (ping rings) → device list (vendor badge) → progress (n dives fetched) → result list with auto-matched sites → cap hit inserts the **paywall**.
- **Paywall (purchase moment):** modal sheet over the blocked import; headline "Unlock your whole logbook"; the two SKU cards (Pro $14.99 / Lifetime $29.99 with PaywallGold accent + "everything in Pro, forever"); restore link; no dark patterns, one dismiss X, price always visible before store sheet.
- **Sites:** search field + list (name, region, depth range, distance); detail: depth range, type, hazards (Warn chips), marine life, access notes, community-notes block, "Suggest correction" (Pro/verified → moderation disclosure "public after review").
- **Settings:** account, entitlement state, Restore purchases, units (m/ft, °C/°F), privacy (export/delete), about/licenses.

## Iconography & app icon
Outline icons, 1.75px stroke, rounded caps (Lucide-style). **App icon:** Abyss rounded square, three concentric Sonar ping arcs off-center lower-left (a sonar return), no wordmark. Adaptive-icon safe area respected; monochrome variant provided.

## Motion
Subtle only: 200ms ease-out fades/slides; ping rings as specified; profile chart draws in 400ms once per view. Everything honors platform reduced-motion.

## Implementation deltas (Stage-5 slice)
Deviations from the direction above as actually built in the MAUI slice. Recorded here (not only in code comments) so a later design/storyboard reconciliation reads them as intentional, not shipped-as-specced.

- **Tab bar — Import "raised circular action button".** The design calls for a physically raised/overlapping circular Sonar action button at tab-bar center (the loop's heartbeat). MAUI `Shell` `TabBar` renders a flat, evenly-weighted tab strip and does not expose a raised center-action affordance without a **custom platform Shell handler** (Android `BottomNavigationView` override). The slice ships the flat tab with a filled circular Sonar disc **icon** (`tab_import.png`) standing in for the raised button; the raised/overlapping treatment is deferred to a post-slice custom handler. Android Shell tab tinting can flatten the disc icon to a single color — the custom handler is also what would restore the two-tone (Sonar disc + Abyss glyph) rendering. Tracked in `AppShell.xaml`.
- **App icon — monochrome (themed-icon) layer.** The icon spec promises an adaptive-icon monochrome variant (Android 13+ themed icons). On .NET 10, `MauiIcon`'s resizetizer **does** emit an adaptive `<monochrome>` layer — but it reuses the *colored* foreground (`@mipmap/appicon_foreground`) as the alpha mask, so themed icons already render (no non-themed fallback). The slice adds a purpose-built single-color mask, `Resources/AppIcon/appiconmono.svg` (shipped as `@drawable/appiconmono`), as the design-promised dedicated monochrome variant. Repointing the adaptive icon's `<monochrome>` at that dedicated mask is **deferred**: it needs a hand-authored `mipmap-anydpi-v26/appicon.xml`, which duplicate-conflicts with the resizetizer-generated resource and would require dropping `MauiIcon`'s density pipeline — a post-slice icon pass.
