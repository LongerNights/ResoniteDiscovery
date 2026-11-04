# Animations and the animation speed setting (in-game checks)

Built 2026-11-04, not yet run in game. Settings > General has **Animations** (on/off) and **Animation speed**
(Slow / Normal / Fast = 0.6x / 1x / 1.6x). Every duration goes through `UiMotion` (`src/Discover.Resonite/Ui/UiMotion.cs`).

1. **Speed.** Change the speed and open the detail panel again: Slow is clearly slower, Fast quicker. Animations off:
   everything appears instantly (and nothing is left half-moved).
2. **Modals** (detail panel, Settings, Organize, name prompts, Session Settings, confirm panels): the panel rises into
   place with an ease-out and the dimming behind it fades in.
3. **Dropdowns** (Sort, Filters, Views, Start on): they drop into place with a small rebound.
4. **Switching tabs:** the title fades in; the cards' text fades in as the cards fill (a soft cascade because cards fill
   a few per frame). Check the game does not stutter more than before when switching tabs (cards now start two small
   fades each); if it does, tell me and I will drop the card fade.
5. **Switches** (Settings, Filters, Organize, Session Settings): the knob slides and the track colour eases; opening
   Settings does not make every knob slide.
6. **Rail:** the white bar on a newly selected row fades in.
7. **Detail panel sessions:** choosing another session fades its information in.

Not animated: closing panels (still instant).
