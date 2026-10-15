# The Religion Menu

> **Doc version 2026-10-15.** Canon for the in-match religion UI: the quick
> menu on the right edge and the full religion tree behind **T**. The rules it
> shows are [Religion.md](Religion.md) and [Sects.md](Sects.md); this doc says
> only how they are presented and operated. Mockups: the **Match HUD** design
> canvas (artboards 1, 4 and 5).

## 1. The quick menu

A panel docked to the **right edge, vertically centred** (where the old
Religion panel was), about 340 px wide at 1080p. **R** shows and hides it; a
slide tab stays on the edge while it is hidden, showing the RP balance.

Top to bottom:

1. **Header** — the title, a **Tree (T)** button, a **go to Temple** button,
   and the hide button.
2. **RP line** — the balance; a progress bar toward the next RP ("curse x/y",
   "blood x/y" for Feraldis); the **Tithe** button with its current price
   (disabled with no standing Temple).
3. **Slots** — six dots, one per sect slot, filled when used, with
   "N of 6 sects · choices are final".
4. **Set passives** — four rings (Sovereign Hour, The Breaking Hour,
   Foreknown, Concord), each with its progress ("3/4", "3·1·0"). A set that
   can no longer be completed with the slots left is struck through; a
   complete set glows gold.
5. **One row per slot.**
   - **Empty slot:** a dashed "Adopt a sect" button that opens the sect
     picker (§3).
   - **Adopted sect:**
     - the sect's icon;
     - the **chapel level** as three bars — clickable to raise it, with its
       RP price as a badge while a level is still to buy;
     - the **three power cells, A / B / ★** — a bought power is its own cast
       button (§2); an unbought one carries its RP price as a badge, gold
       when affordable, red when short;
     - the **sect building chip** — not built (Build), under construction
       (progress ring), standing (go to it), destroyed (Rebuild); Build and
       Rebuild are disabled with "Needs a Temple" while there is none;
     - the **hero chip** — not recruited (Recruit), alive (its level),
       dead (Revive); both need the sect building standing;
     - a one-line caption: building · unit count `n/20` · research owned.

## 2. Casting from the menu

- A bought power cell casts on click, or on its hotkey: **F1-F6** casts
  power A of that row, **Shift+F1-F6** power B, **Ctrl+F1-F6** the wildcard
  (shown on the cell). The digits stay the control groups.
- Cooling cells show a radial sweep and the seconds left.
- After the click the cell turns gold and the cursor carries the power's
  radius ring; left-click the ground casts, right-click or Esc cancels.
- With no standing Temple every bought cell shows "cannot cast" and does
  nothing.

## 3. Buying

- Every purchase (adopt, unlock B or ★, raise a level, Tithe, recruit) uses an
  **inline confirm** on the clicked control — never a modal. Adopt and unlock
  confirms say **"Final, cannot be undone"**.
- **The sect picker** opens to the left of the menu: three culture columns of
  four sects, each with its price. The player's own culture shows the
  affinity price and "yours"; before age-up every sect shows the full price
  with "Prices drop for your culture after age-up". Adopted sects are shown
  greyed. If adopting a sect would complete a set, the picker says so.
- Hovering any control shows the shared tooltip / detail card: a power's
  radius, target, cast time, cooldown, duration and counter-play; a unit's
  role and weaknesses; a hero's abilities and the levels they unlock at; and
  what the piece pairs with.

## 4. States

| State | The menu shows |
|---|---|
| No Temple yet | only "Build a Temple of Ridan…" and a **Build Temple** button |
| Temple destroyed | everything owned, a red banner "Rebuild the Temple to cast sect powers and raise sect buildings" with **Rebuild**; cells "cannot cast"; Build/Rebuild chips and the Tithe disabled; buying unlocks and levels still allowed |
| 6 of 6 sects | no empty row; the picker cannot open |
| Spectating / allied view | the same menu read-only, with the player's name and colour in the header |

## 5. Notifications

The shared toast line announces: enough RP for an unlock the player has not
bought; a sect building destroyed ("Stonehold destroyed: rebuild it to train
Sallyguards"); the Temple destroyed ("sect powers and sect buildings are on
hold"); a set passive completed.

## 6. The religion tree (T)

A full-screen flowchart, opened by **T** or the header button, closed by **T**
or Esc:

- the **Temple** at the top, labelled as the gate for adopting, sect
  buildings and casting;
- three **culture columns**, each with its set passive and progress in the
  header and its four sects below;
- each sect card draws its chain: **Adopt → A / B / ★** (B and ★ both hang
  from adoption) **→ level II → III**, and below it the **sect building →
  unit, research** and the **hero**;
- **Concord** spans the three columns at the bottom;
- node states: owned (cyan), can buy (gold badge), short of RP (red badge),
  not adopted (dim);
- hovering a node opens its detail card and **highlights the pieces it pairs
  with** (Sects.md "pairs with").

Everything bought from the quick menu can also be bought here.
