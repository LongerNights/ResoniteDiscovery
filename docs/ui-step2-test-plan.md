# UI redesign step 2: top bar, header, rail, footer (in-game checks)

Built 2026-11-03, not yet run in game. See `docs/ui-redesign.md`.

1. **Top bar.** Sort, Filters and Views buttons end in a down arrow and show only the value ("Visits ▾",
   "Filters (2) ▾", "Views ▾"); all five buttons are one text size. If the arrow shows as a box, tell me and I
   will use another glyph.
2. **Header.** The subtitle is one short line next to the title (no wrapping to two lines on any lens); the world
   count sits at the right.
3. **Rail rows.** Labels are left-aligned with the count at the right edge. The selected row has a thin purple bar on
   its left and a raised fill; the other rows have no fill. Check Live Now (counts), a named list (long names), and
   with Insights switched on.
4. **Footer.** The page buttons read "‹ Previous" and "Next ›"; Classic browser is unchanged.
5. Everything still works: switching lenses, lists, paging, chips, sort and filters.
