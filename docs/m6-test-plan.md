# M6 test plan: tab takeover

Install: close Resonite, `dotnet build src/Discover.Resonite -p:ResoniteInstallOnBuild=true`, start the game. Nothing here has run in game yet.

| # | Check | Expect |
| --- | --- | --- |
| 1 | Open the dash after startup | One "Worlds" tab with the stock Worlds icon; no second Worlds-like button and no gap in the button strip (V32, confirm by eye) |
| 2 | Start the game with the stock Worlds tab as the last open tab | The dash opens on our screen, not the stock one |
| 3 | Press Classic browser (footer) | The stock Worlds screen opens; its button stays hidden |
| 4 | From Classic, press another tab, then Worlds | Our screen opens; Classic mode is over |
| 5 | Set `showStockWorldsTab` to true in `settings.json`, restart | Both tabs visible; ours is labelled "Galaxy Sieve" with the galaxy icon |
| 6 | Set `galaxyTabIcon` to true, restart | Our "Worlds" tab uses the galaxy icon |
| 7 | Trigger a failure (see below), or wait for "paused" | The stock Worlds button comes back immediately (F25) |
| 8 | Kill the game with the takeover active, restart without the mod (remove the DLL) | The stock Worlds tab is present and works (nothing persisted) |
| 9 | Log in or out (workspace reload) | Our tab is re-attached and the stock button is hidden again; no flash of both |
| 10 | Open Info on a published world, press More by author | Browse shows only that owner's worlds, title reads "Browse · by <name>"; picking any rail lens clears it |

Open: V31 (label driven from the locale key) is not done; the label is the literal "Worlds". Check 7 can be forced by temporarily making `BuildUi` throw; it then must leave the stock tab visible.
