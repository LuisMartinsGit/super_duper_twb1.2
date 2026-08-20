# Lobby Setup — colours and start positions

**Status:** canonical. Covers how a player picks their colour and where on the
map each player begins. Team assignment itself lives in [Teams.md](Teams.md).

---

## 1. Colour picking

Every slot — human or AI — owns a colour drawn from the fixed 12-colour pool in
`FactionColors.ColorPool`. Colour is identity, not a game effect: it never
changes on culture selection and carries no balance meaning.

**The colour swatch on a roster row opens a picker.** It was a click-to-cycle
control, which meant reaching a specific colour took up to eleven clicks and
gave no view of what was available.

The picker shows all twelve colours as a grid of swatches with their names:

- the slot's **current** colour is marked
- colours **already taken by another slot** are shown struck through and are
  not selectable — two players sharing a colour is unreadable on the minimap
- picking a colour closes the picker immediately
- clicking outside it, or pressing Escape, closes it without changing anything

Colour remains unique per slot. Adding or removing slots still resolves
conflicts automatically, and the host still controls AI colours.

In multiplayer a player may change **their own** colour; the host may change
anyone's. The choice travels as an explicit "set colour to index N" rather than
a "cycle" instruction, so the sender and receiver cannot disagree about the
result.

---

## 2. Start positions

Maps author their spawn points as `PlayerStartMarker` objects. The lobby's map
preview draws them, and **clicking one assigns a player to it**.

### Assignment

1. Click a roster row to select that player.
2. Click a start position on the map preview to place them there.

A start position shows the colour and number of whoever holds it, and is hollow
when free. Clicking the start a player already holds releases it. Assigning a
player to a start that is taken moves them there and releases the previous
holder — a start position holds exactly one player.

Any player left unassigned spawns wherever the automatic layout puts them,
which is the behaviour when nobody touches the map at all. **No assignment is
required**: an untouched lobby behaves exactly as before.

Because a player picks a *position*, not a faction, this is how allies arrange
themselves — putting a team on adjacent starts, or splitting them across the
map, is the point of the feature.

### The index contract

`PlayerSlot.StartIndex` indexes the map's start positions. That number has to
mean the same thing in two places that never see each other:

| Where | What it reads |
|---|---|
| Lobby | `MapInfo.PlayerStarts` — normalized 0..1 dots baked from the map scene |
| Match | `MapMarkerRegistry.PlayerStarts` — live `PlayerStartMarker` objects |

These two orderings **disagreed**. `MapMarkerRegistry` sorted markers
(faction, then name, then position) but `MapInfoBaker` baked them in Unity's
unordered `FindObjectsByType` order — on Sundered Crown, baked index 0 was
Green while runtime index 0 was Blue. Picking the north-west start would have
spawned the player in the south-east.

Two things fix it, and both are required:

1. **One shared comparer.** `MapMarkerRegistry.ComparePlayerStarts` is the
   canonical order, and the baker now sorts by it before baking.
2. **Identity, not position.** The baker also writes `PlayerStartFactions`
   parallel to `PlayerStarts`, so the match resolves a chosen start to a marker
   by *which marker it is* rather than by where it sits in an array. Positional
   indexing survives only as a fallback for assets baked before that field
   existed.

> **Re-bake required.** MapInfo assets baked before this change carry the old
> order and no faction array. Every map must be re-baked
> (`Waning Border > Maps > Bake Map Info From Open Scene`) before its start
> picker is trustworthy.

### Resolution order at spawn

Explicit choices are reserved **before** anyone is auto-assigned, because the
spawn loop runs in slot order — otherwise slot 0 falling through to the
"first unused marker" rule could take the marker slot 3 had chosen, and the
player's explicit choice would silently lose.

1. **Reserved** — every start chosen in the lobby, claimed up front
2. **Faction match** — a marker whose `Faction` field matches the slot
3. **First unused marker**, in canonical order
4. **Procedural layout** (circle / two-sides) for anyone left over

A choice that cannot be honoured — the map changed, the marker is gone, two
slots somehow claim one start — logs a warning and falls back to automatic
assignment rather than failing the match.
