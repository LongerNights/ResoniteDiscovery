# UI redesign step 6: Settings panel (in-game checks)

Built 2026-11-04, not yet run in game. See `docs/ui-redesign.md`.

1. **Panel.** Settings opens centred, a fixed height, with a title, a column of six tabs on the left (General,
   Display, Sidebar, Alerts, Hidden, Data) and the chosen page on the right; Done at the bottom right. The selected
   tab is cyan. Switching tabs does not change the panel's size.
2. **General.** "Start on" row shows the current lens and steps to the next when pressed; "Live lenses only" and
   "Animations" are switches that slide and turn cyan.
3. **Display.** Grid / List as two buttons (current one cyan); cards per row as 2 to 7 (current one cyan). Pressing
   changes the results behind the panel straight away.
4. **Sidebar.** Two columns of switches: Now and Discover on the left, Yours and Insights on the right, each lens
   indented under its section. A lens under a switched-off section is dimmed. Check the rail updates.
5. **Alerts.** Two switches and the rate-limit note.
6. **Hidden.** The counts line, "Hide worlds with a word in the name..." (opens the prompt) and "Restore all hidden".
7. **Data.** Export and Import work as before.

Not done from the plan: a dropdown for "Start on" (it steps instead), the Hidden list by name, and the Reduce motion
rename (comes with the animations step).
