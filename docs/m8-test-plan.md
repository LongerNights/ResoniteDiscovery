# M8 in-game test plan: Current Worlds

Install (close Resonite first; the loaded DLL is locked): `dotnet build src/Discover.Resonite -p:ResoniteInstallOnBuild=true`, start Resonite, open the ResoniteDiscovery tab.

| # | Do | Expect |
| --- | --- | --- |
| 1 | Look at the NOW section | Current Worlds is first, with a count of your open worlds (userspace is not counted) |
| 2 | Open it with three worlds open | Three cards. The world you are looking at is first, with a green "You are here" badge. Each card shows Host or Guest, the access level (Public, Registered, Contacts, Contacts+, LAN, Private), users/max and a thumbnail when the world has one |
| 3 | Open a private or LAN world you host | It is listed (Current Worlds ignores the visibility rule) and appears in no other lens |
| 4 | Press Focus world on a background world | The dash closes onto that world; no new connection. Open the tab again: that world is first now |
| 5 | Info on a world you joined | The panel has a Leave world button. Pressing it asks "Leave {world}? Unsaved changes may be lost."; confirm and the world closes and its card disappears within a couple of seconds |
| 6 | Info on a world you host | The button reads End session and the question says it sends everyone out. Cancel works |
| 7 | Info on your local home | No leave button (the engine refuses to exit the local home) |
| 8 | Star a saved-able world (published or custom session id) from its card | Favorites gains it; random-id sessions show no star, as elsewhere |
| 9 | Type in the search box | Filters the open worlds by name, host or tags; chips filter too |
| 10 | Sort menu on this lens | Every sort is disabled (this lens keeps its focus order); the Filters button shows no count |
| 11 | Join a user-count-changing world and wait | The count on the card updates within about 2 seconds without reopening |
| 12 | Check the network log | Opening Current Worlds sends no request |

Not in M8: the stock-tab takeover (M6) and the parity filters (M9). Leave is in the detail panel rather than on the card (decisions.md, 2026-11-03).
