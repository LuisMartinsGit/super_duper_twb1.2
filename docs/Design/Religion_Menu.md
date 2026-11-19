# The Religion Menu

> **2026-11-19: CAST ON THE LEFT, TRACK ON THE RIGHT** (developer: "Sect powers must be always
> visible, add a column to the left side with buttons for all the available powers. When they
> are in cooldown, show the remaining cooldown time clearly, when a power becomes available, it
> flashes twice so the user gets called to it. Left side column = cast, right side religion menu
> = track progress.")
>
> - **The cast column** stands on the left edge of the screen whenever the menu is unlocked and
>   at least one power is lit, whether or not the right-hand menu is open. One row per adopted
>   sect, in the vessels' order (so row N answers F-key N), led by the sect's emblem; the three
>   power columns are A, B and ★ (Shift, Ctrl), and a power not yet lit leaves its place empty.
> - **Cooling down**: the button dims under a dark sweep with the time left in large type
>   (seconds, or minutes:seconds from a minute up).
> - **Ready**: the moment a power is ready again, or first lights up, its button **flashes
>   twice**. A power already ready when the column first appears (a loaded game) does not.
> - With the Temple down every button carries the no-cast slash, as the menu's cells do.
> - **The right-hand menu tracks**: its lit power cells still show the reserve, the level and
>   the cooldown, but no hotkey, and clicking one points at its button in the column (one
>   flash and a notice) instead of casting. The hotkeys are unchanged.
> - Code: `ReligionPanelBinder.CastBar.cs`.

> **2026-11-18: LOCKED UNTIL AGE 1.** While the player's faction is still in Age 0 (no
> culture yet) the menu cannot open: the slide tab stands **dimmed** on the right edge reading
> **R / Age 1**, its tooltip says it unlocks in Age 1, clicking it does nothing and **R** only
> posts "Religion unlocks in Age 1." A Temple finished in Age 0 still pays its points (they show
> on the resource panel's Religion row); the menu waits for the age-up. From Age 1 the rules
> below apply unchanged (a finished Temple, or a sect adopted), and the first time the menu
> unlocks it slides open by itself. Code: `ReligionPanelBinder.Refresh` (`_ageLocked`).

> **2026-11-15 ([Unification decision 66](Unification_Decisions_2026-10-03.md)): THE POWER
> RESERVES. This block replaces the row layout of §2 below.** The menu reads top to bottom as
> liquid running down:
>
> - **The font** fills toward the next religion point. Beside it are the balance to spend
>   (a star and a number) and the Tithe button.
> - **Pipes** run from the font to six **vessels**, one per sect slot; a drip runs in the pipe
>   of every vessel that is still filling. A full vessel's pipe is dry and its rim is bright.
> - **A sect's three powers stand beside its vessel at the height that unlocks them** (the step
>   sizes are data, `sectStepRp`), and marks **II** and **III** on the vessel are the levels.
>   A power the liquid has reached is gold and is its cast button, with its hotkey; the power
>   that comes next has a gold border and a percentage; the rest are dim.
> - **Under each vessel**: the sect's emblem and name, the sect building button and the hero
>   button (dim until level II). The first empty vessel carries the adopt button.
> - **Footer**: the set bonus and how many of its sects are adopted.
>
> **Colour says what a thing is**: gold is faith, the liquid and whatever it has lit; a pale
> outline is something to click and place or pay for; teal is a building that stands.
> **Every button is a pictogram**; names, descriptions, states and prices are in the tooltips.
> **The panel hangs from the top of the right edge and never reaches the minimap.**
> Code: `ReligionPanelBinder.cs` (layout and refresh), `ReligionPanelBinder.Icons.cs` (the
> pictograms, drawn by the menu until the powers have art).

> **2026-11-15 ([Unification decision 63](Unification_Decisions_2026-10-03.md)): nothing is bought
> in this menu except the Tithe.** Powers and levels unlock by themselves (Religion.md, top
> block). A locked power cell is dim; **the power that comes next shows its progress as a
> percentage**, and the chapel-level badge shows the percentage when a level is what comes
> next. Clicking a locked cell or the level bars only says that it unlocks by itself. Adopting
> shows no price. **The religion tree (T, §6) is retired**: no button, no hotkey. A row's
> power cells are in UNLOCK order (Sects.md § Unlock order); F / Shift+F / Ctrl+F follow it.

> **Doc version 2026-10-15.** Canon for the in-match religion UI: the quick
> menu on the right edge and the full religion tree behind **T**. The rules it
> shows are [Religion.md](Religion.md) and [Sects.md](Sects.md); this doc says
> only how they are presented and operated. Mockups: the **Match HUD** design
> canvas (artboards 1, 4 and 5).

## 1. The quick menu

A panel docked to the **right edge, vertically centred** (where the old
Religion panel was), about 340 px wide at 1080p. It is locked while the faction is in
**Age 0** (see the 2026-11-18 note at the top), and from Age 1 appears once the player has
**finished a Temple of Ridan**, and stays from then on as long as the Temple
stands or a sect is adopted (§4). **R** shows and hides it; a
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

- **Every purchase is ONE click** (2026-11-15, developer: "most actions require 2 clicks and
  should require only one"): adopt, unlock B or ★, raise a level, Tithe and recruit happen on the
  click, with no confirm step. The price, and "Final" where it applies, are on the control's
  hover text; a purchase that cannot be made says why. (Until then each used an inline confirm.)
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
| Age 0 (no culture yet) | **only the slide tab, dimmed** ("R / Age 1", tooltip "Unlocks in Age 1."); the tab and R open nothing, F1-F6 do nothing (2026-11-18) |
| Age 1+, no standing Temple and no sect adopted | **only the slide tab, dimmed** ("R / Temple", tooltip "Build a Temple of Ridan to unlock it."); R only says so (developer, 2026-11-18) |
| Age 1, no Temple finished yet | **nothing** — no panel, no slide tab, and R / T / F1-F6 do nothing. A Temple still under construction does not count (2026-10-16) |
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
