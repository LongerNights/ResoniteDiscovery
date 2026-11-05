# M7 in-game test plan: rail and Browse

Build and install: `dotnet build src/Discover.Resonite -p:ResoniteInstallOnBuild=true`, restart Resonite, open the ResoniteDiscovery tab.
Not covered yet (later milestones): tab takeover (M6), Current Worlds (M8), the Filters rows from the stock Custom panel (M9).

| # | Do | Expect |
| --- | --- | --- |
| 1 | Look at the rail | Sections read NOW, DISCOVER, YOURS. DISCOVER lists Browse, Featured, New, Random (no Most Visited) |
| 2 | Open Browse with no chips | Published worlds, most visits first. The chip bar shows Social, Games, Avatars, Art, Tutorials, Educational, then popular tags from the page, then your personal tags (orange) |
| 3 | Press Games | The Games chip lights purple with "+"; results are Games-tagged worlds, most visited first (F29: 2 clicks from opening the tab). Clear all appears |
| 4 | Press Games again, then again | Excluded (red, "−"), then cleared |
| 5 | Sort menu on Browse | Visits is selected; the direction toggle flips it (F14) |
| 6 | Other lenses | The chip bar shows only the chips you set, as before |
| 7 | Settings > rail | Sections are named Now, Discover, Yours; hiding Discover removes Browse, Featured, New, Random and sends no search request |
| 8 | Drop an M4b `settings.json` with `"defaultLens":"MostVisited"` and `"hiddenSections":["Mine"]` into `%APPDATA%\ResoniteDiscovery\<account>\` | Tab opens on Browse; Yours is hidden; favorites, lists and tags are unchanged (F34) |

The six category tags are the ones the stock buttons send (V24): `social`, `game`, `avatar`, `art`, `tutorial`, `educational`. If a chip returns nothing, change `browseCategories` in `settings.json`.
