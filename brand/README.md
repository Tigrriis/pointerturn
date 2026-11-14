# PointerTurn brand files

The logo, icons and colours for PointerTurn's website and advertising ("Sheet": a light page,
an amber accent, a lower-case wordmark).

## Files

| File | What it is | Use it on |
|---|---|---|
| `lockup.svg` | Icon tile and wordmark, ink | Light backgrounds. The main logo |
| `lockup-reversed.svg` | Icon tile and wordmark, white | Ink and other dark backgrounds |
| `icon.svg` | Ink logomark on an amber rounded tile | App icon, avatar, anywhere the logo must be square |
| `icon-ink.svg` | Amber logomark on an ink rounded tile | The same, where amber would clash |
| `logomark-ink.svg`, `logomark-amber.svg`, `logomark-white.svg` | The logomark alone | Large decorative use; on amber use ink, on ink use amber or white |
| `wordmark-ink.svg`, `wordmark-white.svg` | The wordmark alone | Beside an icon that is already shown |
| `favicon-32.png`, `apple-touch-icon.png`, `icon-512.png` | The icon as PNG | Browser tab, iOS home screen, social and store listings |
| `tokens.css` | Colours, fonts and corner radii as CSS variables | The website's stylesheet |

The wordmark is outlines, not text, so the SVGs need no font.

## Colour

| Name | Value | Use |
|---|---|---|
| Amber | `#FFC21A` | The accent: icon tile, highlighted words, buttons on ink |
| Ink | `#111A22` | Logomark, headings, primary buttons, dark bands |
| Paper | `#F5F6F4` | Page background |
| White | `#FFFFFF` | Cards and panels |
| Highlight | `#FFEFB0` | Pale amber fills |
| Slate | `#36414B` | Body text |
| Graphite | `#55606B` | Secondary text and small labels |
| Line | `#D5D9DC` | Borders and dividers |

Text on amber is always ink, never white.

## Type

- **Outfit SemiBold** for headlines and the wordmark, tracked at -0.03em.
- **Inter** for body copy and UI.

Both are on Google Fonts under the SIL Open Font License.

## Using the logo

- Don't redraw, stretch, rotate or recolour the logomark beyond the versions here.
- Keep clear space around the lockup of at least half the tile's height.
- Don't show the lockup narrower than 120 px, or the icon smaller than 16 px.
- The tile's corner radius is 23.5% of its side, and the logomark is 64% of the tile's height,
  centred.

## Where the logomark came from

The logomark here was traced from a 220 px PNG of the original. If the original vector artwork
turns up, replace the path in these files with it.
