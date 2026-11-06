# UI redesign step 4: Classic browser button and detail panel (in-game checks)

Built 2026-11-04, not yet run in game. See `docs/ui-redesign.md`.

1. **Classic browser** is a quiet button pinned to the bottom of the left rail (no longer in the footer). It still opens
   the stock Worlds tab. Check it does not overlap the last rail row with Insights on and a few named lists.
2. **Footer** has only the status text and the Previous / Next buttons.
3. **Detail panel** (press any card): centred, as tall as its content.
   - The world's 360 preview at the left; hovering pans it (as on the card); a world with no thumbnail shows the
     engine's "no thumbnail" picture.
   - Title, byline, description and device mix at the right.
   - TAGS: up to ten chips; pressing one closes the panel and adds it to the tag chips on the results. Hidden when
     the world has no tags.
   - SESSIONS text list, then the action rows: the main action (Join / Focus world / New session) with Favorite,
     Lists and Copy link; below, More by author, Hide..., Leave (when open) and Close.
4. Every action still works: Favorite toggles its label, Lists opens the organize panel, Copy link copies, Hide...
   asks, Leave / End ends or leaves an open world.
5. Check a world with a long name and long description, and a standalone session with no record.
