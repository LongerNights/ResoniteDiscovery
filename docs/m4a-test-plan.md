# M4a in-game test plan

M4a covers F9 (favorites), F16 (Recent), F21 (named lists) and F22 (personal tags) from the spec. The Core logic
(library, lens contents, ordering, card tags, rail) is unit tested (315 tests); nothing here has run in game yet.
Install and first-check steps are in `m2-test-plan.md`.

## First look
1. Log: `[Discover] Account data loaded` appears a second or two after login (and `Screen attached` as before).
   No `Tick failed`, `UI build failed` or `Could not save`.
2. The rail has a MINE section: Favorites, "+ New list", Recent. Cards have a star and a Lists button.

## Cases

| ID | Steps | Expected |
| --- | --- | --- |
| F9 | Press the star on a card, switch to Favorites; restart the game | The world is under Favorites, newest first (live worlds first); it survives the restart |
| F9 account | (If you have a second account) sign in with it | Its favorites are separate; yours come back when you return |
| F21 create | Rail > + New list, type "Games", OK | The list appears in the rail and opens (empty message) |
| F21 add | On a card press Lists, toggle "Games" on, Done; open the list | The world is in the list; a world can be in several lists |
| F21 manage | On the list: Rename, Move up/Move down (with two lists), Delete | Rename changes the rail label; moving changes the rail order; Delete asks first and returns to Favorites; the worlds themselves are untouched |
| F21 restart | Restart the game | The lists and their members are still there |
| F22 add | Lists > type "chill" > Add | An orange tag appears in the panel and first on the card |
| F22 filter | Search `+tag:chill` in Favorites; press the orange tag on a card | Only worlds you tagged chill; pressing the tag adds an orange chip (third press clears it) |
| F22 remove | Lists > press the orange tag | It is removed from the panel and the card |
| F16 | Join a world from here, then open Recent | It is first in Recent; joining another puts that first; the list caps at 50 |
| Hydration | Favorite a world, restart, open Favorites while it is offline | The card shows its name at once; its thumbnail and visits fill in within a moment |
| Saved but gone | Favorite a world, then unfavorite from Favorites | It disappears immediately |
| Not signed in | (Before login) open a Mine lens | "Sign in to save worlds, lists and tags." |

## Known limits
- At most five lists are shown in the rail; the organize panel shows six and five tags per world (the library allows more).
- No export/import buttons yet (the Core support exists), no settings screen, no detail drawer, no grid/list toggle (M4b).
- Search chips from a personal tag filter locally only, so on a search lens they narrow the current page, not the server query.

## Saved sessions (added after first run)

- Join a world hosted with a custom session id (e.g. an Elysium session): it appears in Recent, and its card shows a star and Lists button.
- Star it, then close the session: the Favorites card reads Offline, with no Join button.
- Sessions with random ids (no star on the card) cannot be saved, by design.
