# M4b in-game test plan

Install, restart Resonite, open the ResoniteDiscovery tab.

| # | Do | Expect |
| --- | --- | --- |
| 1 | Click My Worlds (Mine section) | Your published worlds, newest update first. Empty account shows "You have not published any worlds yet." |
| 2 | Click Group Worlds | One group opens directly; several show a choice panel (first four by name). The header reads "Group Worlds · <name>" and lists that group's worlds |
| 3 | Group Worlds when in no group | Toast "You are not in any groups." |
| 4 | Press Info on a card | Detail panel: title, by line, description, tags, sessions, device mix, Join/Focus world, Lists, Close |
| 5 | Detail > Lists, Join | Opens the organize panel / the join confirmation |
| 6 | Settings (top bar) | Panel with Animations, Live lenses only, Start on, Export, Import. Changes apply at once; they are saved per account (`settings.json`) |
| 7 | Export, then Import | `exports\resonite-discovery-export.json` appears under `%APPDATA%\ResoniteDiscovery\<account>\`; Import reports what it added (nothing new when re-importing) |
| 8 | Change "Start on", restart | The screen opens on that lens |

| 9 | Settings > "Results: Grid" (press to switch to List) | Results become one row per world: thumbnail left, title/host/tags in the middle, Info, Lists, star, and the action button on the right. The choice is saved (`listView` in `settings.json`) |
| 10 | Info > Copy link on a published world, then paste in a text field | `resrec:///<owner>/<record>` (the record link the inventory's own copy gives). Not shown for sessions with no record |

Not in M4b (deferred): user names per session in the detail panel. The "paused" banner (F25) was built in M2; the attach failsafe was verified then.
