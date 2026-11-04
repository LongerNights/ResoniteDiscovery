# Animations and the animation speed setting (in-game checks)

Built 2026-11-04, not yet run in game. Settings > General has **Animations** (on/off) and **Animation speed**
(Slow / Normal / Fast = 0.6x / 1x / 1.6x). Every duration goes through `UiMotion` (`src/Discover.Resonite/Ui/UiMotion.cs`).

1. **Speed.** Change the speed and open the detail panel again: Slow is clearly slower, Fast quicker. Animations off:
   everything appears instantly (and nothing is left half-moved).
2. **Modals** (detail panel, Settings, Organize, name prompts, Session Settings, confirm panels): the panel rises into
   place with an ease-out and the dimming behind it fades in.
3. **Dropdowns** (Sort, Filters, Views, Start on): they drop into place with a small rebound.
4. **Switching tabs and paging: a scan line.** A thin cyan line travels up the results area and the cards vanish at the
   line (the pane's own background shows, there is no sheet); the new tab is filled in while nothing shows, then the line
   travels back down, revealing the new cards from the top. Only the results area is affected. The same happens when you
   press Next or Previous (the scan holds until the new page has arrived, up to a few seconds). Check Browse, Live
   Now, Favorites and list mode. Speed and Animations off apply (off: tabs and pages swap at once).
4b. **Card to info panel, then a scan.** Pressing a card (grid), a row (list) or an Info button makes the info panel's box
   grow out of that card into its place (dimming fades in); when it arrives, a sheet over it retracts from the top down,
   scanning its contents in. Plain opens (for example from the Lists button or More by author) still just rise into place.
5. **Switches** (Settings, Filters, Organize, Session Settings): the knob slides and the track colour eases; opening
   Settings does not make every knob slide.
6. **Rail:** the white bar on a newly selected row fades in.
7. **Detail panel sessions:** choosing another session fades its information in.

Not animated: closing panels (still instant).

## Dropdown text padding (2026-11-04)

18. **Sort menu rows** all use one size: the name at the left (check mark on the chosen one) and its hint ("Total visits")
    at the right edge, dimmer and smaller, with room at both edges. Every other button label that fills its button (Filters
    host choices, steppers, the Views and Start on rows, Done / Reset, chips) is inset 10 px from the left and right edge;
    check nothing now wraps that fit before.
19. **Start on.** The built-in default has been Live Now since the overhaul, but your saved setting said Browse (picked in
    Settings earlier), which is what you saw; it has been reset to Live Now. Settings > General > Start on changes it.
