# Animations and the animation speed setting (in-game checks)

Built 2026-11-04, not yet run in game. Settings > General has **Animations** (on/off) and **Animation speed**
(Slow / Normal / Fast = 0.6x / 1x / 1.6x). Every duration goes through `UiMotion` (`src/Discover.Resonite/Ui/UiMotion.cs`).

1. **Speed.** Change the speed and open the detail panel again: Slow is clearly slower, Fast quicker. Animations off:
   everything appears instantly (and nothing is left half-moved).
2. **Modals** (detail panel, Settings, Organize, name prompts, Session Settings, confirm panels): the panel rises into
   place with an ease-out and the dimming behind it fades in.
3. **Dropdowns** (Sort, Filters, Views, Start on): they drop into place with a small rebound.
4. **Switching tabs: the scan reveal.** Each card that gets a new world is covered by a dark sheet with a cyan leading
   edge that sweeps down it, revealing the card from the top; cards fill a few per frame, so a tab appears as a
   cascade of scans. The tab title fades in. Check the game does not stutter more than before (each card starts one
   short animation); if it does, tell me and I will simplify it. Works the same in list mode (each row is scanned).
4b. **Card to info panel.** Pressing a card (grid) or a row (list), or its Info button, makes the info panel grow out of
   that card's rectangle into its place (the contents appear when it arrives) while the dimming fades in. Check cards in
   different places on the page, and that opening a panel from elsewhere (for example after "More by author") still just
   rises into place. With Animations off it opens instantly.
5. **Switches** (Settings, Filters, Organize, Session Settings): the knob slides and the track colour eases; opening
   Settings does not make every knob slide.
6. **Rail:** the white bar on a newly selected row fades in.
7. **Detail panel sessions:** choosing another session fades its information in.

Not animated: closing panels (still instant).
