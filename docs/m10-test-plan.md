# M10 test plan: history and Insights (first slice)

Not run in game yet.

| # | Check | Expect |
| --- | --- | --- |
| 1 | Settings, turn the Insights section on | INSIGHTS appears in the rail with Trending, Rising and Hidden Gems; check the rail still fits |
| 2 | Open Trending right after a fresh install | The empty message says history builds while the game runs |
| 3 | Leave the game running 1 to 2 hours with the tab opened now and then, then open Trending and Rising | Busy sessions are ranked; the subtitle says "as seen by this client" |
| 4 | Quit and restart | `history.json` exists in the account folder under `%APPDATA%` and the lenses populate again without waiting |
| 5 | Open `history.json` | Only world keys, counts and timestamps; no names, hosts or URLs (rule 5) |
| 6 | Hidden Gems | Lists worlds under 5,000 visits published in the last 180 days that were live recently; empty is normal until visit counts have been seen on two different days |
