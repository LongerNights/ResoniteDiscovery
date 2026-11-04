# Animations and the animation speed setting (in-game checks)

Built 2026-11-04, not yet run in game. Settings > General has **Animations** (on/off) and **Animation speed**
(Slow / Normal / Fast = 0.6x / 1x / 1.6x). Every duration goes through `UiMotion` (`src/Discover.Resonite/Ui/UiMotion.cs`).

1. **Speed.** Change the speed and open the detail panel again: Slow is clearly slower, Fast quicker. Animations off:
   everything appears instantly (and nothing is left half-moved).
2. **Modals** (detail panel, Settings, Organize, name prompts, Session Settings, confirm panels): the panel rises into
   place with an ease-out and the dimming behind it fades in.
3. **Dropdowns** (Sort, Filters, Views, Start on): they drop into place with a small rebound.
4. **Switching tabs: one sweep over the results.** A dark sheet with a cyan leading edge rises over the whole results
   area (the old cards are covered from the bottom up), the new tab is filled in underneath, then the sheet retracts
   downward, revealing the new cards from the top. Only the results area is covered (not the rail, top bar or chips). Check
   Browse (search tabs, which wait for the network: the sheet should reveal an empty or "Loading" area rather than hang), Live Now,
   Favorites and list mode. Speed and Animations off apply (off: tabs swap at once).
4b. **Card to info panel, then a scan.** Pressing a card (grid), a row (list) or an Info button makes the info panel's box
   grow out of that card into its place (dimming fades in); when it arrives, a sheet over it retracts from the top down,
   scanning its contents in. Plain opens (for example from the Lists button or More by author) still just rise into place.
5. **Switches** (Settings, Filters, Organize, Session Settings): the knob slides and the track colour eases; opening
   Settings does not make every knob slide.
6. **Rail:** the white bar on a newly selected row fades in.
7. **Detail panel sessions:** choosing another session fades its information in.

Not animated: closing panels (still instant).
