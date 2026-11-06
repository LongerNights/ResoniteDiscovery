# Step 11 in-game checklist: orbs, join without focusing, hidden-by-filters notice (2026-11-06)

1. **Session orb.** Open the detail panel of a world with a live session. "Get session orb" sits in the row under the main
   buttons. Press it: an orb appears in front of you (no toast) in the focused world, named for the session, showing its thumbnail and
   user count. Touching it joins the session. With two sessions, pick the second in the list first: the orb is for that one.
2. **World orb.** On a published world (live or not) press "Get world orb": an orb for the world appears in front of you.
   The button is absent for entries without a world record.
3. **Orb with no focus world / ended session.** Press "Get session orb" right as the session ends: the toast says the orb
   could not be made, no error in the log.
4. **Join without focusing.** On a joinable session press "Join without focusing": no toast; the load
   indicator appears, the world opens in the background and the view you are in does not change. Check it is listed under
   Current Worlds afterwards. It is disabled for a full session.
5. **Already open.** Press it for a session you already have open: the toast says it is already open and nothing else happens.
6. **Blocked host.** Joining a host you have a mutual block with fails with the engine's own message (same path as Join).
7. **Hidden by filters.** In Live Now with Filters at default, search for a world that is open with nobody in it. The empty
   state reads "No worlds match" with "1 matching world is hidden by your filters..." and a "Show hidden worlds" button.
   Press it (the only button, no separate Clear filters): the world shows, the Filters badge counts only Include empty, other
   filters you had set stay, the search text stays.
8. **No false notice.** Search for gibberish: the normal "Try fewer filters" hint, no Show hidden button. With nothing typed
   and no results, no Show hidden button either.
9. **Friends lens.** With Min users set so a friend's session is hidden, search its name in Friends: the notice appears and
   the button lifts the filter.
10. **Focus world.** A world you have open shows "Focus world" (not "Go there") on its card and in the detail panel.
