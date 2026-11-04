# UI redesign step 1: design system (in-game checks)

Built 2026-11-03, not yet run in game. See `docs/ui-redesign.md`.

1. **Type scale.** The top bar's Sort, Filters, Views, Refresh and Settings labels are the same size. Rail headings,
   popover text, the detail panel and Settings use the same few sizes; no label is tiny next to a large one. Long
   labels wrap or clip rather than shrinking (report any that look broken).
2. **Pane.** The header area behind "Browse" shows no world text bleeding through.
3. **Sort, Filters and Views dropdowns** are only as tall as their rows, start right under the top bar and sit
   under their own button. Open each at least twice; check the panel is not clipped at the top and Done / Reset still work.
4. **Confirm panel** (Join session, Start a new session, group picker, Hide...): centred on screen and as tall as its buttons.
5. **Session Settings** (New session on a world with no live session): centred, as tall as its content, all rows and
   Start Session / Cancel visible.
6. Cards, the rail and the detail, Settings and Organize panels look as before (they change in later steps).

If a panel is the wrong size or position, `BeginFloating` in `DiscoverUi.cs` is the one place that controls it.
