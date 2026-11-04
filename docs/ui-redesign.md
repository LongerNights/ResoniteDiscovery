# UI redesign plan

*Written 2026-11-03 from in-game screenshots of Browse, the Filters popover, the detail panel and Session Settings.
Nothing here is built yet. Spec rules still hold: colours by `RadiantUI_Constants` name, strings in the string
table, numbers in settings, no world writes.*

## What is wrong today

These problems repeat across every screen, so fixing them once fixes most of the UI.

1. **Text sizes are random.** Almost every label uses `bestFit`, so the size follows the label's length. "Presets" and
   "Settings" are tiny next to "Filters" and "Refresh". "New session" and "More by author" shrink to two small lines
   beside a large "Lists". "Educational" is half the size of the other chips.
2. **Panels don't fit their content.** Filters, Presets, Session Settings and the detail panel all stretch to the bottom
   of the pane, which leaves half of each panel empty.
3. **Greyed-out controls are clutter.** The sort menu lists four "Not for this lens" rows, and Filters shows five dim
   toggles that do nothing on the current lens. They are hard to read and add nothing.
4. **The world shows through.** The main pane is translucent, so world text ("The Navy Seal", "Elysium") bleeds into
   the header behind "Browse".
5. **No visual hierarchy.** Primary, secondary and quiet buttons look nearly the same, and every row is a full-width
   purple bar. Mobile friendly in Session Settings looks like a seventh access level.
6. **The detail panel has no picture.** The card shows the world's 360° preview, but opening it loses the image.

## Design system (build first)

A small set of shared pieces in `DiscoverUi`'s `Theme`, used by every screen.

| Token | Values |
| --- | --- |
| Type scale (fixed, no `bestFit`) | Caption 13, Body 16, Label 18, Title 24, Hero 32. Long text ellipsizes instead of shrinking. |
| Spacing | 4, 8, 12, 16, 24 |
| Corner radius | Panels 16, buttons and chips 10, pills fully rounded |
| Surfaces | Pane (opaque, darkest), Panel (one step lighter), Raised (rows, fields), Scrim (black at 55 %) |
| Accent | The current purple for the selected item and the one primary action per panel; everything else neutral |

Shared controls:

- **Primary button:** filled accent. One per panel at most.
- **Secondary button:** Raised surface, Body text.
- **Quiet button:** text only, with a hover underline. Used for Cancel, Reset and Classic browser.
- **Icon button:** a square 44 px button with a glyph and a tooltip line (⟳, ⚙, ✕, ⋯, ☆).
- **Chip:** fixed height of 36, Body text, sized to the label. Filled for categories and outlined for popular and personal tags.
- **Segmented control:** two to seven options in one rounded bar. Used for sort direction, the host filter, cards per row and the access level.
- **Switch row:** label on the left, small on/off switch on the right. This replaces the "Has room · off" text buttons.
- **Stepper row:** label on the left, then `[−] value [+]` on the right.
- **Section heading:** Caption, uppercase, secondary text colour, with 16 above and 8 below.

Every popover and modal gets a `ContentSizeFitter` (vertical, preferred size) and a maximum height. Past that height its
body scrolls, so a panel is never taller than its content.

## Main screen (Browse, every lens)

**Pane:** make it opaque, with a very subtle vertical gradient from Pane to Panel colour. This removes the world bleed-through.

**Top bar** (one row, 52 px, all controls the same height and text size):

```
[ 🔍 Search worlds or tags…            ]  [Visits ▾] [Filters ②] [★ Views ▾]   [⟳] [⚙]
```

- **Search:** a field with a magnifier glyph, taking the remaining width.
- **Sort:** shows only the current sort's name plus ▾. "Sort:" is implied by the menu.
- **Filters:** shows the active count as a small badge, not "(2)" in the text.
- **Presets:** becomes "Views", with a star glyph.
- **Refresh and Settings:** become icon buttons. Refresh spins once while loading, which also replaces the "Updated just now" text.

**Header row:**

