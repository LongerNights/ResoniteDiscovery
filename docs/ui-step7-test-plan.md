# UI redesign: animations, Start on dropdown, list mode, Settings sidebar, rebrand (in-game checks)

Built 2026-11-04, not yet run in game.

1. **Rebrand.** The mod loads as "ResoniteDiscovery" (Mods list, log). On first start your data is copied from
   `%APPDATA%\GalaxySieve\<account>` to `%APPDATA%\ResoniteDiscovery\<account>`: favorites, lists, tags, history, saved
   views and settings are all still there. The old folder is left alone. Export writes `resonite-discovery-export.json`
   (an older `galaxy-sieve-export.json` is still read by Import). Settings > Data's note uses the new names.
2. **Animations (on by default).** Dropdowns (Sort, Filters, Views) slide down into place; the detail, Settings,
   Session Settings and confirm panels rise into place. The black fade between lenses is gone for good. Settings >
   General > Animations off makes everything appear instantly. Panels still close instantly.
3. **Start on.** In Settings > General the row shows the current lens and "▾"; pressing it opens a two-column
   dropdown of the lenses in the sidebar, the current one checked. Picking one changes the row and closes the
   dropdown; pressing the row again closes it. The dropdown sits under the row.
4. **List mode** (Settings > Display > List): hovering a row's picture pans it; pressing the picture opens the
   detail panel; tags, Info and Lists are dark inset buttons you can see against the row; row text is larger
   (title, host line, visits, tags, buttons). Switch back to Grid: the cards look as before.
5. **Sidebar settings.** Each section's row is a tinted bar with a bold name and its own switch, a gap between
   sections, and the lenses indented under it (dimmed when the section is off).

Not changed: DLL and assembly names (`Discover.Resonite.dll`) and the GitHub repo name.

## Follow-up build (2026-11-04)

6. **Text alignment.** Words with and without descenders sit at the same height in their boxes: tags, tag chips,
   top-bar buttons ("Refresh" vs "Settings"), rail rows. Long chips ("Educational") keep the same font size as
   the rest (the chip is made wide enough for its text); list-row tags no longer wrap ("new users").
7. **Detail panel sessions.** A world with live sessions lists up to three, each with host, people and age and its
   own Join button (Full and greyed when it cannot be joined). One with none says "Not live right now".
8. **Settings > Hidden.** Below the counts and the two buttons, each hidden author, world and word is listed by name
   with a button to bring back just that one (up to six shown, then "and N more"). Hide a world from its detail
   panel and check its name appears.

## Text alignment pass 2 and the Organize panel (2026-11-04)

9. **Text height.** The lift is now 0.25 of the font size (measured: caps were about 1 px low, lower-case about 2 px).
   Compare "Refresh", "Settings", "Visits", the tag chips, the rail rows and the Previous / Next buttons (now a fixed
   size, previously default-sized and low). Report which way and how far any text is still off.
10. **Organize panel** (Lists on a card or in the detail panel): centred, as tall as its content; each list is a
    switch row (membership), the world's own tags are removable chips, then the tag box with suggestions and Done.

11. **Organize and name prompts** (Lists on a card; New list, Rename, Hide a word): text is larger (headings,
    empty-state lines, tag chips, suggestions, Add); the tag box text is left-aligned and readable; tag and suggestion
    chips fit their text; the name prompt is sized to its content and slides in. The search box text is a size up.

12. **Chip bar** shows the chips you have set first (required, then excluded), then the recommended tags, and only as
    many as fit the width, so the row is never cut off (the sideways-scrolling strip was dropped). Check on Browse
    with a few chips set, and after resizing nothing is clipped at the right edge; Clear all sits after the last chip.
13. **Pager.** The footer shows the status text and two square arrow buttons (‹ and ›) at the right; the right arrow is
    the cyan one. Both still page; ‹ is greyed on page 1.

14. **Clear all** is a red button with a cross, pinned to the right end of the chip bar (not a chip in the row);
    it shows only while a chip is set and clears them all.
15. **`author:` search.** On Browse type `author:` and an exact Resonite user name (for example your own): after a moment
    the results are that user's worlds (the server was asked, not just the loaded page). A name nobody has just
    filters what is loaded. Check the log shows no repeated lookups for the same name.
