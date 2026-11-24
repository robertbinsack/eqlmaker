# Style of the web application template

The values are in `public/style.css` (custom properties at the top, components below). This file
describes the rules behind them.

## The five ideas

1. **A gradient as window frame.** The whole window carries a very calm, almost vertical
   blue-grey gradient (`#E4EBF6` to `#DAE2EF` to `#D0D8E5` to `#C7CFD8`, 178 degrees). The
   sidebar has no background of its own; it lies directly on the gradient.
2. **The content is a white sheet.** The work area is a white surface with 16 px round corners,
   without border and without shadow, floating on the gradient: 38 px free strip at the top,
   16 px air on the right and at the bottom, directly next to the sidebar on the left.
3. **Tiles and hairlines instead of boxes.** Inside the sheet: cards with a 1 px hairline
   (`#EDEAF0`) and 14 px radius, no shadows. Fields are tinted (`#F8F5F9`), not outlined. Tables
   have fine row lines only.
4. **Ink instead of a brand colour.** The control colour is an almost black ink (`#24292E`).
   Primary buttons are ink pills with white text, secondary buttons light grey pills
   (`#F1EEF3`). No blue as control colour; the only exception are text links (`#2F6BF0`).
5. **State colours stay state colours.** Green `#1F7A4C`, red `#C8443C`, amber `#9A6410`, each
   with a light background, for states only.

## App shell

- Sidebar 230 px wide, inner width 210 px. Everything in it aligns to the same line.
- A button in the top strip hides the sidebar; the sheet then moves to 8 px from the left edge.
- Name of the application at the top of the sidebar: pill, 41 px high, white at 62 %.
- Navigation rows: 41 px high, radius 11, icon in an 18 px box, text 14 px / 500 in `#5C6470`.
  Hover: white at 42 %. Active: white at 68 %, text `#1B1F24` / 600, icon fully opaque.
- Language switch at the bottom of the sidebar: a segmented switch.
- Page head: a card inside the sheet, 64 px high, title 16 px / 700, context on the right.
- Page content: background `#F8F5F9`, padding 16 / 24, 16 px between cards.
- Status line at the bottom of the sheet, separated by a hairline.

## Components

| Component | Rules |
|---|---|
| Card | white, 1 px `#EDEAF0`, radius 14, head with 11 / 15 px padding, title 14 / 700, head separated by a hairline |
| Section title | 12.8 / 600, upper case, letter spacing .09em, `#98A0AC` |
| Notice | `#FBF1E0` with line `#F3DFB6`, radius 14 |
| Button with text | pill, height 32, radius fixed at 16 px (never 999px), padding 0 / 16, 13 / 600, width follows the content. Primary: ink / white, hover `#3A4148`. Secondary: `#F1EEF3` / ink. Dangerous: red text on `#FBEAE8` |
| Button with icon only | circle 32 x 32, same colours; variant "outline": white with a hairline |
| Input field, select | height 38, radius 10, background `#F8F5F9`, transparent border, 13 px; focus: white, border `#98A0AC`, ring `0 0 0 3px rgba(27,31,36,.08)` |
| Field label | in form grids on the left 13 / 700 ink; above a field 12 / 600 `#98A0AC` |
| Segmented switch | tinted surface, the chosen segment white with a hint of shadow |
| Tabs | height 45, 13 / 600 `#98A0AC`; active: ink, 2 px underline in ink; hairline below the bar. No card-style tabs |
| Chip | pill, 11 / 500, padding 4 / 9, background `#F8F5F9`; state chips green, red, amber on light ground |
| Table | head `#F8F5F9`, 9.5 / 600 upper case, `#98A0AC`; cells 12 px, row height about 28, line `#F4F2F6`; hover `#F8F5F9`; numbers right-aligned with tabular figures |
| Dialog | white, radius 16, hairline, shadow `0 24px 64px -28px rgba(27,31,36,.42)`; backdrop `rgba(27,31,36,.34)` with 3 px blur; head with title 14 / 700 and a round close button |
| Scroll bar | 10 px, thumb `#C9CFDA` (hover `#9AA3B2`), round |

## Type

- System fonts: `"Segoe UI", system-ui, -apple-system, "Helvetica Neue", Arial, sans-serif`;
  for code `ui-monospace, "Cascadia Mono", Consolas, monospace`.
- Base size 13 px, dense and calm. Headings with slightly negative letter spacing, small
  captions upper case with positive letter spacing.
- Numbers and amounts with tabular figures (`font-variant-numeric: tabular-nums`).

## Icons

Inline SVG from the Lucide icon set, one stroke style everywhere, 18 px in the navigation and
15 to 16 px in buttons. See `ICONS.md`.

## Keyboard

Every control can be reached with the keyboard and shows its focus (2 px ink outline; fields
show the focus ring). Labels are bound to their inputs. Dialogs keep the focus inside, close
with Escape and give the focus back to the element that opened them. Table rows can be focused;
Space selects, Enter opens.

## Do not

- No shadows on cards, no dark sidebar, no blue as button colour.
- No `border-radius: 999px`; pills have a fixed 16 px.
- Never stretch text buttons to the full width.
- Do not outline fields; they are tinted.
- Do not cut content off: main areas scroll, content in scrolling columns must not shrink.

## Left out and why

The style follows a private style package of the author. These parts of it were not taken over:

| Left out | Why |
|---|---|
| Geist and Geist Mono font files | OFL fonts; replaced by system fonts, so that no font file has to be shipped |
| Font Awesome icon font (loaded from a CDN in the reference page) | replaced by inline Lucide icons; the template loads nothing from other servers |
| Icons folder and logo | origin not documented; not needed |
| Screenshots | show another application; not needed |
| Original style sheets of that application | origin not documented, written for another framework; not needed |
| Machine-readable token file | not needed; the values are in `public/style.css` |
| Product and customer names, German texts of the reference page | the template is neutral; all texts are in the language resources |
| Components without use in the template (user button with avatar, area switcher with menu, greeting, KPI cards with progress bars, toggle switch, choice pills, login card, holiday rows) | not needed by the template; the rules above are enough to add them in the same style |