- Lens title (Title size) and the world count on the right ("40 worlds").
- One short subtitle line in Caption that ellipsizes, never wrapping. Shorten the texts: "Most visited first", "A shuffle of published worlds".
- When "More by author" is active, show a removable chip such as "by Resonite ✕" here.

**Chip bar:**

- Every chip has the same height and font size.
- The row scrolls sideways when it overflows instead of squeezing.
- Category chips are filled, and popular and personal tags are outlined, with a thin divider between the groups.
- An active chip shows ✓ when required and ⊘ when excluded.

**Rail:**

- Section headings use the Section heading style.
- Each item is a row with a small glyph, the label left-aligned, and its count as a pill on the right (only when it's not zero).
- The selected item gets a 4 px accent bar on the left and a Raised fill instead of a full purple block, which reads calmer.
- "+ New list" becomes a Quiet row.
- The rail scrolls if it overflows (Insights on plus many lists).

**Cards:** keep the current look. Add a hover state: a 2 px accent outline and the preview panning, which is already in place.
Users and friends pills share one style.

**Footer:**

- Left: a status line in Caption ("Page 1", plus a loading spinner when busy).
- Centre: "Classic browser" as a Quiet button.
- Right: a compact pager, `‹  1 / 3  ›`, replacing the oversized "Next" bar.

## Sort menu

- List only the sorts the lens supports. Unsupported ones are hidden, not greyed out.
- Put the direction at the top as a segmented control: `↓ High to low | ↑ Low to high`. Hide it for Random.
- Each row shows the label in Body and the hint ("Total visits") right-aligned in Caption. The selected row gets ✓ and the accent bar.
- The menu sizes to its rows.

## Filters popover

Show only the sections that apply to the current lens. Group them with headings:

| Section | Controls | Lenses |
| --- | --- | --- |
| People | Min people (stepper), Has room, Has VR users, Mobile friendly (switches) | Live Now, Friends |
| Session | Min uptime (stepper), Host: Anyone · A person · Headless (segmented), Include empty (switch) | Live Now, Friends |
| World | Min visits (stepper), Featured only, Show other versions (switches) | Search lenses |

Put Reset (Quiet) and Done (Primary) in a footer, with Reset disabled when nothing is set. The panel sizes to its
content: about 300 px tall on search lenses instead of the full pane.

## Views (presets) menu

- The top row is "+ Save current view".
- Each saved view is a row with its name in Body and the lens in Caption below it ("Featured · 2 filters"). Pressing the row loads it.
- A ✕ icon button sits at the right end of each row.
- The empty state is one Caption line, and the menu sizes to its content.

## Detail panel

A modal card about 70 % of the pane wide and sized to its content.

```
┌──────────────────────────────────────────────────────────────┐
│ ┌───────────────────────┐  Cloud                          ✕  │
│ │                       │  by Resonite ›     (author link)    │
│ │   360° preview        │  [169.7k visits] [3 y old] [Live: 0]│
│ │   (pans on hover)     │                                     │
│ │                       │  No description                     │
│ └───────────────────────┘  cloud  home  default  resonite     │
│                                                              │
│ SESSIONS                                                     │
│  Not live right now                                          │
│                                                              │
│ [ New session ]                        [☆] [Lists] [🔗] [⋯]  │
└──────────────────────────────────────────────────────────────┘
```

- **Hero:** the same `Projection360PropertyBlock` and `Perspective360Panner` the card uses.
- **Author:** the author's name is a link that does More by author, so the separate button goes.
- **Stats:** stat pills replace the "· · ·" byline.
- **Tags:** tags are chips, and pressing one adds it to the chip bar and closes the panel.
- **Sessions:** a row per session showing host, users/max and age, each with its own Join button. This replaces the separate join confirmation when there are two or more sessions.
- **Action bar:** the primary action (Join, Go there or New session) on the left. Favorite, Lists and Copy link are icon buttons on the right. ⋯ opens a small menu with Hide this world, Hide author and Leave/End session.

## Session Settings (new session)

```
┌──────────────────────────────────────┐
│ [thumbnail strip]  Start a session   │
│                    Cloud             │
│ WHO CAN JOIN                         │
│ ○ Default        engine's choice     │
│ ● Anyone         listed publicly     │
│ ○ Registered     signed-in users     │
│ ○ Contacts+      contacts and theirs │
│ ○ Contacts       your contacts only  │
│ ○ LAN            this network only   │
│ ○ Private        invite only         │
│ OPTIONS                              │
│ Mobile friendly                [  ○] │
│                  Cancel  [Start ▸]   │
└──────────────────────────────────────┘
```

- Access levels become a radio list. Each row has a one-line explanation in Caption, and the selected row has the accent bar.
- Mobile friendly moves under its own OPTIONS heading as a switch, so it no longer looks like an access level.
- The panel sizes to its content (about 520 px tall).
- Open it as a step inside the detail panel, sliding over the action area, instead of as a second modal. That keeps the world context visible.

## Settings panel

Split it into tabs along the top of one panel, so no column overflows as rows are added:

| Tab | Contents |
| --- | --- |
| General | Start lens, live lenses only, animations (reduce motion), Classic browser button |
| Display | View (grid/list, segmented), cards per row (segmented 2–7) |
| Sidebar | Section and lens switches (today's left column) |
| Notifications | The two notification switches |
| Hidden | Counts, list of hidden worlds and authors with ✕ each, add word, restore all |
| Data | Export, import, the note |

## Animations

All animation goes through one helper (`UiMotion`) that tweens a handful of fields with the engine's `TweenTo` /
`TweenFromTo`, the same calls the curtain already uses. The cost is a few field writes per frame, unlike the card fill, so
it won't bring back the lag. Every animation honours the Animations setting. When it's off, panels just appear and disappear.

| Element | Open | Close | Time / easing |
| --- | --- | --- | --- |
| Dropdowns (Sort, Filters, Views) | Unroll downward from the button: a `Mask` on the panel and its `AnchorMin.y` tweened from the top edge to its final value, plus a slight fade of the panel tint | Roll up, faster | 140 ms CubicOut / 90 ms SineIn |
| Modals (detail, Session Settings, Settings, confirm) | Scrim fades 0 → 55 %, and the panel rises 16 px from below while fading in | Panel drops 8 px and fades, then the scrim fades | 180 ms CubicOut / 120 ms SineIn |
| Session Settings inside the detail panel | Slides in from the right over the action area | Slides back out | 160 ms CubicOut |
| Lens switch | The header title cross-fades. Cards keep the current staggered fill, which already reads as a cascade. | — | 120 ms |
| Card hover | Outline fades in | Fades out | 80 ms |
| Toasts | Engine's own | — | — |

Rules:

- Panels take no input until the open animation ends, and close before the next one opens. A fast double-press never leaves two panels half-open.
- The "Animations" setting is renamed "Reduce motion" (inverted, default off). Today's black-out lens curtain is removed, since you rejected it.
- `VERIFY` before building:
  - (a) whether UIX honours slot `LocalScale` when rendering. If it doesn't, the rise uses `OffsetMin/OffsetMax` only, with no scale.
  - (b) whether `Text.Color` alpha needs tweening on each label, or whether a parent `Mask` plus the panel tint is enough to read as a fade.

## Order of work

Each step is shippable and testable in game on its own.

1. **Design system:** fixed type scale, button variants, size-to-content panels, opaque pane. Most of the "messy" look goes away here alone.
2. **Top bar, header, chip bar, rail and footer.**
3. **Sort menu and Filters popover:** hide what doesn't apply, add sections, switches and segmented controls.
4. **Detail panel:** hero image, author link, session rows, icon action bar.
5. **Session Settings:** radio list and Options switch, opened inside the detail panel.
6. **Settings tabs.**
7. **Animations,** after the two `VERIFY` probes.

Every step adds its strings to the string table and keeps colours by `RadiantUI_Constants` name. Each step gets a short
in-game checklist in a test plan, the same way M6–M10 did.
