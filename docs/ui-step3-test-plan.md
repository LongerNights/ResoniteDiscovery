# UI redesign step 3: Sort menu and Filters popover (in-game checks)

Built 2026-11-04, not yet run in game. See `docs/ui-redesign.md`.

1. **Sort menu.** Open it on Browse, Live Now and Favorites. Only sorts the tab supports are listed (no greyed rows);
   the direction pair ("↓ High to low", "↑ Low to high") is at the top and is hidden on Random. The selected sort
   has a check mark and the cyan fill. Pick a sort and the menu closes; flip the direction.
2. **Filters on Live Now / Friends.** Sections PEOPLE (min people stepper, Has room, Has VR users, Mobile friendly) and
   SESSION (min uptime stepper, host choice Anyone / A person / Headless, Include empty, Show other versions). No
   world-only rows.
3. **Filters on Browse / Featured / New / Random.** A single WORLD section (min visits stepper, Featured only).
4. **Filters on Favorites, Recent, Current Worlds.** Only the "no filters" line and Reset / Done.
5. **Switch rows.** Each row has the label left and a track and knob at the right; the knob slides to the right end
   and the track turns cyan when on. Reset turns them all off. The Filters button's count badge matches.
6. The popover is as tall as what is shown, and is still centred under its button.
