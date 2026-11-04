# UI redesign step 5: Session Settings (in-game checks)

Built 2026-11-04, not yet run in game. See `docs/ui-redesign.md`.

Open it from a published world with no live session (New session).

1. The panel is centred and as tall as its content: title, the world's name, WHO CAN JOIN, the seven rows, OPTIONS,
   then Cancel and Start Session (the main button, on the right).
2. Each access row shows a ring, with a dot and the cyan fill on the chosen row (Default at first), and the name
   followed by a short grey hint ("Listed publicly", "Your contacts only", ...). Pressing a row moves the dot.
3. OPTIONS has a Mobile friendly switch (knob slides, track turns cyan). It is not mistaken for an access level.
4. Start Session still opens the session with the chosen access level (and mobile friendly when on); Cancel and
   pressing outside close the panel without starting.
5. The hint text is readable and nothing wraps or clips on the narrowest rows ("Contacts and their contacts").

Not done from the plan: opening this inside the detail panel instead of as its own panel.
